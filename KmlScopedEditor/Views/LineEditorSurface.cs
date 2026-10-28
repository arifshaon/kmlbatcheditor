using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using Cursors = System.Windows.Input.Cursors;
using Pen = System.Windows.Media.Pen;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;

namespace KmlScopedEditor.Views;

/// <summary>
/// Lightweight drawing surface for interactive LineString editing. Left-drag
/// draws the active Select/Cut stroke, the mouse wheel zooms, and right- or
/// middle-drag pans.
/// </summary>
public sealed class LineEditorSurface : FrameworkElement
{
    private IReadOnlyList<KmlLineFeature> _features =
        Array.Empty<KmlLineFeature>();

    private HashSet<XElement> _selected = new();
    private Rect _worldBounds = Rect.Empty;

    private Point _worldCenter;
    private double _scale = 1d;
    private bool _fitPending = true;

    private bool _drawingStroke;
    private readonly List<Point> _strokeWorld = new();
    private Point _lastStrokeScreen;

    private bool _panning;
    private Point _lastPanScreen;

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

    public void SetDocument(
        IReadOnlyList<KmlLineFeature> features,
        IEnumerable<XElement> selected,
        Rect worldBounds,
        bool fitView = false)
    {
        _features = features ?? Array.Empty<KmlLineFeature>();
        _selected = selected?.ToHashSet() ?? new HashSet<XElement>();
        _worldBounds = worldBounds;

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

        if (_features.Count == 0)
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

        var normalPen = new Pen(
            new SolidColorBrush(Color.FromRgb(84, 92, 102)),
            1.1d);

        var selectedPen = new Pen(
            new SolidColorBrush(Color.FromRgb(190, 45, 45)),
            2.6d);

        normalPen.Freeze();
        selectedPen.Freeze();

        foreach (var feature in _features)
        {
            var points = feature.WorldPoints;
            if (points.Count < 2)
                continue;

            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(
                    WorldToScreen(points[0]),
                    isFilled: false,
                    isClosed: false);

                for (var index = 1; index < points.Count; index++)
                    context.LineTo(WorldToScreen(points[index]), true, false);
            }

            geometry.Freeze();

            var pen = _selected.Contains(feature.LineStringElement)
                ? selectedPen
                : normalPen;

            drawingContext.DrawGeometry(null, pen, geometry);
        }

        if (_strokeWorld.Count >= 2)
        {
            var brush = Tool == LineEditorTool.Cut
                ? new SolidColorBrush(Color.FromRgb(213, 112, 23))
                : new SolidColorBrush(Color.FromRgb(24, 119, 205));

            var strokePen = new Pen(brush, 2.5d)
            {
                DashStyle = DashStyles.Dash
            };

            strokePen.Freeze();

            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(
                    WorldToScreen(_strokeWorld[0]),
                    isFilled: false,
                    isClosed: false);

                for (var index = 1; index < _strokeWorld.Count; index++)
                    context.LineTo(WorldToScreen(_strokeWorld[index]), true, false);
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(null, strokePen, geometry);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        Focus();
        CaptureMouse();

        _drawingStroke = true;
        _strokeWorld.Clear();

        var screen = e.GetPosition(this);
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

        if ((screen - _lastStrokeScreen).Length >= 1d)
            _strokeWorld.Add(ScreenToWorld(screen));

        _drawingStroke = false;
        ReleaseMouseCapture();

        if (_strokeWorld.Count >= 2)
        {
            StrokeCompleted?.Invoke(
                this,
                new LineEditorStrokeEventArgs(
                    Tool,
                    _strokeWorld.ToList()));
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

        if (!_drawingStroke || e.LeftButton != MouseButtonState.Pressed)
            return;

        if ((screen - _lastStrokeScreen).Length < 3d)
            return;

        _lastStrokeScreen = screen;
        _strokeWorld.Add(ScreenToWorld(screen));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (_features.Count == 0)
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

    private Point WorldToScreen(Point world)
    {
        return new Point(
            ((world.X - _worldCenter.X) * _scale) + (ActualWidth / 2d),
            ((_worldCenter.Y - world.Y) * _scale) + (ActualHeight / 2d));
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

public sealed class LineEditorStrokeEventArgs : EventArgs
{
    public LineEditorStrokeEventArgs(
        LineEditorTool tool,
        IReadOnlyList<Point> worldPoints)
    {
        Tool = tool;
        WorldPoints = worldPoints;
    }

    public LineEditorTool Tool { get; }
    public IReadOnlyList<Point> WorldPoints { get; }
}
