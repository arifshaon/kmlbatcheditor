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
    private IReadOnlyList<KmlLineFeature> _features =
        Array.Empty<KmlLineFeature>();

    private IReadOnlyList<LineEditorGuideVisual> _guides =
        Array.Empty<LineEditorGuideVisual>();

    private IReadOnlyList<Point> _cutPoints = Array.Empty<Point>();
    private readonly Dictionary<XElement, StreamGeometry> _featureGeometry = new();
    private readonly List<(LineEditorGuideVisual Guide, StreamGeometry Geometry)> _guideGeometry = new();

    private HashSet<XElement> _selected = new();
    private XElement? _hovered;
    private Rect _worldBounds = Rect.Empty;

    private Point _worldCenter;
    private double _scale = 1d;
    private bool _fitPending = true;

    private bool _drawingStroke;
    private readonly List<Point> _strokeWorld = new();
    private Point _strokeStartScreen;
    private Point _lastStrokeScreen;

    private bool _panning;
    private Point _lastPanScreen;
    private Point _lastHoverScreen;
    private bool _hasHoverScreen;

    public LineEditorSurface()
    {
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
                InvalidateVisual();
        };
    }

    public LineEditorTool Tool { get; set; } = LineEditorTool.Select;

    public event EventHandler<LineEditorStrokeEventArgs>? StrokeCompleted;
    public event EventHandler<LineEditorClickEventArgs>? LineClicked;
    public event EventHandler<LineEditorPointerEventArgs>? PointerMoved;

    public void SetDocument(
        IReadOnlyList<KmlLineFeature> features,
        IEnumerable<XElement> selected,
        Rect worldBounds,
        IReadOnlyList<LineEditorGuideVisual>? guides = null,
        IReadOnlyList<Point>? cutPoints = null,
        bool fitView = false)
    {
        _features = features ?? Array.Empty<KmlLineFeature>();
        _selected = selected?.ToHashSet() ?? new HashSet<XElement>();
        _worldBounds = worldBounds;
        _guides = guides ?? Array.Empty<LineEditorGuideVisual>();
        _cutPoints = cutPoints ?? Array.Empty<Point>();

        RebuildGeometryCache();

        if (_hovered is not null &&
            !_featureGeometry.ContainsKey(_hovered))
        {
            _hovered = null;
        }

        if (fitView)
        {
            _fitPending = true;

            if (IsLoaded)
                FitView();
        }
        else
        {
            InvalidateVisual();
        }
    }

    public void SetSelection(IEnumerable<XElement> selected)
    {
        _selected = selected?.ToHashSet() ?? new HashSet<XElement>();
        InvalidateVisual();
    }

    public void SetHovered(XElement? hovered)
    {
        if (ReferenceEquals(_hovered, hovered))
            return;

        _hovered = hovered;
        InvalidateVisual();
    }

    public double WorldToleranceFromPixels(double pixels)
    {
        if (_scale <= 0d || !double.IsFinite(_scale))
            return 0d;

        return Math.Max(0d, pixels) / _scale;
    }

    public void FitView()
    {
        if (!IsLoaded ||
            ActualWidth <= 0d ||
            ActualHeight <= 0d ||
            _worldBounds.IsEmpty)
        {
            return;
        }

        var width = Math.Max(_worldBounds.Width, 0.000000001d);
        var height = Math.Max(_worldBounds.Height, 0.000000001d);
        var usableWidth = Math.Max(ActualWidth - 40d, 20d);
        var usableHeight = Math.Max(ActualHeight - 40d, 20d);

        _scale = Math.Min(
            usableWidth / width,
            usableHeight / height);

        if (!double.IsFinite(_scale) || _scale <= 0d)
            _scale = 1d;

        _worldCenter = new Point(
            _worldBounds.Left + (_worldBounds.Width / 2d),
            _worldBounds.Top + (_worldBounds.Height / 2d));

        _fitPending = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        drawingContext.DrawRectangle(
            Brushes.White,
            null,
            new Rect(0d, 0d, ActualWidth, ActualHeight));

        if (_features.Count == 0 && _guides.Count == 0)
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

            return;
        }

        var safeScale = Math.Max(_scale, 0.000001d);
        drawingContext.PushTransform(CreateWorldTransform());

        var normalPen = CreatePen(Color.FromRgb(84, 92, 102), 1.1d / safeScale);
        var selectedPen = CreatePen(Color.FromRgb(190, 45, 45), 2.6d / safeScale);
        var hoverPen = CreatePen(Color.FromRgb(0, 120, 190), 2.2d / safeScale);

        foreach (var feature in _features)
        {
            if (!_featureGeometry.TryGetValue(
                    feature.LineStringElement,
                    out var geometry))
            {
                continue;
            }

            var pen = _selected.Contains(feature.LineStringElement)
                ? selectedPen
                : ReferenceEquals(_hovered, feature.LineStringElement)
                    ? hoverPen
                    : normalPen;

            drawingContext.DrawGeometry(null, pen, geometry);
        }

        foreach (var item in _guideGeometry)
        {
            var alpha = item.Guide.Applied ? (byte)110 : (byte)230;
            var guidePen = CreatePen(
                Color.FromArgb(alpha, 116, 61, 170),
                (item.Guide.Applied ? 2.0d : 2.8d) / safeScale,
                DashStyles.Dash);

            drawingContext.DrawGeometry(null, guidePen, item.Geometry);
        }

        if (_cutPoints.Count > 0)
        {
            var markerBrush = new SolidColorBrush(
                Color.FromRgb(224, 91, 32));
            markerBrush.Freeze();

            var radius = 3.5d / safeScale;

            foreach (var point in _cutPoints)
            {
                drawingContext.DrawEllipse(
                    markerBrush,
                    null,
                    point,
                    radius,
                    radius);
            }
        }

        if (_strokeWorld.Count >= 2)
        {
            var strokeColor = Tool == LineEditorTool.Cut
                ? Color.FromRgb(213, 112, 23)
                : Color.FromRgb(24, 119, 205);

            var strokePen = CreatePen(
                strokeColor,
                2.5d / safeScale,
                DashStyles.Dash);

            var strokeGeometry = BuildGeometry(_strokeWorld);
            drawingContext.DrawGeometry(null, strokePen, strokeGeometry);
        }

        drawingContext.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        Focus();
        CaptureMouse();

        _drawingStroke = true;
        _strokeWorld.Clear();

        var screen = e.GetPosition(this);
        _strokeStartScreen = screen;
        _lastStrokeScreen = screen;
        _strokeWorld.Add(ScreenToWorld(screen));

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_drawingStroke)
            return;

        var screen = e.GetPosition(this);
        var clickDistance = (screen - _strokeStartScreen).Length;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        _drawingStroke = false;
        ReleaseMouseCapture();

        if (clickDistance < 4d)
        {
            LineClicked?.Invoke(
                this,
                new LineEditorClickEventArgs(
                    Tool,
                    ScreenToWorld(screen),
                    alt));
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
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        BeginPan(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        EndPan();
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

        if (_panning)
        {
            var delta = screen - _lastPanScreen;
            _lastPanScreen = screen;

            if (_scale > 0d)
            {
                _worldCenter = new Point(
                    _worldCenter.X - (delta.X / _scale),
                    _worldCenter.Y + (delta.Y / _scale));

                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        if (_drawingStroke && e.LeftButton == MouseButtonState.Pressed)
        {
            if ((screen - _lastStrokeScreen).Length < 3d)
                return;

            _lastStrokeScreen = screen;
            _strokeWorld.Add(ScreenToWorld(screen));
            InvalidateVisual();
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
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (_features.Count == 0 && _guides.Count == 0)
            return;

        var screen = e.GetPosition(this);
        var worldBefore = ScreenToWorld(screen);

        var factor = e.Delta > 0 ? 1.2d : (1d / 1.2d);
        _scale = Math.Clamp(_scale * factor, 0.000001d, 1000000000d);

        var worldAfter = ScreenToWorld(screen);

        _worldCenter = new Point(
            _worldCenter.X + (worldBefore.X - worldAfter.X),
            _worldCenter.Y + (worldBefore.Y - worldAfter.Y));

        _fitPending = false;
        InvalidateVisual();
        e.Handled = true;
    }

    private void RebuildGeometryCache()
    {
        _featureGeometry.Clear();

        foreach (var feature in _features)
        {
            if (feature.WorldPoints.Count >= 2)
            {
                _featureGeometry[feature.LineStringElement] =
                    BuildGeometry(feature.WorldPoints);
            }
        }

        _guideGeometry.Clear();

        foreach (var guide in _guides)
        {
            if (guide.WorldPoints.Count >= 2)
                _guideGeometry.Add((guide, BuildGeometry(guide.WorldPoints)));
        }
    }

    private static StreamGeometry BuildGeometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(
                points[0],
                isFilled: false,
                isClosed: false);

            for (var index = 1; index < points.Count; index++)
                context.LineTo(points[index], true, false);
        }

        geometry.Freeze();
        return geometry;
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

        Cursor = Cursors.Arrow;
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
        bool isDeselect)
    {
        Tool = tool;
        WorldPoint = worldPoint;
        IsDeselect = isDeselect;
    }

    public LineEditorTool Tool { get; }
    public Point WorldPoint { get; }
    public bool IsDeselect { get; }
}

public sealed class LineEditorPointerEventArgs : EventArgs
{
    public LineEditorPointerEventArgs(Point worldPoint)
    {
        WorldPoint = worldPoint;
    }

    public Point WorldPoint { get; }
}
