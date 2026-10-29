using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Keyboard = System.Windows.Input.Keyboard;
using Key = System.Windows.Input.Key;
using Cursors = System.Windows.Input.Cursors;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace KmlScopedEditor.Views;

/// <summary>
/// Lightweight drawing surface for interactive LineString editing. Geometry is
/// cached in world coordinates so mouse movement redraws only cached paths plus
/// the active stroke rather than rebuilding every contour vertex.
/// </summary>
public sealed class LineEditorSurface : FrameworkElement
{
    private readonly DrawingVisual _baseVisual = new();
    private readonly DrawingVisual _selectionVisual = new();
    private readonly DrawingVisual _overlayVisual = new();
    private readonly VisualCollection _visualLayers;

    private IReadOnlyList<KmlLineFeature> _features =
        Array.Empty<KmlLineFeature>();

    private IReadOnlyList<LineEditorGuideVisual> _guides =
        Array.Empty<LineEditorGuideVisual>();

    private IReadOnlyList<Point> _cutPoints = Array.Empty<Point>();
    private IReadOnlyList<Point> _appliedCutPoints = Array.Empty<Point>();
    private IReadOnlyList<Point> _draftCutPoints = Array.Empty<Point>();
    private Point? _draftCutPreviewPoint;
    private List<Point> _editableGuidePoints = new();
    private int _dragGuideVertexIndex = -1;
    private readonly Dictionary<XElement, StreamGeometry> _featureGeometry = new();
    private readonly List<(LineEditorGuideVisual Guide, StreamGeometry Geometry)> _guideGeometry = new();

    private DrawingGroup? _baseLayer;
    private double _renderScaleBucket = double.NaN;
    private bool _renderCacheDirty = true;

    private Pen? _normalPen;
    private Pen? _selectedPen;
    private Pen? _softSelectedPen;
    private Pen? _hoverPen;
    private Pen? _activeSplitLinePen;
    private Pen? _appliedSplitLinePen;
    private Pen? _draftSplitPen;
    private Pen? _selectStrokePen;
    private Pen? _splitStrokePen;
    private SolidColorBrush? _splitVertexBrush;
    private SolidColorBrush? _splitPointBrush;
    private SolidColorBrush? _appliedSplitPointBrush;

    private HashSet<XElement> _selected = new();
    private bool _selectionIsSoft;
    private XElement? _hovered;
    private Rect _worldBounds = Rect.Empty;

    private Point _worldCenter;
    private double _scale = 1d;
    private bool _fitPending = true;

    private bool _drawingStroke;
    private bool _cutClickPending;
    private bool _leftPan;
    private bool _rightPanPending;
    private bool _rightPanMoved;
    private Point _rightPanStartScreen;
    private readonly List<Point> _strokeWorld = new();
    private Point _strokeStartScreen;
    private Point _lastStrokeScreen;

    private bool _panning;
    private Point _lastPanScreen;
    private Point _lastHoverScreen;
    private bool _hasHoverScreen;

    public LineEditorSurface()
    {
        _visualLayers = new VisualCollection(this)
        {
            _baseVisual,
            _selectionVisual,
            _overlayVisual
        };

        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;

        Loaded += (_, _) =>
        {
            if (_fitPending)
                FitView();
        };

        SizeChanged += (_, _) =>
        {
            if (_fitPending)
                FitView();
            else
            {
                RefreshViewLayers();
                InvalidateVisual();
            }
        };
    }

    protected override int VisualChildrenCount =>
        _visualLayers.Count;

