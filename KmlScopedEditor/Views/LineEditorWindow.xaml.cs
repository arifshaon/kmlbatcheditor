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
    private static readonly XNamespace EditorNs =
        "urn:kml-scoped-editor:temporary";

    private static readonly XName ScopeMarkerAttribute =
        EditorNs + "line-editor-scope";

    private readonly XDocument _originalDocument;
    private XDocument _workingDocument;
    private KmlLineGeometryService _geometry;
    private KmlLineGeometryService _contextGeometry;
    private readonly double _projectionScale;
    private readonly bool _isScoped;
    private readonly string _scopeDisplayName;
    private readonly int _scopePlacemarkCount;

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
        string? sourceName = null,
        IReadOnlyCollection<int>? scopedPlacemarkIndexes = null,
        string? scopeDisplayName = null)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(sourceDocument);

        _originalDocument = new XDocument(sourceDocument);
        _workingDocument = new XDocument(sourceDocument);

        var allPlacemarkCount = _workingDocument
            .Descendants(KmlNs + "Placemark")
            .Count();

        _isScoped = scopedPlacemarkIndexes is not null;
        _scopeDisplayName = string.IsNullOrWhiteSpace(scopeDisplayName)
            ? (_isScoped ? "Selected tree node" : "Entire document")
            : scopeDisplayName;

        if (_isScoped)
        {
            ApplyScopeMarkers(
                _workingDocument,
                scopedPlacemarkIndexes!);

            _scopePlacemarkCount = scopedPlacemarkIndexes!.Count;
        }
        else
        {
            _scopePlacemarkCount = allPlacemarkCount;
        }

        var projectionGeometry = new KmlLineGeometryService(
            _workingDocument);

        _projectionScale = projectionGeometry.LongitudeScale;

        _contextGeometry = new KmlLineGeometryService(
            _workingDocument,
            _projectionScale);

        _geometry = new KmlLineGeometryService(
            _workingDocument,
            _projectionScale,
            _isScoped ? ScopeMarkerAttribute : null);

        if (!string.IsNullOrWhiteSpace(sourceName))
            Title = $"Line Editor - {sourceName}";

        EditorSurface.StrokeCompleted += OnStrokeCompleted;
        EditorSurface.LineClicked += OnLineClicked;
        EditorSurface.PointerMoved += OnPointerMoved;
        EditorSurface.PointerExited += OnPointerExited;
        EditorSurface.ContextRequested += OnContextRequested;
        PreviewKeyDown += OnPreviewKeyDown;

        Loaded += (_, _) =>
        {
            SetTool(LineEditorTool.Pan);
            UpdateScopeText();
            RefreshSurface(fitView: true);
            UpdateStatus("Ready.");
        };
    }

    public XDocument EditedDocument => CreateOutputDocument();

    private void OnPanToolClick(object sender, RoutedEventArgs e)
    {
        StopDraftPreview();
        SetTool(LineEditorTool.Pan);
    }

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
        PanToolButton.IsChecked = tool == LineEditorTool.Pan;
        SelectToolButton.IsChecked = tool == LineEditorTool.Select;
        CutToolButton.IsChecked = tool == LineEditorTool.Cut;
        EditorSurface.Cursor = tool == LineEditorTool.Pan
            ? System.Windows.Input.Cursors.Hand
            : System.Windows.Input.Cursors.Arrow;

        InstructionText.Text = tool switch
        {
            LineEditorTool.Pan =>
                "PAN: left-drag to move around the map. Mouse wheel zooms. You can also hold Space and left-drag from any tool, or middle-drag.",
            LineEditorTool.Select =>
                "SELECT: drag across editable lines to select only lines that are actually crossed. Alt + drag removes crossed lines from the selection. Single-click selects the nearest editable line.",
            _ =>
                "DRAW CUT LINE: click to add connected points. Right-click anywhere on the map for Add/Finish/Cancel options. Finish Line (or Enter) stops the preview; Esc cancels."
        };
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

            AddDraftCutPoint(e.WorldPoint);

            UpdateStatus(
                _draftCutPoints.Count < 2
                    ? "Cut line started. Add at least one more point."
                    : $"Cut line has {_draftCutPoints.Count:N0} point(s). Right-click to finish, or keep adding points.");

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

        if (EditorSurface.Tool == LineEditorTool.Pan)
        {
            EditorSurface.SetHovered(null);
            return;
        }

        var tolerance = EditorSurface.WorldToleranceFromPixels(4d);
        var feature = _geometry.FindNearestFeature(
            e.WorldPoint,
            tolerance);

        EditorSurface.SetHovered(feature?.LineStringElement);
    }

    private void OnPointerExited(
        object? sender,
        EventArgs e)
    {
        if (!_draftCutDrawing)
            return;

        _draftCutPreviewPoint = null;
        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);
    }

    private void OnContextRequested(
        object? sender,
        LineEditorContextEventArgs e)
    {
        _draftCutPreviewPoint = null;
        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = EditorSurface,
            Placement =
                System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        if (_draftCutPoints.Count == 0)
        {
            AddContextMenuItem(
                menu,
                "Start Cut Line Here",
                () => BeginDraftCutLineAt(e.WorldPoint));
        }
        else if (_draftCutDrawing)
        {
            AddContextMenuItem(
                menu,
                "Add Cut Point Here",
                () => AddDraftCutPoint(e.WorldPoint));

            AddContextMenuItem(
                menu,
                "Finish Cut Line Here",
                () =>
                {
                    AddDraftCutPoint(e.WorldPoint);
                    FinishDraftCutLine();
                });

            AddContextMenuItem(
                menu,
                "Finish Line",
                FinishDraftCutLine);

            AddContextMenuItem(
                menu,
                "Undo Last Point",
                UndoLastDraftCutPoint);

            AddContextMenuItem(
                menu,
                "Cancel Line",
                CancelDraftCutLine);
        }
        else
        {
            AddContextMenuItem(
                menu,
                "Continue Cut Line Here",
                () =>
                {
                    SetTool(LineEditorTool.Cut);
                    _draftCutDrawing = true;
                    AddDraftCutPoint(e.WorldPoint);
                });

            AddContextMenuItem(
                menu,
                "Apply Cut",
                ApplyPendingCuts);

            AddContextMenuItem(
                menu,
                "Cancel Line",
                CancelDraftCutLine);

            AddContextMenuItem(
                menu,
                "Start New Cut Line Here",
                () =>
                {
                    _draftCutPoints.Clear();
                    BeginDraftCutLineAt(e.WorldPoint);
                });
        }

        menu.IsOpen = true;
    }

    private static void AddContextMenuItem(
        System.Windows.Controls.ContextMenu menu,
        string header,
        Action action)
    {
        var item = new System.Windows.Controls.MenuItem
        {
            Header = header
        };

        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void BeginDraftCutLineAt(Point point)
    {
        _draftCutPoints.Clear();
        _draftCutPreviewPoint = null;
        _draftCutDrawing = true;
        SetTool(LineEditorTool.Cut);
        AddDraftCutPoint(point);
    }

    private void AddDraftCutPoint(Point point)
    {
        _draftCutPoints.Add(point);
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateStatus(
            $"Cut line has {_draftCutPoints.Count:N0} point(s).");
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
        ApplyPendingCuts();
    }

    private void ApplyPendingCuts()
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
        {
            _undo.Push(snapshot);
            _contextGeometry.Reload();
        }

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
        UndoLastDraftCutPoint();
    }

    private void UndoLastDraftCutPoint()
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
        _contextGeometry.Reload();
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

        _contextGeometry = new KmlLineGeometryService(
            _workingDocument,
            _projectionScale);

        _geometry = new KmlLineGeometryService(
            _workingDocument,
            _projectionScale,
            _isScoped ? ScopeMarkerAttribute : null);

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
        var outputDocument = CreateOutputDocument();

        if (XNode.DeepEquals(
                _originalDocument.Root,
                outputDocument.Root))
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

    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private static void ApplyScopeMarkers(
        XDocument document,
        IReadOnlyCollection<int> placemarkIndexes)
    {
        var placemarks = document
            .Descendants(KmlNs + "Placemark")
            .ToList();

        foreach (var index in placemarkIndexes)
        {
            if (index < 0 || index >= placemarks.Count)
                continue;

            placemarks[index].SetAttributeValue(
                ScopeMarkerAttribute,
                "true");
        }
    }

    private XDocument CreateOutputDocument()
    {
        var output = new XDocument(_workingDocument);

        foreach (var attribute in output
                     .Descendants()
                     .Attributes(ScopeMarkerAttribute)
                     .ToList())
        {
            attribute.Remove();
        }

        return output;
    }

    private void UpdateScopeText()
    {
        var currentPlacemarkCount = _isScoped
            ? _workingDocument
                .Descendants(KmlNs + "Placemark")
                .Count(placemark =>
                    string.Equals(
                        placemark.Attribute(ScopeMarkerAttribute)?.Value,
                        "true",
                        StringComparison.Ordinal))
            : _workingDocument
                .Descendants(KmlNs + "Placemark")
                .Count();

        ScopeText.Text = _isScoped
            ? $"Editing scope: {_scopeDisplayName} — {currentPlacemarkCount:N0} placemark(s), {_geometry.Features.Count:N0} editable line(s). Lines outside this scope are shown light grey and will not be changed."
            : $"Editing scope: Entire document — {currentPlacemarkCount:N0} placemark(s), {_geometry.Features.Count:N0} editable line(s).";
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
        UpdateScopeText();

        var guideVisuals = _guides
            .Select(guide =>
                new LineEditorGuideVisual(
                    ProjectGuide(guide.Guide),
                    guide.Applied))
            .ToList();

        var editableElements = _geometry.Features
            .Select(feature => feature.LineStringElement)
            .ToHashSet();

        var contextOnly = _contextGeometry.Features
            .Where(feature =>
                !editableElements.Contains(feature.LineStringElement))
            .ToList();

        EditorSurface.SetDocument(
            _geometry.Features,
            contextOnly,
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
        var bounds = _contextGeometry.WorldBounds;

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
            $"{_geometry.Features.Count:N0} editable {CurrentLineNoun}; " +
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
