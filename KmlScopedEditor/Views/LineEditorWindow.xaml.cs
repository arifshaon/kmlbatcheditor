using System.Windows;
using System.Xml.Linq;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace KmlScopedEditor.Views;

public partial class LineEditorWindow : Window
{
    private readonly XDocument _originalDocument;
    private XDocument _workingDocument;
    private KmlLineGeometryService _geometry;
    private readonly double _projectionScale;

    private readonly KmlCutGuideService _cutGuideLoader = new();
    private readonly List<CutGuideState> _guides = new();
    private readonly List<Point> _cutPoints = new();
    private readonly List<Point> _draftCutPoints = new();
    private Point? _draftCutPreviewPoint;
    private bool _draftCutDrawing;

    private readonly HashSet<XElement> _selected = new();
    private readonly Stack<UndoState> _undo = new();

    private bool _hasPerformedCut;

    public LineEditorWindow(
        XDocument sourceDocument,
        string? sourceName = null)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(sourceDocument);

        _originalDocument = new XDocument(sourceDocument);
        _workingDocument = new XDocument(sourceDocument);
        _geometry = new KmlLineGeometryService(_workingDocument);
        _projectionScale = _geometry.LongitudeScale;

        if (!string.IsNullOrWhiteSpace(sourceName))
            Title = $"Line Editor - {sourceName}";

        EditorSurface.StrokeCompleted += OnStrokeCompleted;
        EditorSurface.LineClicked += OnLineClicked;
        EditorSurface.PointerMoved += OnPointerMoved;
        PreviewKeyDown += OnPreviewKeyDown;