    protected override Visual GetVisualChild(int index)
    {
        if (index < 0 || index >= _visualLayers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _visualLayers[index];
    }

    public LineEditorTool Tool { get; set; } = LineEditorTool.Select;

    public event EventHandler<LineEditorStrokeEventArgs>? StrokeCompleted;
    public event EventHandler<LineEditorStrokeEventArgs>? SplitStrokePreviewChanged;
    public event EventHandler<LineEditorClickEventArgs>? LineClicked;
    public event EventHandler<LineEditorPointerEventArgs>? PointerMoved;
    public event EventHandler? PointerExited;
    public event EventHandler<LineEditorContextEventArgs>? ContextRequested;
    public event EventHandler<LineEditorGuideEditEventArgs>? EditableGuideChanged;

    public void SetDocument(
        IReadOnlyList<KmlLineFeature> features,
        IEnumerable<XElement> selected,
        Rect worldBounds,
        IReadOnlyList<LineEditorGuideVisual>? guides = null,
        IReadOnlyList<Point>? cutPoints = null,
        bool fitView = false)
    {
        var previousFeatures = _features;

        _features = features ?? Array.Empty<KmlLineFeature>();
        _selected = selected?.ToHashSet() ?? new HashSet<XElement>();
        _worldBounds = worldBounds;
        _guides = guides ?? Array.Empty<LineEditorGuideVisual>();
        _cutPoints = cutPoints ?? Array.Empty<Point>();

        if (CanIncrementallyUpdateRenderCache(fitView))
        {
            UpdateChangedFeatureGeometry(previousFeatures);
            RebuildGuideGeometry();
            RebuildBaseLayerFromCachedGeometry();
            RedrawSelectionLayer();
            RedrawOverlayLayer();
            UpdateLayerTransforms();
        }
        else
        {
            InvalidateRenderCache();
            EnsureRenderCache();
        }

        if (_hovered is not null &&
            !_features.Any(feature =>
                ReferenceEquals(
                    feature.LineStringElement,
                    _hovered)))
        {
            _hovered = null;
        }

        if (fitView)
        {
            _fitPending = true;

            if (IsLoaded)
                FitView();
        }

        // SetDocument may replace the initial empty surface after OnRender has
        // already painted the "No KML LineStrings" message. Always invalidate
        // the FrameworkElement itself so that stale background text is cleared.
        InvalidateVisual();
    }

    public void SetSelection(IEnumerable<XElement> selected)
    {
        _selected = selected?.ToHashSet() ?? new HashSet<XElement>();
        EnsureRenderCache();
        RedrawSelectionLayer();
    }

    public void SetSelectionSoft(bool isSoft)
    {
        if (_selectionIsSoft == isSoft)
            return;

        _selectionIsSoft = isSoft;
        EnsureRenderCache();
        RedrawSelectionLayer();
    }

    public void SetAppliedCutPoints(
        IReadOnlyList<Point> cutPoints)
    {
        _appliedCutPoints =
            cutPoints ?? Array.Empty<Point>();
        EnsureRenderCache();
        RedrawOverlayLayer();
    }

    public void SetCutPoints(
        IReadOnlyList<Point> cutPoints)
    {
        _cutPoints = cutPoints ?? Array.Empty<Point>();
        EnsureRenderCache();
        RedrawOverlayLayer();
    }

    public void SetDraftCut(
        IReadOnlyList<Point> points,
        Point? previewPoint)
    {
        _draftCutPoints = points ?? Array.Empty<Point>();
        _draftCutPreviewPoint = previewPoint;
        EnsureRenderCache();
        RedrawOverlayLayer();
    }

    public void SetEditableGuide(
        IReadOnlyList<Point>? points)
    {
        _editableGuidePoints = points?.ToList() ?? new List<Point>();
        _dragGuideVertexIndex = -1;
        EnsureRenderCache();
        RedrawOverlayLayer();
    }

    public void SetHovered(XElement? hovered)
    {
        if (ReferenceEquals(_hovered, hovered))
            return;

        _hovered = hovered;
        EnsureRenderCache();
        RedrawSelectionLayer();
    }

    public double WorldToleranceFromPixels(double pixels)
    {
        if (_scale <= 0d || !double.IsFinite(_scale))
            return 0d;

        return Math.Max(0d, pixels) / _scale;
    }

    public void FitView()
    {
        FitBounds(_worldBounds);
    }

    public void ZoomBy(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0d)
            return;

        _scale = Math.Clamp(
            _scale * factor,
            0.000001d,
            1000000000d);

        _fitPending = false;
        RefreshViewLayers();
    }

    public bool ZoomToSelection()
    {
        var selectedFeatures = _features
            .Where(feature =>
                _selected.Contains(feature.LineStringElement))
            .ToList();

        if (selectedFeatures.Count == 0)
            return false;

        var bounds = Rect.Empty;

        foreach (var feature in selectedFeatures)
        {
            if (bounds.IsEmpty)
                bounds = feature.WorldBounds;
            else
                bounds.Union(feature.WorldBounds);
        }

        FitBounds(bounds);
        return true;
    }

