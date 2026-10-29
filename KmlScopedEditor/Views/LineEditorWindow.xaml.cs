using System.Windows;
using System.Xml.Linq;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Keyboard = System.Windows.Input.Keyboard;

namespace KmlScopedEditor.Views;

public partial class LineEditorWindow : Window
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private static readonly XNamespace EditorNs =
        "urn:kml-scoped-editor:temporary";

    private static readonly XName ScopeMarkerAttribute =
        EditorNs + "line-editor-scope";

    private static readonly XName CutTargetMarkerAttribute =
        EditorNs + "line-editor-cut-target";

    private readonly XDocument _originalDocument;
    private XDocument _workingDocument;
    private KmlLineGeometryService _geometry;
    private KmlLineGeometryService _contextGeometry;
    private readonly double _projectionScale;
    private readonly bool _isScoped;
    private readonly string _scopeDisplayName;

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

        _isScoped = scopedPlacemarkIndexes is not null;
        _scopeDisplayName = string.IsNullOrWhiteSpace(scopeDisplayName)
            ? (_isScoped ? "selected tree scope" : "entire document")
            : scopeDisplayName;

        if (_isScoped)
        {
            ApplyScopeMarkers(
                _workingDocument,
                scopedPlacemarkIndexes!);
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
            SetTool(LineEditorTool.Select);
            RefreshSurface(fitView: true);
            UpdateStatus("Ready.");
        };
    }

    public XDocument EditedDocument => CreateOutputDocument();

    private void OnPanToolClick(
        object sender,
        RoutedEventArgs e)
    {
        PauseDraftCut();
        SetTool(LineEditorTool.Pan);
    }

    private void OnSelectToolClick(
        object sender,
        RoutedEventArgs e)
    {
        PauseDraftCut();
        SetTool(LineEditorTool.Select);
    }

    private void OnCutToolClick(
        object sender,
        RoutedEventArgs e)
    {
        SetTool(LineEditorTool.Cut);
        _draftCutDrawing = true;
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateLiveCutPreview();

        UpdateStatus(
            _draftCutPoints.Count == 0
                ? "Cut tool ready. Click points or drag freehand to draw a cut line."
                : $"Continuing cut line from {_draftCutPoints.Count:N0} existing point(s).");
    }

    private void SetTool(LineEditorTool tool)
    {
        EditorSurface.Tool = tool;

        PanToolButton.IsChecked = tool == LineEditorTool.Pan;
        SelectToolButton.IsChecked = tool == LineEditorTool.Select;
        CutToolButton.IsChecked = tool == LineEditorTool.Cut;

        SelectActionsPanel.Visibility =
            tool == LineEditorTool.Select
                ? Visibility.Visible
                : Visibility.Collapsed;

        CutActionsPanel.Visibility =
            tool == LineEditorTool.Cut
                ? Visibility.Visible
                : Visibility.Collapsed;

        EditorSurface.Cursor = tool switch
        {
            LineEditorTool.Pan =>
                System.Windows.Input.Cursors.Hand,
            LineEditorTool.Cut =>
                System.Windows.Input.Cursors.Cross,
            _ =>
                System.Windows.Input.Cursors.Arrow
        };

        InstructionText.Text = tool switch
        {
            LineEditorTool.Pan =>
                "HAND: left-drag to pan. Mouse wheel zooms. Space+drag and middle-drag also pan.",
            LineEditorTool.Select =>
                "SELECT: drag across editable lines to select real crossings. Alt+drag deselects. Click selects the nearest line. Delete removes highlighted lines.",
            _ =>
                "CUT: click points or drag freehand. Double-click, right-click, or Enter finishes and applies the drawn cut. Backspace removes the last point; Esc cancels."
        };

        ToolHintText.Text = tool switch
        {
            LineEditorTool.Pan =>
                "H Hand · Space+drag or middle-drag pan · +/- zoom · F fit",
            LineEditorTool.Select =>
                "V Select · drag across lines · Alt+drag deselect · Delete remove · Space+drag pan",
            _ =>
                "C Cut · click points or drag freehand · double-click/right-click/Enter finish · Backspace undo point · Esc cancel"
        };

        UpdateCutScopeDisplay();
    }

    private void OnStrokeCompleted(
        object? sender,
        LineEditorStrokeEventArgs e)
    {
        if (e.Tool == LineEditorTool.Select)
        {
            var matches = _geometry.FindIntersectingFeatures(
                e.WorldPoints);

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
            UpdateCutScopeDisplay();
            UpdatePendingGuidePreview();

            var noun = CurrentLineNoun;

            UpdateStatus(
                matches.Count == 0
                    ? $"No {noun} crossed by that selection stroke."
                    : e.IsDeselect
                        ? $"Removed {matches.Count:N0} crossed {noun} from the selection."
                        : $"Added {matches.Count:N0} crossed {noun} to the selection.");

            return;
        }

        if (e.Tool != LineEditorTool.Cut)
            return;

        _draftCutDrawing = true;
        AppendDraftPoints(e.WorldPoints);
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateLiveCutPreview();

        UpdateStatus(
            $"Added a freehand section. Cut line now has {_draftCutPoints.Count:N0} sampled point(s). " +
            "Continue drawing, or right-click/Enter to finish.");
    }

    private void OnLineClicked(
        object? sender,
        LineEditorClickEventArgs e)
    {
        if (e.Tool == LineEditorTool.Cut)
        {
            if (!_draftCutDrawing)
                _draftCutDrawing = true;

            AddDraftCutPoint(e.WorldPoint);
            UpdateLiveCutPreview();

            if (e.IsDoubleClick &&
                _draftCutPoints.Count >= 2)
            {
                FinishAndApplyDrawnCut();
                return;
            }

            UpdateStatus(
                _draftCutPoints.Count < 2
                    ? "Cut line started. Add at least one more point."
                    : $"Cut line has {_draftCutPoints.Count:N0} point(s). Double-click, right-click, or press Enter to finish.");

            return;
        }

        if (e.Tool != LineEditorTool.Select)
            return;

        var tolerance =
            EditorSurface.WorldToleranceFromPixels(6d);

        var feature = _geometry.FindNearestFeature(
            e.WorldPoint,
            tolerance);

        if (feature is null)
        {
            UpdateStatus("No editable line close enough to that click.");
            return;
        }

        if (e.IsDeselect)
        {
            _selected.Remove(feature.LineStringElement);
            UpdateStatus(
                $"Removed one {CurrentLineNoun} from the selection.");
        }
        else
        {
            _selected.Clear();
            _selected.Add(feature.LineStringElement);
            UpdateStatus(
                $"Selected one {CurrentLineNoun}.");
        }

        EditorSurface.SetSelection(_selected);
        UpdateCutScopeDisplay();
        UpdatePendingGuidePreview();
    }

    private void OnPointerMoved(
        object? sender,
        LineEditorPointerEventArgs e)
    {
        if (EditorSurface.Tool == LineEditorTool.Cut)
        {
            EditorSurface.SetHovered(null);

            if (_draftCutDrawing &&
                _draftCutPoints.Count > 0)
            {
                _draftCutPreviewPoint = e.WorldPoint;

                EditorSurface.SetDraftCut(
                    _draftCutPoints,
                    _draftCutPreviewPoint);

                UpdateLiveCutPreview();
            }

            return;
        }

        if (EditorSurface.Tool == LineEditorTool.Pan)
        {
            EditorSurface.SetHovered(null);
            return;
        }

        var tolerance =
            EditorSurface.WorldToleranceFromPixels(4d);

        var feature = _geometry.FindNearestFeature(
            e.WorldPoint,
            tolerance);

        EditorSurface.SetHovered(
            feature?.LineStringElement);
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

        UpdateLiveCutPreview();
    }

    private void OnContextRequested(
        object? sender,
        LineEditorContextEventArgs e)
    {
        if (EditorSurface.Tool == LineEditorTool.Cut)
        {
            if (_draftCutPoints.Count == 0)
            {
                _draftCutDrawing = true;
                AddDraftCutPoint(e.WorldPoint);
                UpdateLiveCutPreview();
                UpdateStatus(
                    "Cut line started. Add another point, then right-click again to finish.");
                return;
            }

            if (_draftCutPoints.Count >= 2)
            {
                FinishAndApplyDrawnCut();
                return;
            }
        }

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = EditorSurface,
            Placement =
                System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        AddContextMenuItem(
            menu,
            "Start Cut Line Here",
            () =>
            {
                CancelDraftCutLine(switchToSelect: false);
                SetTool(LineEditorTool.Cut);
                _draftCutDrawing = true;
                AddDraftCutPoint(e.WorldPoint);
                UpdateLiveCutPreview();
            });

        AddContextMenuItem(
            menu,
            "Select Tool",
            () => SetTool(LineEditorTool.Select));

        AddContextMenuItem(
            menu,
            "Hand Tool",
            () => SetTool(LineEditorTool.Pan));

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

    private void OnImportCutLinesClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Cut Lines",
            Filter =
                "Google Earth files (*.kml;*.kmz)|*.kml;*.kmz|" +
                "KML files (*.kml)|*.kml|" +
                "KMZ files (*.kmz)|*.kmz",
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
                var guides =
                    _cutGuideLoader.Load(fileName);

                foreach (var guide in guides)
                {
                    _guides.Add(
                        new CutGuideState(
                            guide,
                            removeMatchingSourceBeforeCut: false));

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
            UpdateStatus(
                "No LineString paths were found in the selected cutline file.");
            return;
        }

        RefreshSurface(fitView: true);
        UpdatePendingGuidePreview();

        UpdateStatus(
            $"Imported {imported:N0} cut guide(s). Review the intersection markers, then click Apply Imported Guide.");
    }

    private void OnUseSelectedAsCutlineClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_selected.Count != 1)
        {
            UpdateStatus(
                "Select exactly one editable line, then choose Use Selected as Cutline.");
            return;
        }

        var selectedElement = _selected.Single();

        var feature =
            _geometry.Features.FirstOrDefault(item =>
                ReferenceEquals(
                    item.LineStringElement,
                    selectedElement));

        if (feature is null)
        {
            UpdateStatus(
                "The selected line is no longer available.");
            return;
        }

        var guide = new KmlCutGuide(
            feature.Name,
            feature.Coordinates.ToList());

        _guides.Add(
            new CutGuideState(
                guide,
                removeMatchingSourceBeforeCut: true));

        // This selection identified the helper line. Clearing it prevents the
        // helper from also becoming the cut target scope.
        _selected.Clear();
        EditorSurface.SetSelection(_selected);

        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();

        UpdateStatus(
            "Selected line converted to a pending cut guide. It will be removed before the cut is calculated, so it cannot cut itself.");
    }

    private void OnApplyCutLinesClick(
        object sender,
        RoutedEventArgs e)
    {
        ApplyPendingGuides();
    }

    private void ApplyPendingGuides()
    {
        var pending = _guides
            .Where(guide => !guide.Applied)
            .ToList();

        if (pending.Count == 0)
        {
            UpdateStatus(
                _guides.Count == 0
                    ? "No imported or existing cut guides are loaded."
                    : "All loaded cut guides have already been applied.");
            return;
        }

        ApplyGuideStates(
            pending,
            "Applied imported/existing cut guide(s).");
    }

    private void FinishAndApplyDrawnCut()
    {
        if (_draftCutPoints.Count < 2)
        {
            UpdateStatus(
                "The cut line needs at least two points.");
            return;
        }

        _draftCutDrawing = false;
        _draftCutPreviewPoint = null;

        var guide = new KmlCutGuide(
            "Drawn cut line",
            _draftCutPoints
                .Select(_geometry.Unproject)
                .ToList());

        var state = new CutGuideState(
            guide,
            removeMatchingSourceBeforeCut: false);

        _guides.Add(state);

        _draftCutPoints.Clear();

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        ApplyGuideStates(
            new[] { state },
            "Drawn cut applied.");

        SetTool(LineEditorTool.Select);
    }

    private void ApplyGuideStates(
        IReadOnlyList<CutGuideState> guideStates,
        string successPrefix)
    {
        if (guideStates.Count == 0)
            return;

        var snapshot = CaptureUndoState();
        var selectedTargets = GetValidSelectedLines();
        var useSelectedTargets = selectedTargets.Count > 0;

        ClearCutTargetMarkers();

        if (useSelectedTargets)
        {
            foreach (var line in selectedTargets)
            {
                line.SetAttributeValue(
                    CutTargetMarkerAttribute,
                    "true");
            }
        }

        var totalLines = 0;
        var totalIntersections = 0;
        var totalPieces = 0;
        var helperLinesRemoved = 0;
        var allCutPoints = new List<Point>();
        var changed = false;

        try
        {
            foreach (var guideState in guideStates)
            {
                if (guideState.RemoveMatchingSourceBeforeCut)
                {
                    var helper = FindMatchingFeature(
                        guideState.Guide.Coordinates);

                    if (helper is not null)
                    {
                        helperLinesRemoved +=
                            _geometry.DeleteLines(
                                new[]
                                {
                                    helper.LineStringElement
                                });

                        _contextGeometry.Reload();
                        changed = true;
                    }
                }

                var worldPoints =
                    ProjectGuide(guideState.Guide);

                var result = _geometry.CutWithStroke(
                    worldPoints,
                    useSelectedTargets
                        ? CutTargetMarkerAttribute
                        : null);

                totalLines += result.LinesSplit;
                totalIntersections +=
                    result.IntersectionCount;
                totalPieces += result.OutputSegments;
                allCutPoints.AddRange(
                    result.CutPoints);

                if (result.LinesSplit > 0)
                    changed = true;

                guideState.Applied = true;
            }
        }
        finally
        {
            ClearCutTargetMarkers();
            _geometry.Reload();
            _contextGeometry.Reload();
        }

        _selected.Clear();
        _cutPoints.Clear();
        _cutPoints.AddRange(allCutPoints);

        if (totalLines > 0)
            _hasPerformedCut = true;

        if (changed)
            _undo.Push(snapshot);

        RefreshSurface();
        UpdateCutScopeDisplay();

        if (!changed)
        {
            UpdateStatus(
                "The guide did not split any lines in the current cut scope.");
            return;
        }

        UpdateStatus(
            $"{successPrefix} " +
            $"{totalLines:N0} line(s) cut at " +
            $"{totalIntersections:N0} intersection(s), producing " +
            $"{totalPieces:N0} piece(s)." +
            (helperLinesRemoved > 0
                ? $" Removed {helperLinesRemoved:N0} helper line(s) before cutting."
                : string.Empty));
    }

    private void OnClearGuidesClick(
        object sender,
        RoutedEventArgs e)
    {
        _guides.Clear();
        _cutPoints.Clear();
        CancelDraftCutLine(
            switchToSelect: false);

        RefreshSurface();
        UpdateCutScopeDisplay();

        UpdateStatus(
            "Cut guides cleared. Existing cuts were not changed.");
    }

    private void OnDeleteSelectedClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_selected.Count == 0)
        {
            UpdateStatus(
                "Nothing is selected for deletion.");
            return;
        }

        var snapshot = CaptureUndoState();

        var deleted =
            _geometry.DeleteLines(_selected);

        if (deleted == 0)
        {
            UpdateStatus(
                "The selected lines could not be deleted.");
            return;
        }

        _undo.Push(snapshot);
        _selected.Clear();
        _contextGeometry.Reload();

        RefreshSurface();
        UpdateCutScopeDisplay();

        UpdateStatus(
            $"Deleted {deleted:N0} selected {CurrentLineNoun}.");
    }

    private void OnClearSelectionClick(
        object sender,
        RoutedEventArgs e)
    {
        _selected.Clear();
        EditorSurface.SetSelection(_selected);

        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();
        UpdateStatus("Selection cleared.");
    }

    private void OnInvertSelectionClick(
        object sender,
        RoutedEventArgs e)
    {
        var inverted = _geometry.Features
            .Where(feature =>
                !_selected.Contains(
                    feature.LineStringElement))
            .Select(feature =>
                feature.LineStringElement)
            .ToList();

        _selected.Clear();

        foreach (var line in inverted)
            _selected.Add(line);

        EditorSurface.SetSelection(_selected);

        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();
        UpdateStatus("Selection inverted.");
    }

    private void OnUndoClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_undo.Count == 0)
        {
            UpdateStatus(
                "There is no cut or deletion to undo.");
            return;
        }

        var state = _undo.Pop();

        _workingDocument = state.Document;

        _contextGeometry =
            new KmlLineGeometryService(
                _workingDocument,
                _projectionScale);

        _geometry =
            new KmlLineGeometryService(
                _workingDocument,
                _projectionScale,
                _isScoped
                    ? ScopeMarkerAttribute
                    : null);

        _hasPerformedCut =
            state.HasPerformedCut;

        foreach (var guide in _guides)
        {
            guide.Applied =
                state.GuideApplied.TryGetValue(
                    guide.Id,
                    out var applied) &&
                applied;
        }

        _cutPoints.Clear();
        _cutPoints.AddRange(
            state.CutPoints);

        _selected.Clear();

        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();

        UpdateStatus(
            "Last cut/deletion undone. Guide overlays remain available.");
    }

    private void OnFitViewClick(
        object sender,
        RoutedEventArgs e)
    {
        EditorSurface.FitView();
    }

    private void OnZoomSelectionClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!EditorSurface.ZoomToSelection())
        {
            UpdateStatus(
                "Select one or more lines before using Zoom Selection.");
        }
    }

    private void OnZoomInClick(
        object sender,
        RoutedEventArgs e)
    {
        EditorSurface.ZoomBy(1.25d);
    }

    private void OnZoomOutClick(
        object sender,
        RoutedEventArgs e)
    {
        EditorSurface.ZoomBy(0.8d);
    }

    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        var control =
            (Keyboard.Modifiers & ModifierKeys.Control) ==
            ModifierKeys.Control;

        if (control && e.Key == Key.Z)
        {
            OnUndoClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            OnDeleteSelectedClick(
                this,
                new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back &&
            _draftCutPoints.Count > 0)
        {
            UndoLastDraftCutPoint();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            _draftCutPoints.Count >= 2)
        {
            FinishAndApplyDrawnCut();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_draftCutPoints.Count > 0 ||
                _draftCutDrawing)
            {
                CancelDraftCutLine(
                    switchToSelect: false);
            }
            else if (EditorSurface.Tool ==
                     LineEditorTool.Cut)
            {
                SetTool(LineEditorTool.Select);
            }

            e.Handled = true;
            return;
        }

        if (control)
            return;

        switch (e.Key)
        {
            case Key.V:
                PauseDraftCut();
                SetTool(LineEditorTool.Select);
                e.Handled = true;
                break;

            case Key.C:
                OnCutToolClick(
                    this,
                    new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.H:
                PauseDraftCut();
                SetTool(LineEditorTool.Pan);
                e.Handled = true;
                break;

            case Key.F:
                EditorSurface.FitView();
                e.Handled = true;
                break;

            case Key.OemPlus:
            case Key.Add:
                EditorSurface.ZoomBy(1.25d);
                e.Handled = true;
                break;

            case Key.OemMinus:
            case Key.Subtract:
                EditorSurface.ZoomBy(0.8d);
                e.Handled = true;
                break;
        }
    }

    private void OnApplyClick(
        object sender,
        RoutedEventArgs e)
    {
        var outputDocument =
            CreateOutputDocument();

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

    private void AddDraftCutPoint(
        Point point)
    {
        var tolerance =
            EditorSurface.WorldToleranceFromPixels(1.5d);

        if (_draftCutPoints.Count > 0 &&
            (_draftCutPoints[^1] - point).Length <= tolerance)
        {
            return;
        }

        _draftCutPoints.Add(point);
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);
    }

    private void AppendDraftPoints(
        IReadOnlyList<Point> points)
    {
        foreach (var point in points)
            AddDraftCutPoint(point);
    }

    private void UndoLastDraftCutPoint()
    {
        if (_draftCutPoints.Count == 0)
        {
            UpdateStatus(
                "There are no cut points to undo.");
            return;
        }

        _draftCutPoints.RemoveAt(
            _draftCutPoints.Count - 1);

        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateLiveCutPreview();

        UpdateStatus(
            _draftCutPoints.Count == 0
                ? "Drawn cut line cleared."
                : $"Removed the last cut point. {_draftCutPoints.Count:N0} point(s) remain.");
    }

    private void PauseDraftCut()
    {
        _draftCutDrawing = false;
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);
    }

    private void CancelDraftCutLine(
        bool switchToSelect)
    {
        _draftCutPoints.Clear();
        _draftCutPreviewPoint = null;
        _draftCutDrawing = false;
        _cutPoints.Clear();
        EditorSurface.SetCutPoints(_cutPoints);

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        if (switchToSelect)
            SetTool(LineEditorTool.Select);
        else
            UpdateCutScopeDisplay();
    }

    private void UpdateLiveCutPreview()
    {
        if (_draftCutPoints.Count == 0)
        {
            UpdatePendingGuidePreview();
            return;
        }

        var stroke =
            _draftCutPoints.ToList();

        if (_draftCutPreviewPoint.HasValue)
            stroke.Add(
                _draftCutPreviewPoint.Value);

        if (stroke.Count < 2)
        {
            _cutPoints.Clear();
            EditorSurface.SetCutPoints(_cutPoints);
            UpdateCutScopeDisplay();
            return;
        }

        var preview = _geometry.PreviewCutWithStroke(
            stroke,
            GetSelectedTargetSet());

        _cutPoints.Clear();
        _cutPoints.AddRange(
            preview.CutPoints);

        EditorSurface.SetCutPoints(_cutPoints);
        UpdateCutScopeDisplay(preview);
    }

    private void UpdatePendingGuidePreview()
    {
        if (_draftCutPoints.Count > 0)
        {
            UpdateLiveCutPreview();
            return;
        }

        var pending = _guides
            .Where(guide => !guide.Applied)
            .ToList();

        if (pending.Count == 0)
        {
            UpdateCutScopeDisplay();
            return;
        }

        var selectedTargets =
            GetSelectedTargetSet();

        var allPoints =
            new List<Point>();

        var lines = new HashSet<XElement>();
        var intersections = 0;

        foreach (var guideState in pending)
        {
            var targetSet = selectedTargets is null
                ? _geometry.Features
                    .Select(feature =>
                        feature.LineStringElement)
                    .ToHashSet()
                : new HashSet<XElement>(
                    selectedTargets);

            if (guideState.RemoveMatchingSourceBeforeCut)
            {
                var helper = FindMatchingFeature(
                    guideState.Guide.Coordinates);

                if (helper is not null)
                    targetSet.Remove(
                        helper.LineStringElement);
            }

            var preview =
                _geometry.PreviewCutWithStroke(
                    ProjectGuide(
                        guideState.Guide),
                    targetSet);

            intersections +=
                preview.IntersectionCount;

            allPoints.AddRange(
                preview.CutPoints);

            foreach (var feature in
                     _geometry.FindIntersectingFeatures(
                         ProjectGuide(
                             guideState.Guide)))
            {
                if (targetSet.Contains(
                        feature.LineStringElement))
                {
                    lines.Add(
                        feature.LineStringElement);
                }
            }
        }

        _cutPoints.Clear();
        _cutPoints.AddRange(allPoints);

        EditorSurface.SetCutPoints(_cutPoints);

        var previewSummary =
            new LineCutPreview(
                lines.Count,
                intersections,
                allPoints);

        UpdateCutScopeDisplay(
            previewSummary);
    }

    private IReadOnlySet<XElement>?
        GetSelectedTargetSet()
    {
        var valid = GetValidSelectedLines();

        return valid.Count == 0
            ? null
            : valid.ToHashSet();
    }

    private List<XElement>
        GetValidSelectedLines()
    {
        return _selected
            .Where(line =>
                line.Document == _workingDocument &&
                _geometry.Features.Any(
                    feature =>
                        ReferenceEquals(
                            feature.LineStringElement,
                            line)))
            .ToList();
    }

    private void UpdateCutScopeDisplay(
        LineCutPreview? preview = null)
    {
        var validSelected =
            GetValidSelectedLines();

        MapScopeText.Text =
            validSelected.Count > 0
                ? $"Cut affects: {validSelected.Count:N0} selected editable line(s)"
                : $"Cut affects: all {_geometry.Features.Count:N0} editable line(s) in {_scopeDisplayName}";

        if (preview.HasValue)
        {
            ToolHintText.Text =
                $"{ToolHintForCurrentTool()} · Will cut {preview.Value.LinesAffected:N0} line(s) at {preview.Value.IntersectionCount:N0} point(s)";
        }
        else
        {
            ToolHintText.Text =
                ToolHintForCurrentTool();
        }
    }

    private string ToolHintForCurrentTool()
    {
        return EditorSurface.Tool switch
        {
            LineEditorTool.Pan =>
                "H Hand · Space+drag or middle-drag pan · +/- zoom · F fit",
            LineEditorTool.Select =>
                "V Select · drag across lines · Alt+drag deselect · Delete remove · Space+drag pan",
            _ =>
                "C Cut · click points or drag freehand · double-click/right-click/Enter finish · Backspace undo point · Esc cancel"
        };
    }

    private void UpdateScopeText()
    {
        var currentPlacemarkCount =
            _isScoped
                ? _workingDocument
                    .Descendants(
                        KmlNs + "Placemark")
                    .Count(placemark =>
                        string.Equals(
                            placemark
                                .Attribute(
                                    ScopeMarkerAttribute)
                                ?.Value,
                            "true",
                            StringComparison.Ordinal))
                : _workingDocument
                    .Descendants(
                        KmlNs + "Placemark")
                    .Count();

        ScopeText.Text =
            _isScoped
                ? $"Editing scope: {_scopeDisplayName} — {currentPlacemarkCount:N0} placemark(s), {_geometry.Features.Count:N0} editable line(s). Lines outside this scope are light grey and locked."
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

    private IReadOnlyList<Point> ProjectGuide(
        KmlCutGuide guide)
    {
        return guide.Coordinates
            .Select(_geometry.Project)
            .ToList();
    }

    private KmlLineFeature? FindMatchingFeature(
        IReadOnlyList<KmlCoordinate> coordinates)
    {
        return _geometry.Features
            .FirstOrDefault(feature =>
                CoordinatesMatch(
                    feature.Coordinates,
                    coordinates));
    }

    private static bool CoordinatesMatch(
        IReadOnlyList<KmlCoordinate> left,
        IReadOnlyList<KmlCoordinate> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0;
             index < left.Count;
             index++)
        {
            if (Math.Abs(
                    left[index].Longitude -
                    right[index].Longitude) >
                0.0000000001d ||
                Math.Abs(
                    left[index].Latitude -
                    right[index].Latitude) >
                0.0000000001d)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshSurface(
        bool fitView = false)
    {
        UpdateScopeText();

        var guideVisuals = _guides
            .Select(guide =>
                new LineEditorGuideVisual(
                    ProjectGuide(
                        guide.Guide),
                    guide.Applied))
            .ToList();

        var editableElements =
            _geometry.Features
                .Select(feature =>
                    feature.LineStringElement)
                .ToHashSet();

        var contextOnly =
            _contextGeometry.Features
                .Where(feature =>
                    !editableElements.Contains(
                        feature.LineStringElement))
                .ToList();

        EditorSurface.SetDocument(
            _geometry.Features,
            contextOnly,
            _selected,
            GetDisplayBounds(
                guideVisuals),
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
        var bounds =
            _contextGeometry.WorldBounds;

        foreach (var guide in guides)
        {
            foreach (var point in
                     guide.WorldPoints)
            {
                if (bounds.IsEmpty)
                    bounds =
                        new Rect(point, point);
                else
                    bounds.Union(point);
            }
        }

        return bounds;
    }

    private string CurrentLineNoun =>
        _hasPerformedCut
            ? "line fragment(s)"
            : "contour(s)";

    private void UpdateStatus(
        string message)
    {
        StatusText.Text =
            $"{message}  " +
            $"{_geometry.Features.Count:N0} editable {CurrentLineNoun}; " +
            $"{_selected.Count:N0} selected; " +
            $"{_guides.Count:N0} guide(s); " +
            $"{_undo.Count:N0} undo step(s).";
    }

    private static void ApplyScopeMarkers(
        XDocument document,
        IReadOnlyCollection<int> placemarkIndexes)
    {
        var placemarks = document
            .Descendants(
                KmlNs + "Placemark")
            .ToList();

        foreach (var index in
                 placemarkIndexes)
        {
            if (index < 0 ||
                index >= placemarks.Count)
            {
                continue;
            }

            placemarks[index].SetAttributeValue(
                ScopeMarkerAttribute,
                "true");
        }
    }

    private void ClearCutTargetMarkers()
    {
        foreach (var attribute in
                 _workingDocument
                     .Descendants()
                     .Attributes(
                         CutTargetMarkerAttribute)
                     .ToList())
        {
            attribute.Remove();
        }
    }

    private XDocument CreateOutputDocument()
    {
        var output =
            new XDocument(_workingDocument);

        foreach (var attribute in
                 output
                     .Descendants()
                     .Attributes()
                     .Where(attribute =>
                         attribute.Name ==
                             ScopeMarkerAttribute ||
                         attribute.Name ==
                             CutTargetMarkerAttribute)
                     .ToList())
        {
            attribute.Remove();
        }

        return output;
    }

    private sealed class CutGuideState
    {
        public CutGuideState(
            KmlCutGuide guide,
            bool removeMatchingSourceBeforeCut)
        {
            Guide = guide;
            RemoveMatchingSourceBeforeCut =
                removeMatchingSourceBeforeCut;
        }

        public Guid Id { get; } =
            Guid.NewGuid();

        public KmlCutGuide Guide { get; }

        public bool
            RemoveMatchingSourceBeforeCut
        {
            get;
        }

        public bool Applied { get; set; }
    }

    private sealed record UndoState(
        XDocument Document,
        bool HasPerformedCut,
        Dictionary<Guid, bool> GuideApplied,
        IReadOnlyList<Point> CutPoints);
}