        Loaded += (_, _) =>
        {
            SetTool(LineEditorTool.Select);
            RefreshSurface(fitView: true);
            UpdateStatus("Ready.");
        };
    }

    public XDocument EditedDocument => new(_workingDocument);

    private void OnSelectToolClick(object sender, RoutedEventArgs e)
    {
        StopDraftPreview();
        SetTool(LineEditorTool.Select);
    }

    private void OnCutToolClick(object sender, RoutedEventArgs e)
    {
        SetTool(LineEditorTool.Cut);
        _draftCutDrawing = true;
        _draftCutPreviewPoint = null;
        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateStatus(
            _draftCutPoints.Count == 0
                ? "Draw Cut Line started. Click to place the first point."
                : $"Continuing the cut line from {_draftCutPoints.Count:N0} existing point(s). Click to add another point.");
    }

    private void SetTool(LineEditorTool tool)
    {
        EditorSurface.Tool = tool;
        SelectToolButton.IsChecked = tool == LineEditorTool.Select;
        CutToolButton.IsChecked = tool == LineEditorTool.Cut;

        InstructionText.Text = tool == LineEditorTool.Select
            ? "SELECT: drag across lines to select only lines that are actually crossed. Alt + drag removes crossed lines from the selection. A single click selects the nearest line only. Mouse wheel zooms; right- or middle-drag pans."
            : "DRAW CUT LINE: click to add connected points. Click Finish Line (or press Enter) when the path is complete. Cancel Line (or Esc) discards the current path. Apply Cut also finishes the line automatically before cutting.";
    }

    private void OnStrokeCompleted(
        object? sender,
        LineEditorStrokeEventArgs e)
    {
        if (e.Tool != LineEditorTool.Select)
            return;

        var matches = _geometry.FindIntersectingFeatures(e.WorldPoints);

        if (e.IsDeselect)
        {
            foreach (var feature in matches)
                _selected.Remove(feature.LineStringElement);
        }
        else
        {
            foreach (var feature in matches)
                _selected.Add(feature.LineStringElement);
        }

        EditorSurface.SetSelection(_selected);

        var noun = CurrentLineNoun;

        UpdateStatus(
            matches.Count == 0
                ? $"No {noun} crossed by that selection stroke."
                : e.IsDeselect
                    ? $"Removed {matches.Count:N0} crossed {noun} from the selection."
                    : $"Added {matches.Count:N0} crossed {noun} to the selection.");
    }

    private void OnLineClicked(
        object? sender,
        LineEditorClickEventArgs e)
    {
        if (e.Tool == LineEditorTool.Cut)
        {
            if (!_draftCutDrawing)
                return;

            _draftCutPoints.Add(e.WorldPoint);
            _draftCutPreviewPoint = null;
            EditorSurface.SetDraftCut(
                _draftCutPoints,
                _draftCutPreviewPoint);

            UpdateStatus(
                $"Cut line has {_draftCutPoints.Count:N0} point(s). " +
                (_draftCutPoints.Count < 2
                    ? "Add at least one more point."
                    : "Keep adding points, or click Finish Line / Apply Cut when ready."));

            return;
        }

        if (e.Tool != LineEditorTool.Select)
            return;

        var tolerance = EditorSurface.WorldToleranceFromPixels(6d);
        var feature = _geometry.FindNearestFeature(
            e.WorldPoint,
            tolerance);

        if (feature is null)
        {
            UpdateStatus("No line close enough to that click.");
            return;
        }

        if (e.IsDeselect)
        {
            _selected.Remove(feature.LineStringElement);
            UpdateStatus($"Removed one {CurrentLineNoun} from the selection.");
        }
        else
        {
            _selected.Clear();
            _selected.Add(feature.LineStringElement);
            UpdateStatus($"Selected one {CurrentLineNoun}.");
        }

        EditorSurface.SetSelection(_selected);
    }

    private void OnPointerMoved(
        object? sender,
        LineEditorPointerEventArgs e)
    {
        if (EditorSurface.Tool == LineEditorTool.Cut)
        {
            EditorSurface.SetHovered(null);

            if (_draftCutDrawing && _draftCutPoints.Count > 0)
            {
                _draftCutPreviewPoint = e.WorldPoint;
                EditorSurface.SetDraftCut(
                    _draftCutPoints,
                    _draftCutPreviewPoint);
            }

            return;
        }

        var tolerance = EditorSurface.WorldToleranceFromPixels(4d);
        var feature = _geometry.FindNearestFeature(
            e.WorldPoint,
            tolerance);

        EditorSurface.SetHovered(feature?.LineStringElement);
    }

    private void OnImportCutLinesClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Cut Lines",
            Filter = "Google Earth files (*.kml;*.kmz)|*.kml;*.kmz|KML files (*.kml)|*.kml|KMZ files (*.kmz)|*.kmz",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var imported = 0;

        try
        {
            foreach (var fileName in dialog.FileNames)
            {
                var guides = _cutGuideLoader.Load(fileName);

                foreach (var guide in guides)
                {
                    _guides.Add(new CutGuideState(
                        guide,
                        removeMatchingSourceAfterCut: false));

                    imported++;
                }
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"The cut line could not be imported.\n\n{exception.Message}",
                "Import Cut Lines",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        if (imported == 0)
        {
            UpdateStatus("No LineString paths were found in the selected cutline file.");
            return;
        }

        RefreshSurface(fitView: true);

        UpdateStatus(
            $"Imported {imported:N0} cut guide(s). Review the purple guide line(s), then click Apply Cut.");
    }

    private void OnUseSelectedAsCutlineClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_selected.Count != 1)
        {
            UpdateStatus(
                "Select exactly one line with a single click, then choose Use Selected as Cutline.");
            return;
        }

        var selectedElement = _selected.Single();

        var feature = _geometry.Features.FirstOrDefault(item =>
            ReferenceEquals(item.LineStringElement, selectedElement));

        if (feature is null)
        {
            UpdateStatus("The selected line is no longer available.");
            return;
        }

        var guide = new KmlCutGuide(
            feature.Name,
            feature.Coordinates.ToList());

        _guides.Add(new CutGuideState(
            guide,
            removeMatchingSourceAfterCut: true));

        _selected.Clear();
        EditorSurface.SetSelection(_selected);
        RefreshSurface();

        UpdateStatus(
            "Selected line added as a cut guide. Apply Cut will use it and remove the matching helper line from the working document.");
    }

    private void OnApplyCutLinesClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_draftCutDrawing)
            StopDraftPreview();

        if (_draftCutPoints.Count == 1)
        {
            UpdateStatus(
                "The drawn cut line has only one point. Add at least one more point before applying it.");
            return;
        }

        if (_draftCutPoints.Count >= 2)
        {
            var drawnGuide = new KmlCutGuide(
                "Drawn cut line",
                _draftCutPoints
                    .Select(_geometry.Unproject)
                    .ToList());

            _guides.Add(new CutGuideState(
                drawnGuide,
                removeMatchingSourceAfterCut: false));

            _draftCutPoints.Clear();
            _draftCutPreviewPoint = null;
            EditorSurface.SetDraftCut(
                _draftCutPoints,
                _draftCutPreviewPoint);
        }

        SetTool(LineEditorTool.Select);

        var pending = _guides
            .Where(guide => !guide.Applied)
            .ToList();

        if (pending.Count == 0)
        {
            UpdateStatus(
                _guides.Count == 0
                    ? "No cut guides are loaded."
                    : "All loaded cut guides have already been applied.");
            return;
        }

        var snapshot = CaptureUndoState();
        var totalLines = 0;
        var totalIntersections = 0;
        var totalPieces = 0;
        var helperLinesRemoved = 0;
        var allCutPoints = new List<Point>();
        var changed = false;

        foreach (var guideState in pending)
        {
            var worldPoints = ProjectGuide(guideState.Guide);
            var result = _geometry.CutWithStroke(worldPoints);

            totalLines += result.LinesSplit;
            totalIntersections += result.IntersectionCount;
            totalPieces += result.OutputSegments;
            allCutPoints.AddRange(result.CutPoints);

            if (result.LinesSplit > 0)
                changed = true;

            if (guideState.RemoveMatchingSourceAfterCut)
            {
                var sourceFeature = FindMatchingFeature(
                    guideState.Guide.Coordinates);

                if (sourceFeature is not null)
                {
                    helperLinesRemoved += _geometry.DeleteLines(
                        new[] { sourceFeature.LineStringElement });

                    if (helperLinesRemoved > 0)
                        changed = true;
                }
            }

            guideState.Applied = true;
        }

        _selected.Clear();
        _cutPoints.Clear();
        _cutPoints.AddRange(allCutPoints);

        if (totalLines > 0)
            _hasPerformedCut = true;

        if (changed)
            _undo.Push(snapshot);

        RefreshSurface();

        if (totalLines == 0)
        {
            UpdateStatus(
                helperLinesRemoved > 0
                    ? $"No contours were split; removed {helperLinesRemoved:N0} helper cutline(s)."
                    : "The cut guide did not split any contours.");
            return;
        }

        UpdateStatus(
            $"Applied {pending.Count:N0} cut guide(s): " +
            $"{totalLines:N0} line(s) cut at " +
            $"{totalIntersections:N0} intersection(s), producing " +
            $"{totalPieces:N0} piece(s)." +
            (helperLinesRemoved > 0
                ? $" Removed {helperLinesRemoved:N0} helper line(s)."
                : string.Empty));
    }

    private void OnFinishCutLineClick(
        object sender,
        RoutedEventArgs e)
    {
        FinishDraftCutLine();
    }

    private void OnCancelCutLineClick(
        object sender,
        RoutedEventArgs e)
    {
        CancelDraftCutLine();
    }

    private void OnUndoCutPointClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_draftCutPoints.Count == 0)
        {
            UpdateStatus("There are no drawn cut points to undo.");
            return;
        }

        _draftCutPoints.RemoveAt(_draftCutPoints.Count - 1);

        if (_draftCutPoints.Count == 0)
            _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateStatus(
            _draftCutPoints.Count == 0
                ? "Drawn cut line cleared."
                : $"Removed the last cut point. {_draftCutPoints.Count:N0} point(s) remain.");
    }

    private void OnClearGuidesClick(
        object sender,
        RoutedEventArgs e)
    {
        _guides.Clear();
        _cutPoints.Clear();
        _draftCutPoints.Clear();
        _draftCutPreviewPoint = null;
        _draftCutDrawing = false;
        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);
        RefreshSurface();

        UpdateStatus(
            "Cut guide overlays and the current drawn cut line were cleared. Existing cuts in the working document were not changed.");
    }

    private void OnDeleteSelectedClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_selected.Count == 0)
        {
            UpdateStatus("Nothing is selected for deletion.");
            return;
        }

        var snapshot = CaptureUndoState();
        var deleted = _geometry.DeleteLines(_selected);

        if (deleted == 0)
        {
            UpdateStatus("The selected lines could not be deleted.");
            return;
        }

        _undo.Push(snapshot);
        _selected.Clear();
        RefreshSurface();

        UpdateStatus(
            $"Deleted {deleted:N0} selected {CurrentLineNoun}.");
    }

    private void OnClearSelectionClick(
        object sender,
        RoutedEventArgs e)
    {
        _selected.Clear();
        EditorSurface.SetSelection(_selected);
        UpdateStatus("Selection cleared.");
    }

    private void OnInvertSelectionClick(
        object sender,
        RoutedEventArgs e)
    {
        var inverted = _geometry.Features
            .Where(feature => !_selected.Contains(feature.LineStringElement))
            .Select(feature => feature.LineStringElement)
            .ToList();

        _selected.Clear();

        foreach (var line in inverted)
            _selected.Add(line);

        EditorSurface.SetSelection(_selected);
        UpdateStatus("Selection inverted.");
    }

    private void OnUndoClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_undo.Count == 0)
        {
            UpdateStatus("There is no cut or deletion to undo.");
            return;
        }

        var state = _undo.Pop();

        _workingDocument = state.Document;
        _geometry = new KmlLineGeometryService(
            _workingDocument,
            _projectionScale);

        _hasPerformedCut = state.HasPerformedCut;

        foreach (var guide in _guides)
        {
            guide.Applied =
                state.GuideApplied.TryGetValue(
                    guide.Id,
                    out var applied) &&
                applied;
        }

        _cutPoints.Clear();
        _cutPoints.AddRange(state.CutPoints);
        _selected.Clear();

        RefreshSurface();

        UpdateStatus(
            "Last cut/deletion undone. Cut guide overlays remain available.");
    }

    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            (_draftCutDrawing || _draftCutPoints.Count > 0))
        {
            FinishDraftCutLine();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape &&
            (_draftCutDrawing || _draftCutPoints.Count > 0))
        {
            CancelDraftCutLine();
            e.Handled = true;
        }
    }

    private void OnFitViewClick(
        object sender,
        RoutedEventArgs e)
    {
        EditorSurface.FitView();
    }

    private void OnApplyClick(
        object sender,
        RoutedEventArgs e)
    {
        if (XNode.DeepEquals(
                _originalDocument.Root,
                _workingDocument.Root))
        {
            DialogResult = false;
            Close();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void FinishDraftCutLine()
    {
        if (_draftCutPoints.Count < 2)
        {
            UpdateStatus(
                _draftCutPoints.Count == 0
                    ? "No cut line is being drawn."
                    : "The cut line needs at least two points before it can be finished.");
            return;
        }

        StopDraftPreview();
        SetTool(LineEditorTool.Select);

        UpdateStatus(
            $"Cut line finished with {_draftCutPoints.Count:N0} point(s). Review the orange path, then click Apply Cut.");
    }

    private void CancelDraftCutLine()
    {
        _draftCutPoints.Clear();
        _draftCutPreviewPoint = null;
        _draftCutDrawing = false;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        SetTool(LineEditorTool.Select);
        UpdateStatus("Current drawn cut line cancelled.");
    }

    private void StopDraftPreview()
    {
        _draftCutDrawing = false;
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);
    }

    private UndoState CaptureUndoState()
    {
        return new UndoState(
            new XDocument(_workingDocument),
            _hasPerformedCut,
            _guides.ToDictionary(
                guide => guide.Id,
                guide => guide.Applied),
            _cutPoints.ToList());
    }

    private IReadOnlyList<Point> ProjectGuide(KmlCutGuide guide)
    {
        return guide.Coordinates
            .Select(_geometry.Project)
            .ToList();
    }

    private KmlLineFeature? FindMatchingFeature(
        IReadOnlyList<KmlCoordinate> coordinates)
    {
        return _geometry.Features.FirstOrDefault(feature =>
            CoordinatesMatch(feature.Coordinates, coordinates));
    }

    private static bool CoordinatesMatch(
        IReadOnlyList<KmlCoordinate> left,
        IReadOnlyList<KmlCoordinate> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (Math.Abs(
                    left[index].Longitude -
                    right[index].Longitude) > 0.0000000001d ||
                Math.Abs(
                    left[index].Latitude -
                    right[index].Latitude) > 0.0000000001d)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshSurface(bool fitView = false)
    {
        var guideVisuals = _guides
            .Select(guide =>
                new LineEditorGuideVisual(
                    ProjectGuide(guide.Guide),
                    guide.Applied))
            .ToList();

        EditorSurface.SetDocument(
            _geometry.Features,
            _selected,
            GetDisplayBounds(guideVisuals),
            guideVisuals,
            _cutPoints,
            fitView);

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);
    }

    private Rect GetDisplayBounds(
        IReadOnlyList<LineEditorGuideVisual> guides)
    {
        var bounds = _geometry.WorldBounds;

        foreach (var guide in guides)
        {
            foreach (var point in guide.WorldPoints)
            {
                if (bounds.IsEmpty)
                    bounds = new Rect(point, point);
                else
                    bounds.Union(point);
            }
        }

        return bounds;
    }

    private string CurrentLineNoun =>
        _hasPerformedCut ? "line fragment(s)" : "contour(s)";

    private void UpdateStatus(string message)
    {
        StatusText.Text =
            $"{message}  " +
            $"{_geometry.Features.Count:N0} {CurrentLineNoun} in document; " +
            $"{_selected.Count:N0} selected; " +
            $"{_guides.Count:N0} guide(s); " +
            $"{_undo.Count:N0} undo step(s).";
    }

    private sealed class CutGuideState
    {
        public CutGuideState(
            KmlCutGuide guide,
            bool removeMatchingSourceAfterCut)
        {
            Guide = guide;
            RemoveMatchingSourceAfterCut =
                removeMatchingSourceAfterCut;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public KmlCutGuide Guide { get; }
        public bool RemoveMatchingSourceAfterCut { get; }
        public bool Applied { get; set; }
    }

    private sealed record UndoState(
        XDocument Document,
        bool HasPerformedCut,
        Dictionary<Guid, bool> GuideApplied,
        IReadOnlyList<Point> CutPoints);
}