    private void FitBounds(Rect bounds)
    {
        if (!IsLoaded ||
            ActualWidth <= 0d ||
            ActualHeight <= 0d ||
            bounds.IsEmpty)
        {
            return;
        }

        var width = Math.Max(bounds.Width, 0.000000001d);
        var height = Math.Max(bounds.Height, 0.000000001d);
        var usableWidth = Math.Max(ActualWidth - 40d, 20d);
        var usableHeight = Math.Max(ActualHeight - 40d, 20d);

        _scale = Math.Min(
            usableWidth / width,
            usableHeight / height);

        if (!double.IsFinite(_scale) || _scale <= 0d)
            _scale = 1d;

        _worldCenter = new Point(
            bounds.Left + (bounds.Width / 2d),
            bounds.Top + (bounds.Height / 2d));

        _fitPending = false;
        RefreshViewLayers();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        drawingContext.DrawRectangle(
            Brushes.White,
            null,
            new Rect(0d, 0d, ActualWidth, ActualHeight));

        EnsureRenderCache();
        UpdateLayerTransforms();

        if (_features.Count == 0 &&
            _guides.Count == 0)
        {
            var text = new FormattedText(
                "No KML LineStrings were found.",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                16d,
                Brushes.Gray,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(
                text,
                new Point(
                    Math.Max(20d, (ActualWidth - text.Width) / 2d),
                    Math.Max(20d, (ActualHeight - text.Height) / 2d)));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        Focus();
        var screen = e.GetPosition(this);

        if (Tool == LineEditorTool.Pan ||
            Keyboard.IsKeyDown(Key.Space))
        {
            _leftPan = true;
            BeginPan(screen);
            e.Handled = true;
            return;
        }

        if (Tool == LineEditorTool.Split &&
            _editableGuidePoints.Count > 0)
        {
            var tolerance = WorldToleranceFromPixels(9d);
            var world = ScreenToWorld(screen);
            var nearest = FindNearestEditableGuideVertex(
                world,
                tolerance);

            if (nearest >= 0)
            {
                CaptureMouse();
                _dragGuideVertexIndex = nearest;
                Cursor = Cursors.SizeAll;
                e.Handled = true;
                return;
            }
        }

        if (Tool == LineEditorTool.Split)
        {
            CaptureMouse();
            _cutClickPending = true;
            _strokeWorld.Clear();
            _strokeStartScreen = screen;
            _lastStrokeScreen = screen;
            _strokeWorld.Add(ScreenToWorld(screen));
            e.Handled = true;
            return;
        }

        CaptureMouse();

        _drawingStroke = true;
        _strokeWorld.Clear();

        _strokeStartScreen = screen;
        _lastStrokeScreen = screen;
        _strokeWorld.Add(ScreenToWorld(screen));

        RedrawOverlayLayer();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var screen = e.GetPosition(this);
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (_leftPan)
        {
            _leftPan = false;
            EndPan();
            e.Handled = true;
            return;
        }

        if (_dragGuideVertexIndex >= 0)
        {
            var index = _dragGuideVertexIndex;
            _dragGuideVertexIndex = -1;
            ReleaseMouseCapture();

            EditableGuideChanged?.Invoke(
                this,
                new LineEditorGuideEditEventArgs(
                    _editableGuidePoints.ToList(),
                    index,
                    isComplete: true));

            Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        if (Tool == LineEditorTool.Split && _cutClickPending)
        {
            _cutClickPending = false;
            ReleaseMouseCapture();

            var splitGestureDistance = (screen - _strokeStartScreen).Length;

            if (splitGestureDistance < 4d)
            {
                LineClicked?.Invoke(
                    this,
                    new LineEditorClickEventArgs(
                        Tool,
                        ScreenToWorld(screen),
                        alt,
                        shift,
                        e.ClickCount >= 2));
            }
            else
            {
                if ((screen - _lastStrokeScreen).Length >= 1d)
                    _strokeWorld.Add(ScreenToWorld(screen));

                if (_strokeWorld.Count >= 2)
                {
                    StrokeCompleted?.Invoke(
                        this,
                        new LineEditorStrokeEventArgs(
                            Tool,
                            _strokeWorld.ToList(),
                            alt));
                }
            }

            _strokeWorld.Clear();
            RedrawOverlayLayer();
            e.Handled = true;
            return;
        }

        if (!_drawingStroke)
            return;

        var clickDistance = (screen - _strokeStartScreen).Length;

        _drawingStroke = false;
        ReleaseMouseCapture();

        if (clickDistance < 4d)
        {
            LineClicked?.Invoke(
                this,
                new LineEditorClickEventArgs(
                    Tool,
                    ScreenToWorld(screen),
                    alt,
                    shift,
                    e.ClickCount >= 2));
        }
        else
        {
            if ((screen - _lastStrokeScreen).Length >= 1d)
                _strokeWorld.Add(ScreenToWorld(screen));

            if (_strokeWorld.Count >= 2)
            {
                StrokeCompleted?.Invoke(
                    this,
                    new LineEditorStrokeEventArgs(
                        Tool,
                        _strokeWorld.ToList(),
                        alt));
            }
        }

        _strokeWorld.Clear();
        RedrawOverlayLayer();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        Focus();
        CaptureMouse();

        _rightPanPending = true;
        _rightPanMoved = false;
        _rightPanStartScreen = e.GetPosition(this);

        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);

        var screen = e.GetPosition(this);

        if (_rightPanMoved)
        {
            _rightPanPending = false;
            _rightPanMoved = false;
            EndPan();
            e.Handled = true;
            return;
        }

        _rightPanPending = false;

        if (!_drawingStroke &&
            _dragGuideVertexIndex < 0)
        {
            ReleaseMouseCapture();
        }

        ContextRequested?.Invoke(
            this,
            new LineEditorContextEventArgs(
                ScreenToWorld(screen)));

        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.ChangedButton == MouseButton.Middle)
        {
            BeginPan(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton == MouseButton.Middle)
        {
            EndPan();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var screen = e.GetPosition(this);

        if (_dragGuideVertexIndex >= 0 &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            var world = ScreenToWorld(screen);
            _editableGuidePoints[_dragGuideVertexIndex] = world;

            EditableGuideChanged?.Invoke(
                this,
                new LineEditorGuideEditEventArgs(
                    _editableGuidePoints.ToList(),
                    _dragGuideVertexIndex,
                    isComplete: false));

            RedrawOverlayLayer();
            e.Handled = true;
            return;
        }

        if (_rightPanPending &&
            e.RightButton == MouseButtonState.Pressed &&
            !_rightPanMoved &&
            (screen - _rightPanStartScreen).Length >= 4d)
        {
            _rightPanMoved = true;
            BeginPan(_rightPanStartScreen);
        }

        if (_panning)
        {
            var delta = screen - _lastPanScreen;
            _lastPanScreen = screen;

            if (_scale > 0d)
            {
                _worldCenter = new Point(
                    _worldCenter.X - (delta.X / _scale),
                    _worldCenter.Y + (delta.Y / _scale));

                UpdateLayerTransforms();
            }

            e.Handled = true;
            return;
        }

        if (Tool == LineEditorTool.Split &&
            _cutClickPending &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            if ((screen - _lastStrokeScreen).Length < 5d)
                return;

            _lastStrokeScreen = screen;
            _strokeWorld.Add(ScreenToWorld(screen));

            SplitStrokePreviewChanged?.Invoke(
                this,
                new LineEditorStrokeEventArgs(
                    Tool,
                    _strokeWorld.ToList(),
                    isDeselect: false));

            RedrawOverlayLayer();
            e.Handled = true;
            return;
        }

        if (_drawingStroke && e.LeftButton == MouseButtonState.Pressed)
        {
            if ((screen - _lastStrokeScreen).Length < 3d)
                return;

            _lastStrokeScreen = screen;
            _strokeWorld.Add(ScreenToWorld(screen));
            RedrawOverlayLayer();
            e.Handled = true;
            return;
        }

        if (!_hasHoverScreen ||
            (screen - _lastHoverScreen).Length >= 4d)
        {
            _lastHoverScreen = screen;
            _hasHoverScreen = true;

            PointerMoved?.Invoke(
                this,
                new LineEditorPointerEventArgs(ScreenToWorld(screen)));
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hasHoverScreen = false;
        SetHovered(null);
        PointerExited?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (_features.Count == 0 &&
            _guides.Count == 0)
        {
            return;
        }

        var screen = e.GetPosition(this);
        var worldBefore = ScreenToWorld(screen);

        var factor = e.Delta > 0 ? 1.2d : (1d / 1.2d);
        _scale = Math.Clamp(_scale * factor, 0.000001d, 1000000000d);

        var worldAfter = ScreenToWorld(screen);

        _worldCenter = new Point(
            _worldCenter.X + (worldBefore.X - worldAfter.X),
            _worldCenter.Y + (worldBefore.Y - worldAfter.Y));

        _fitPending = false;
        RefreshViewLayers();
        e.Handled = true;
    }

    private void DrawBaseLayer()
    {
        using var context = _baseVisual.RenderOpen();

        if (_baseLayer is not null)
            context.DrawDrawing(_baseLayer);
    }

    private void RedrawSelectionLayer()
    {
        using var context = _selectionVisual.RenderOpen();

        var selectionPen =
            _selectionIsSoft
                ? _softSelectedPen
                : _selectedPen;

        if (selectionPen is not null)
        {
            foreach (var element in _selected)
            {
                if (_featureGeometry.TryGetValue(
                        element,
                        out var geometry))
                {
                    context.DrawGeometry(
                        null,
                        selectionPen,
                        geometry);
                }
            }
        }

        if (_hovered is not null &&
            !_selected.Contains(_hovered) &&
            _hoverPen is not null &&
            _featureGeometry.TryGetValue(
                _hovered,
                out var hoveredGeometry))
        {
            context.DrawGeometry(
                null,
                _hoverPen,
                hoveredGeometry);
        }
    }

    private void RedrawOverlayLayer()
    {
        using var context = _overlayVisual.RenderOpen();

        var safeScale = Math.Max(
            double.IsFinite(_renderScaleBucket)
                ? _renderScaleBucket
                : _scale,
            0.000001d);

        foreach (var item in _guideGeometry)
        {
            var splitLinePen = item.Guide.Applied
                ? _appliedSplitLinePen
                : _activeSplitLinePen;

            if (splitLinePen is not null)
            {
                context.DrawGeometry(
                    null,
                    splitLinePen,
                    item.Geometry);
            }
        }

        if (_editableGuidePoints.Count >= 2)
        {
            context.DrawGeometry(
                null,
                _draftSplitPen,
                BuildGeometry(_editableGuidePoints));

            var handleRadius = 5.0d / safeScale;

            foreach (var point in _editableGuidePoints)
            {
                context.DrawEllipse(
                    _splitVertexBrush,
                    _hoverPen,
                    point,
                    handleRadius,
                    handleRadius);
            }
        }

        if (_draftCutPoints.Count > 0)
        {
            var draftPoints = _draftCutPoints.ToList();

            if (_draftCutPreviewPoint.HasValue)
                draftPoints.Add(_draftCutPreviewPoint.Value);

            if (draftPoints.Count >= 2)
            {
                context.DrawGeometry(
                    null,
                    _draftSplitPen,
                    BuildGeometry(draftPoints));
            }

            var vertexRadius = 4.2d / safeScale;

            foreach (var point in _draftCutPoints)
            {
                context.DrawEllipse(
                    _splitVertexBrush,
                    null,
                    point,
                    vertexRadius,
                    vertexRadius);
            }
        }

        if (_appliedCutPoints.Count > 0)
        {
            var radius = 1.9d / safeScale;

            foreach (var point in _appliedCutPoints)
            {
                context.DrawEllipse(
                    _appliedSplitPointBrush,
                    null,
                    point,
                    radius,
                    radius);
            }
        }

        if (_cutPoints.Count > 0)
        {
            var radius = 2.6d / safeScale;

            foreach (var point in _cutPoints)
            {
                context.DrawEllipse(
                    _splitPointBrush,
                    null,
                    point,
                    radius,
                    radius);
            }
        }

        if (_strokeWorld.Count >= 2)
        {
            var strokePen = Tool == LineEditorTool.Split
                ? _splitStrokePen
                : _selectStrokePen;

            if (strokePen is not null)
            {
                context.DrawGeometry(
                    null,
                    strokePen,
                    BuildGeometry(_strokeWorld));
            }
        }
    }

    private void RefreshViewLayers()
    {
        EnsureRenderCache();
        UpdateLayerTransforms();
    }

    private void UpdateLayerTransforms()
    {
        var transform = CreateWorldTransform();

        _baseVisual.Transform = transform;
        _selectionVisual.Transform = transform;
        _overlayVisual.Transform = transform;
    }

    private bool CanIncrementallyUpdateRenderCache(
        bool fitView)
    {
        return !fitView &&
               !_renderCacheDirty &&
               double.IsFinite(_renderScaleBucket) &&
               _normalPen is not null &&
               _baseLayer is not null;
    }

    private void UpdateChangedFeatureGeometry(
        IReadOnlyList<KmlLineFeature> previousFeatures)
    {
        var previousByElement = previousFeatures
            .ToDictionary(
                feature => feature.LineStringElement,
                feature => feature);

        var currentElements = _features
            .Select(feature => feature.LineStringElement)
            .ToHashSet();

        foreach (var removed in _featureGeometry.Keys
                     .Where(element =>
                         !currentElements.Contains(element))
                     .ToList())
        {
            _featureGeometry.Remove(removed);
        }

        var simplifyTolerance =
            0.55d / Math.Max(
                _renderScaleBucket,
                0.000001d);

        foreach (var feature in _features)
        {
            if (previousByElement.TryGetValue(
                    feature.LineStringElement,
                    out var previous) &&
                ReferenceEquals(previous, feature) &&
                _featureGeometry.ContainsKey(
                    feature.LineStringElement))
            {
                continue;
            }

            if (feature.WorldPoints.Count < 2)
            {
                _featureGeometry.Remove(
                    feature.LineStringElement);
                continue;
            }

            _featureGeometry[feature.LineStringElement] =
                BuildGeometry(
                    feature.WorldPoints,
                    simplifyTolerance);
        }
    }

    private void RebuildGuideGeometry()
    {
        _guideGeometry.Clear();

        var simplifyTolerance =
            0.55d / Math.Max(
                _renderScaleBucket,
                0.000001d);

        foreach (var guide in _guides)
        {
            if (guide.WorldPoints.Count >= 2)
            {
                _guideGeometry.Add((
                    guide,
                    BuildGeometry(
                        guide.WorldPoints,
                        simplifyTolerance)));
            }
        }
    }

    private void RebuildBaseLayerFromCachedGeometry()
    {
        if (_normalPen is null)
        {
            InvalidateRenderCache();
            return;
        }

        var baseLayer = new DrawingGroup();

        using (var context = baseLayer.Open())
        {
            foreach (var feature in _features)
            {
                if (_featureGeometry.TryGetValue(
                        feature.LineStringElement,
                        out var geometry))
                {
                    context.DrawGeometry(
                        null,
                        _normalPen,
                        geometry);
                }
            }
        }

        baseLayer.Freeze();
        _baseLayer = baseLayer;
        DrawBaseLayer();
    }

    private void InvalidateRenderCache()
    {
        _renderCacheDirty = true;
        _baseLayer = null;
        _featureGeometry.Clear();
        _guideGeometry.Clear();

        using (_baseVisual.RenderOpen())
        {
        }

        using (_selectionVisual.RenderOpen())
        {
        }

        using (_overlayVisual.RenderOpen())
        {
        }
    }

    private void EnsureRenderCache()
    {
        var scaleBucket = GetScaleBucket(_scale);

        if (!_renderCacheDirty &&
            double.IsFinite(_renderScaleBucket) &&
            Math.Abs(scaleBucket - _renderScaleBucket) <=
            Math.Max(0.000000001d, scaleBucket * 0.000001d))
        {
            return;
        }

        _renderScaleBucket = scaleBucket;
        _renderCacheDirty = false;

        var safeScale = Math.Max(scaleBucket, 0.000001d);
        var simplifyTolerance = 0.55d / safeScale;

        _normalPen = CreatePen(
            Color.FromRgb(84, 92, 102),
            1.1d / safeScale);

        _selectedPen = CreatePen(
            Color.FromRgb(190, 45, 45),
            2.6d / safeScale);

        _softSelectedPen = CreatePen(
            Color.FromArgb(105, 190, 45, 45),
            1.8d / safeScale);

        _hoverPen = CreatePen(
            Color.FromRgb(0, 120, 190),
            2.2d / safeScale);

        _activeSplitLinePen = CreatePen(
            Color.FromArgb(230, 116, 61, 170),
            2.8d / safeScale,
            DashStyles.Dash);

        _appliedSplitLinePen = CreatePen(
            Color.FromArgb(110, 116, 61, 170),
            2.0d / safeScale,
            DashStyles.Dash);

        _draftSplitPen = CreatePen(
            Color.FromRgb(240, 139, 0),
            2.4d / safeScale);

        _selectStrokePen = CreatePen(
            Color.FromRgb(24, 119, 205),
            2.5d / safeScale,
            DashStyles.Dash);

        _splitStrokePen = CreatePen(
            Color.FromRgb(213, 112, 23),
            2.5d / safeScale,
            DashStyles.Dash);

        _splitVertexBrush = CreateFrozenBrush(
            Color.FromRgb(240, 139, 0));

        _splitPointBrush = CreateFrozenBrush(
            Color.FromRgb(218, 94, 39));

        _appliedSplitPointBrush = CreateFrozenBrush(
            Color.FromArgb(105, 218, 94, 39));

        _featureGeometry.Clear();

        var baseLayer = new DrawingGroup();

        using (var context = baseLayer.Open())
        {
            foreach (var feature in _features)
            {
                if (feature.WorldPoints.Count < 2)
                    continue;

                var geometry = BuildGeometry(
                    feature.WorldPoints,
                    simplifyTolerance);

                _featureGeometry[feature.LineStringElement] =
                    geometry;

                context.DrawGeometry(
                    null,
                    _normalPen,
                    geometry);
            }
        }

        baseLayer.Freeze();
        _baseLayer = baseLayer;

        RebuildGuideGeometry();
        DrawBaseLayer();
        RedrawSelectionLayer();
        RedrawOverlayLayer();
        UpdateLayerTransforms();
    }

    private static double GetScaleBucket(double scale)
    {
        var safeScale = Math.Max(scale, 0.000001d);
        var exponent = Math.Round(
            Math.Log(safeScale, 2d) * 2d) / 2d;

        return Math.Pow(2d, exponent);
    }

    private static StreamGeometry BuildGeometry(
        IReadOnlyList<Point> points,
        double simplifyTolerance = 0d)
    {
        var displayPoints = simplifyTolerance > 0d
            ? SimplifyForDisplay(points, simplifyTolerance)
            : points;

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(
                displayPoints[0],
                isFilled: false,
                isClosed: false);

            for (var index = 1;
                 index < displayPoints.Count;
                 index++)
            {
                context.LineTo(
                    displayPoints[index],
                    true,
                    false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static IReadOnlyList<Point> SimplifyForDisplay(
        IReadOnlyList<Point> points,
        double tolerance)
    {
        if (points.Count <= 2 || tolerance <= 0d)
            return points;

        var isClosed =
            points.Count >= 4 &&
            (points[0] - points[^1]).Length <= 0.000000000001d;

        var sourceCount = isClosed
            ? points.Count - 1
            : points.Count;

        if (sourceCount <= 2)
            return points;

        var keep = new bool[sourceCount];
        keep[0] = true;
        keep[sourceCount - 1] = true;

        var toleranceSquared =
            tolerance * tolerance;

        var stack =
            new Stack<(int Start, int End)>();

        stack.Push((0, sourceCount - 1));

        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();

            if (end <= start + 1)
                continue;

            var maxDistanceSquared = -1d;
            var maxIndex = -1;

            for (var index = start + 1;
                 index < end;
                 index++)
            {
                var distanceSquared =
                    PointToSegmentDistanceSquared(
                        points[index],
                        points[start],
                        points[end]);

                if (distanceSquared >
                    maxDistanceSquared)
                {
                    maxDistanceSquared =
                        distanceSquared;
                    maxIndex = index;
                }
            }

            if (maxIndex < 0 ||
                maxDistanceSquared <=
                toleranceSquared)
            {
                continue;
            }

            keep[maxIndex] = true;
            stack.Push((start, maxIndex));
            stack.Push((maxIndex, end));
        }

        var simplified =
            new List<Point>(sourceCount + 1);

        for (var index = 0;
             index < sourceCount;
             index++)
        {
            if (keep[index])
                simplified.Add(points[index]);
        }

        if (isClosed)
        {
            // A valid display ring needs at least three distinct vertices.
            // If DP collapses more aggressively than that, keep the original
            // ring rather than changing its visible topology.
            if (simplified.Count < 3)
                return points;

            simplified.Add(simplified[0]);
        }

        return simplified;
    }

    private static double PointToSegmentDistanceSquared(
        Point point,
        Point start,
        Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared =
            (dx * dx) + (dy * dy);

        if (lengthSquared <= double.Epsilon)
        {
            var px = point.X - start.X;
            var py = point.Y - start.Y;
            return (px * px) + (py * py);
        }

        var t =
            ((point.X - start.X) * dx +
             (point.Y - start.Y) * dy) /
            lengthSquared;

        t = Math.Clamp(t, 0d, 1d);

        var projectedX =
            start.X + (t * dx);
        var projectedY =
            start.Y + (t * dy);

        var offsetX =
            point.X - projectedX;
        var offsetY =
            point.Y - projectedY;

        return
            (offsetX * offsetX) +
            (offsetY * offsetY);
    }

    private static SolidColorBrush CreateFrozenBrush(
        Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(
        Color color,
        double thickness,
        DashStyle? dashStyle = null)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var pen = new Pen(brush, thickness);

        if (dashStyle is not null)
            pen.DashStyle = dashStyle;

        pen.Freeze();
        return pen;
    }

    private MatrixTransform CreateWorldTransform()
    {
        var matrix = new Matrix(
            _scale,
            0d,
            0d,
            -_scale,
            (ActualWidth / 2d) - (_worldCenter.X * _scale),
            (ActualHeight / 2d) + (_worldCenter.Y * _scale));

        var transform = new MatrixTransform(matrix);
        transform.Freeze();
        return transform;
    }

    private void BeginPan(Point screen)
    {
        Focus();
        CaptureMouse();
        _panning = true;
        _lastPanScreen = screen;
        Cursor = Cursors.Hand;
    }

    private void EndPan()
    {
        if (!_panning)
            return;

        _panning = false;

        if (!_drawingStroke)
            ReleaseMouseCapture();

        Cursor = Tool switch
        {
            LineEditorTool.Pan => Cursors.Hand,
            LineEditorTool.Split => Cursors.Cross,
            _ => Cursors.Arrow
        };
    }

    private int FindNearestEditableGuideVertex(
        Point worldPoint,
        double tolerance)
    {
        var bestIndex = -1;
        var bestDistance = tolerance;

        for (var index = 0;
             index < _editableGuidePoints.Count;
             index++)
        {
            var distance =
                (_editableGuidePoints[index] - worldPoint).Length;

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private Point ScreenToWorld(Point screen)
    {
        if (_scale <= 0d)
            return _worldCenter;

        return new Point(
            _worldCenter.X +
            ((screen.X - (ActualWidth / 2d)) / _scale),
            _worldCenter.Y -
            ((screen.Y - (ActualHeight / 2d)) / _scale));
    }
}

public sealed record LineEditorGuideVisual(
    IReadOnlyList<Point> WorldPoints,
    bool Applied);

public sealed class LineEditorStrokeEventArgs : EventArgs
{
    public LineEditorStrokeEventArgs(
        LineEditorTool tool,
        IReadOnlyList<Point> worldPoints,
        bool isDeselect)
    {
        Tool = tool;
        WorldPoints = worldPoints;
        IsDeselect = isDeselect;
    }

    public LineEditorTool Tool { get; }
    public IReadOnlyList<Point> WorldPoints { get; }
    public bool IsDeselect { get; }
}

public sealed class LineEditorClickEventArgs : EventArgs
{
    public LineEditorClickEventArgs(
        LineEditorTool tool,
        Point worldPoint,
        bool isDeselect,
        bool isAdditive,
        bool isDoubleClick)
    {
        Tool = tool;
        WorldPoint = worldPoint;
        IsDeselect = isDeselect;
        IsAdditive = isAdditive;
        IsDoubleClick = isDoubleClick;
    }

    public LineEditorTool Tool { get; }
    public Point WorldPoint { get; }
    public bool IsDeselect { get; }
    public bool IsAdditive { get; }
    public bool IsDoubleClick { get; }
}

public sealed class LineEditorPointerEventArgs : EventArgs
{
    public LineEditorPointerEventArgs(Point worldPoint)
    {
        WorldPoint = worldPoint;
    }

    public Point WorldPoint { get; }
}

public sealed class LineEditorGuideEditEventArgs : EventArgs
{
    public LineEditorGuideEditEventArgs(
        IReadOnlyList<Point> worldPoints,
        int vertexIndex,
        bool isComplete)
    {
        WorldPoints = worldPoints;
        VertexIndex = vertexIndex;
        IsComplete = isComplete;
    }

    public IReadOnlyList<Point> WorldPoints { get; }
    public int VertexIndex { get; }
    public bool IsComplete { get; }
}

public sealed class LineEditorContextEventArgs : EventArgs
{
    public LineEditorContextEventArgs(Point worldPoint)
    {
        WorldPoint = worldPoint;
    }

    public Point WorldPoint { get; }
}
