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
    private readonly double _projectionScale;

    private readonly KmlCutGuideService _cutGuideLoader = new();
    private readonly List<CutGuideState> _guides = new();
    private Guid? _editingGuideId;
    private readonly List<Point> _cutPoints = new();
    private readonly List<Point> _draftCutPoints = new();
    private Point? _draftCutPreviewPoint;
    private bool _draftCutDrawing;

    private readonly HashSet<XElement> _committedPreviewLines = new();
    private readonly List<Point> _committedPreviewPoints = new();
    private readonly HashSet<XElement> _activeStrokePreviewLines = new();
    private readonly List<Point> _activeStrokePreviewPoints = new();
    private int _activeStrokePreviewProcessedPoints;

    private readonly HashSet<XElement> _selected = new();
    private readonly Stack<UndoState> _undo = new();

    private bool _hasPerformedCut;
    private int _workflowStep = 1;

    public LineEditorWindow(
        XDocument sourceDocument,
        string? sourceName = null)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(sourceDocument);

        _originalDocument = new XDocument(sourceDocument);
        _workingDocument = new XDocument(sourceDocument);

        _geometry = new KmlLineGeometryService(
            _workingDocument);

        _projectionScale = _geometry.LongitudeScale;

        if (!string.IsNullOrWhiteSpace(sourceName))
            Title = $"Edit Lines — {sourceName}";

        EditorSurface.StrokeCompleted += OnStrokeCompleted;
        EditorSurface.SplitStrokePreviewChanged += OnSplitStrokePreviewChanged;
        EditorSurface.LineClicked += OnLineClicked;
        EditorSurface.PointerMoved += OnPointerMoved;
        EditorSurface.PointerExited += OnPointerExited;
        EditorSurface.ContextRequested += OnContextRequested;
        EditorSurface.EditableGuideChanged += OnEditableGuideChanged;
        PreviewKeyDown += OnPreviewKeyDown;

        Loaded += (_, _) =>
        {
            SetWorkflowStep(1);
            RefreshSurface(fitView: true);
            UpdateStatus("Ready. Remove any unwanted source lines, or go straight to Split.");
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

    private void OnSplitToolClick(
        object sender,
        RoutedEventArgs e)
    {
        SetWorkflowStep(2);
        BeginSplitMode();
    }

    private void OnStep1Click(
        object sender,
        RoutedEventArgs e)
    {
        SetWorkflowStep(1);
    }

    private void OnStep2Click(
        object sender,
        RoutedEventArgs e)
    {
        SetWorkflowStep(2);
    }

    private void OnStep3Click(
        object sender,
        RoutedEventArgs e)
    {
        SetWorkflowStep(3);
    }

    private void SetWorkflowStep(int step)
    {
        _workflowStep = Math.Clamp(step, 1, 3);

        Step1Button.IsChecked = _workflowStep == 1;
        Step2Button.IsChecked = _workflowStep == 2;
        Step3Button.IsChecked = _workflowStep == 3;

        Step1ActionsPanel.Visibility =
            _workflowStep == 1
                ? Visibility.Visible
                : Visibility.Collapsed;

        Step2ActionsPanel.Visibility =
            _workflowStep == 2
                ? Visibility.Visible
                : Visibility.Collapsed;

        Step3ActionsPanel.Visibility =
            _workflowStep == 3
                ? Visibility.Visible
                : Visibility.Collapsed;

        PauseDraftCut();

        SetTool(
            _workflowStep == 2
                ? LineEditorTool.Split
                : LineEditorTool.Select);

        UpdateCutScopeDisplay();
    }

    private void OnDrawSplitLineClick(
        object sender,
        RoutedEventArgs e)
    {
        SetWorkflowStep(2);

        var pending = _guides
            .Where(guide => !guide.Applied)
            .ToList();

        if (pending.Count > 0)
        {
            foreach (var guide in pending)
                _guides.Remove(guide);

            StopEditingGuide();
        }

        CancelDraftCutLine(switchToSelect: false);
        RefreshSurface();
        BeginSplitMode();

        UpdateStatus(
            "Draw the split line by clicking points or dragging. Press Apply split or Enter when it is ready.");
    }

    private void BeginSplitMode()
    {
        SetTool(LineEditorTool.Split);
        _draftCutDrawing = true;
        _draftCutPreviewPoint = null;
        RebuildCommittedSplitPreview();
        ResetActiveStrokePreview();

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateLiveSplitPreview();

        UpdateStatus(
            _draftCutPoints.Count == 0
                ? "Draw or import a split line. Nothing needs to be selected unless you want to limit the split."
                : $"Continuing split line from {_draftCutPoints.Count:N0} existing point(s).");
    }

    private void SetTool(LineEditorTool tool)
    {
        EditorSurface.Tool = tool;

        EditorSurface.Cursor = tool switch
        {
            LineEditorTool.Pan =>
                System.Windows.Input.Cursors.Hand,
            LineEditorTool.Split =>
                System.Windows.Input.Cursors.Cross,
            _ =>
                System.Windows.Input.Cursors.Arrow
        };

        InstructionText.Text = _workflowStep switch
        {
            1 =>
                "Remove unwanted source lines. Drag across lines to select them, then Delete selected.",
            2 =>
                "Nothing needs to be selected. Import a split line or draw one; orange dots show where lines will be split.",
            _ =>
                "Remove unwanted pieces after the split. Drag across pieces to select them, then Delete selected."
        };

        ToolHintText.Text = tool switch
        {
            LineEditorTool.Pan =>
                "Right-drag / Space+drag / middle-drag pans · mouse wheel zooms · F fits the view",
            LineEditorTool.Select =>
                "Drag across lines · Shift+click adds · Alt+click/drag removes · Delete removes selected lines · right-drag pans",
            _ =>
                "Click points or drag freehand · imported split vertices are draggable · Apply split or Enter applies · right-drag pans"
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

            var noun = CurrentLineLabel;

            UpdateStatus(
                matches.Count == 0
                    ? $"No {noun} crossed by that selection stroke."
                    : e.IsDeselect
                        ? $"Removed {matches.Count:N0} crossed {noun} from the selection."
                        : $"Added {matches.Count:N0} crossed {noun} to the selection.");

            return;
        }

        if (e.Tool != LineEditorTool.Split)
            return;

        _draftCutDrawing = true;
        AppendDraftPoints(e.WorldPoints);
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateLiveSplitPreview();

        UpdateStatus(
            $"Drawn split line has {_draftCutPoints.Count:N0} sampled point(s). " +
            "Continue drawing, or press Apply split / Enter when it is ready.");
    }

    private void OnSplitStrokePreviewChanged(
        object? sender,
        LineEditorStrokeEventArgs e)
    {
        if (e.Tool != LineEditorTool.Split ||
            e.WorldPoints.Count < 2)
        {
            return;
        }

        if (_activeStrokePreviewProcessedPoints <= 0)
            _activeStrokePreviewProcessedPoints = 1;

        var startIndex = Math.Max(
            1,
            _activeStrokePreviewProcessedPoints);

        for (var index = startIndex;
             index < e.WorldPoints.Count;
             index++)
        {
            var preview = _geometry.PreviewSplitWithStroke(
                new[]
                {
                    e.WorldPoints[index - 1],
                    e.WorldPoints[index]
                },
                GetSelectedTargetSet());

            MergePreview(
                preview,
                _activeStrokePreviewLines,
                _activeStrokePreviewPoints);
        }

        _activeStrokePreviewProcessedPoints =
            e.WorldPoints.Count;

        ShowCombinedSplitPreview();
    }

    private void OnLineClicked(
        object? sender,
        LineEditorClickEventArgs e)
    {
        if (e.Tool == LineEditorTool.Split)
        {
            if (!_draftCutDrawing)
                _draftCutDrawing = true;

            AddDraftCutPoint(e.WorldPoint);
            UpdateLiveSplitPreview();

            if (e.IsDoubleClick &&
                _draftCutPoints.Count >= 2)
            {
                _draftCutDrawing = false;
                _draftCutPreviewPoint = null;
                EditorSurface.SetDraftCut(
                    _draftCutPoints,
                    _draftCutPreviewPoint);

                UpdateStatus(
                    $"Split line ready with {_draftCutPoints.Count:N0} point(s). Press Apply split or Enter.");
                return;
            }

            UpdateStatus(
                _draftCutPoints.Count < 2
                    ? "Split line started. Add at least one more point."
                    : $"Split line has {_draftCutPoints.Count:N0} point(s). Press Apply split or Enter when it is ready.");

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
                "Removed one line from the selection.");
        }
        else if (e.IsAdditive)
        {
            _selected.Add(feature.LineStringElement);
            UpdateStatus(
                $"Added one line to the selection; {_selected.Count:N0} selected.");
        }
        else
        {
            _selected.Clear();
            _selected.Add(feature.LineStringElement);
            UpdateStatus(
                "Selected one line.");
        }

        EditorSurface.SetSelection(_selected);
        UpdateCutScopeDisplay();
        if (_draftCutPoints.Count > 0)
            RebuildCommittedSplitPreview();

        UpdatePendingGuidePreview();
    }

    private void OnPointerMoved(
        object? sender,
        LineEditorPointerEventArgs e)
    {
        if (EditorSurface.Tool == LineEditorTool.Split)
        {
            EditorSurface.SetHovered(null);

            if (_draftCutDrawing &&
                _draftCutPoints.Count > 0)
            {
                _draftCutPreviewPoint = e.WorldPoint;

                EditorSurface.SetDraftCut(
                    _draftCutPoints,
                    _draftCutPreviewPoint);

                UpdateLiveSplitPreview();
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

        UpdateLiveSplitPreview();
    }

    private void OnContextRequested(
        object? sender,
        LineEditorContextEventArgs e)
    {
        // Stop the rubber-band preview while the context menu is open. The
        // committed draft points remain visible and unchanged.
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

        var editingGuide = GetEditingGuide();

        if (editingGuide is not null)
        {
            AddContextMenuItem(
                menu,
                "Add Vertex Here",
                () => AddVertexToEditingGuide(
                    editingGuide,
                    e.WorldPoint));

            if (editingGuide.Guide.Coordinates.Count > 2)
            {
                AddContextMenuItem(
                    menu,
                    "Delete Nearest Vertex",
                    () => DeleteNearestVertexFromEditingGuide(
                        editingGuide,
                        e.WorldPoint));
            }

            AddContextMenuItem(
                menu,
                "Done Editing Imported Line",
                StopEditingGuide);

            AddContextMenuItem(
                menu,
                "Apply split",
                ApplyCurrentSplit);

            menu.IsOpen = true;
            return;
        }

        if (EditorSurface.Tool == LineEditorTool.Split)
        {
            if (_draftCutPoints.Count == 0)
            {
                AddContextMenuItem(
                    menu,
                    "Start Split Line Here",
                    () =>
                    {
                        _draftCutDrawing = true;
                        AddDraftCutPoint(e.WorldPoint);
                        UpdateLiveSplitPreview();
                    });
            }
            else
            {
                AddContextMenuItem(
                    menu,
                    "Add Split Point Here",
                    () =>
                    {
                        _draftCutDrawing = true;
                        AddDraftCutPoint(e.WorldPoint);
                        UpdateLiveSplitPreview();
                    });

                AddContextMenuItem(
                    menu,
                    "Finish Drawing Here",
                    () =>
                    {
                        AddDraftCutPoint(e.WorldPoint);
                        _draftCutDrawing = false;
                        _draftCutPreviewPoint = null;
                        EditorSurface.SetDraftCut(
                            _draftCutPoints,
                            _draftCutPreviewPoint);
                        UpdateLiveSplitPreview();
                        UpdateStatus(
                            "Split line ready. Press Apply split or Enter.");
                    });

                if (_draftCutPoints.Count >= 2)
                {
                    AddContextMenuItem(
                        menu,
                        "Apply split",
                        ApplyCurrentSplit);
                }

                AddContextMenuItem(
                    menu,
                    "Undo Last Point",
                    UndoLastDraftCutPoint);

                AddContextMenuItem(
                    menu,
                    "Cancel Split Line",
                    () => CancelDraftCutLine(
                        switchToSelect: false));
            }
        }
        else
        {
            AddContextMenuItem(
                menu,
                "Start Split Line Here",
                () =>
                {
                    CancelDraftCutLine(
                        switchToSelect: false);
                    SetTool(LineEditorTool.Split);
                    _draftCutDrawing = true;
                    AddDraftCutPoint(e.WorldPoint);
                    UpdateLiveSplitPreview();
                });
        }

        AddContextMenuItem(
            menu,
            "Select Tool",
            () =>
            {
                PauseDraftCut();
                SetTool(LineEditorTool.Select);
            });

        AddContextMenuItem(
            menu,
            "Hand Tool",
            () =>
            {
                PauseDraftCut();
                SetTool(LineEditorTool.Pan);
            });

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

    private void OnImportSplitLineClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Split Line",
            Filter =
                "Google Earth files (*.kml;*.kmz)|*.kml;*.kmz|" +
                "KML files (*.kml)|*.kml|" +
                "KMZ files (*.kmz)|*.kmz",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        // Step 2 intentionally has one pending split operation. Importing a
        // new split line replaces any unapplied drawn/imported split state.
        StopEditingGuide();
        _guides.RemoveAll(guide => !guide.Applied);
        CancelDraftCutLine(switchToSelect: false);

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
                $"The split line could not be imported.\n\n{exception.Message}",
                "Import Split Line",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        if (imported == 0)
        {
            UpdateStatus(
                "No LineString paths were found in the selected split-line file.");
            return;
        }

        RefreshSurface(fitView: true);

        var importedGuide = _guides
            .LastOrDefault(guide => !guide.Applied);

        if (importedGuide is not null)
            StartEditingGuide(importedGuide);

        UpdatePendingGuidePreview();

        UpdateStatus(
            $"Imported {imported:N0} split line(s). Drag orange vertex handles if you need to reshape it, then press Apply split or Enter.");
    }

    private void OnEditImportedLineClick(
        object sender,
        RoutedEventArgs e)
    {
        var current = GetEditingGuide();

        if (current is not null)
        {
            StopEditingGuide();
            UpdateStatus(
                "Imported split-line editing finished. You can re-enter Edit Imported Line at any time before applying it.");
            return;
        }

        var pending = _guides
            .LastOrDefault(guide => !guide.Applied);

        if (pending is null)
        {
            UpdateStatus(
                "There is no pending imported/existing split line to edit.");
            return;
        }

        StartEditingGuide(pending);
        UpdateStatus(
            "Editing imported split line. Drag orange handles to move vertices; right-click for add/delete vertex options.");
    }

    private void OnEditableGuideChanged(
        object? sender,
        LineEditorGuideEditEventArgs e)
    {
        var state = GetEditingGuide();

        if (state is null)
            return;

        UpdateGuideFromWorldPoints(
            state,
            e.WorldPoints);

        UpdatePendingGuidePreview();

        if (e.IsComplete)
        {
            RefreshSurface();
            UpdateStatus(
                $"Moved split-line vertex {e.VertexIndex + 1:N0}. Intersection preview updated.");
        }
    }

    private void OnUseSelectedAsSplitLineClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_selected.Count != 1)
        {
            UpdateStatus(
                "Select exactly one editable line, then choose Use Selected as Split Line.");
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
        // helper from also becoming the split target scope.
        _selected.Clear();
        EditorSurface.SetSelection(_selected);

        var createdGuide = _guides[^1];

        RefreshSurface();
        StartEditingGuide(createdGuide);
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();

        UpdateStatus(
            "Selected line is now the split line and is editable. Drag its orange handles if needed, then press Apply split or Enter. The helper line will be removed before splitting so it cannot split itself.");
    }

    private void OnApplyImportedSplitLineClick(
        object sender,
        RoutedEventArgs e)
    {
        ApplyCurrentSplit();
    }

    private void OnApplySplitClick(
        object sender,
        RoutedEventArgs e)
    {
        ApplyCurrentSplit();
    }

    private void OnOnlySplitSelectedChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (_draftCutPoints.Count > 0)
            RebuildCommittedSplitPreview();

        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();

        UpdateStatus(
            OnlySplitSelectedCheckBox.IsChecked == true
                ? "Split scope limited to the currently selected lines."
                : "Split scope is unrestricted; any crossed line can be split.");
    }

    private void ApplyCurrentSplit()
    {
        if (_draftCutPoints.Count >= 2)
        {
            FinishAndApplyDrawnSplit();
            return;
        }

        if (_guides.Any(guide => !guide.Applied))
        {
            ApplyPendingGuides();
            return;
        }

        UpdateStatus(
            "There is no split line ready. Import one or choose Draw split line first.");
    }

    private void ApplyPendingGuides()
    {
        StopEditingGuide();

        var pending = _guides
            .Where(guide => !guide.Applied)
            .ToList();

        if (pending.Count == 0)
        {
            UpdateStatus(
                _guides.Count == 0
                    ? "No imported or existing split lines are loaded."
                    : "All loaded split lines have already been used.");
            return;
        }

        ApplyGuideStates(
            pending,
            "Split using imported/existing line(s).");
    }

    private void FinishAndApplyDrawnSplit()
    {
        if (_draftCutPoints.Count < 2)
        {
            UpdateStatus(
                "The split line needs at least two points.");
            return;
        }

        _draftCutDrawing = false;
        _draftCutPreviewPoint = null;

        var guide = new KmlCutGuide(
            "Drawn split line",
            _draftCutPoints
                .Select(_geometry.Unproject)
                .ToList());

        var state = new CutGuideState(
            guide,
            removeMatchingSourceBeforeCut: false);

        _guides.Add(state);

        _draftCutPoints.Clear();
        ResetIncrementalSplitPreview();

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        ApplyGuideStates(
            new[] { state },
            "Drawn split applied.");
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

                        changed = true;
                    }
                }

                var worldPoints =
                    ProjectGuide(guideState.Guide);

                var result = _geometry.SplitWithStroke(
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
            // SplitWithStroke/DeleteLines already update the feature list and
            // spatial index incrementally. Clearing the temporary XML marker
            // does not change geometry, so a full Reload here is redundant.
            ClearCutTargetMarkers();
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
                "The split line did not split any lines in the current split target.");
            return;
        }

        UpdateStatus(
            $"{successPrefix} " +
            $"{totalLines:N0} line(s) split at " +
            $"{totalIntersections:N0} intersection(s), producing " +
            $"{totalPieces:N0} piece(s)." +
            (helperLinesRemoved > 0
                ? $" Removed {helperLinesRemoved:N0} helper line(s) before cutting."
                : string.Empty));
    }

    private void OnClearSplitLinesClick(
        object sender,
        RoutedEventArgs e)
    {
        StopEditingGuide();
        _guides.Clear();
        _cutPoints.Clear();
        CancelDraftCutLine(
            switchToSelect: false);

        RefreshSurface();
        UpdateCutScopeDisplay();

        UpdateStatus(
            "Split-line overlays cleared. Existing splits were not changed.");
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

        RefreshSurface();
        UpdateCutScopeDisplay();

        UpdateStatus(
            $"Deleted {deleted:N0} selected {CurrentLineLabel}.");
    }

    private void OnClearSelectionClick(
        object sender,
        RoutedEventArgs e)
    {
        _selected.Clear();
        EditorSurface.SetSelection(_selected);

        if (_draftCutPoints.Count > 0)
            RebuildCommittedSplitPreview();

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

        if (_draftCutPoints.Count > 0)
            RebuildCommittedSplitPreview();

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
                "There is no split or deletion to undo.");
            return;
        }

        var state = _undo.Pop();

        _workingDocument = state.Document;

        _geometry =
            new KmlLineGeometryService(
                _workingDocument,
                _projectionScale);

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
            "Last split/deletion undone. Imported split-line overlays remain available.");
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

        if (e.Key == Key.Space)
        {
            EditorSurface.Focus();
            e.Handled = true;
            return;
        }

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

        if (e.Key == Key.Enter)
        {
            ApplyCurrentSplit();
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
                     LineEditorTool.Split)
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

            case Key.S:
                OnDrawSplitLineClick(
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
        if (_draftCutPoints.Count >= 2)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "A drawn split line has not been applied yet. Apply this split before closing?",
                "Unapplied split line",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return;

            if (result == MessageBoxResult.Yes)
                FinishAndApplyDrawnSplit();
            else
                CancelDraftCutLine(switchToSelect: false);
        }

        if (_guides.Any(guide => !guide.Applied))
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "One or more imported/existing split lines have not been used yet. Split with them before closing?",
                "Unused split line",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return;

            if (result == MessageBoxResult.Yes)
                ApplyPendingGuides();
        }

        var outputDocument =
            CreateOutputDocument();

        if (XNode.DeepEquals(
                _originalDocument.Root,
                outputDocument.Root))
        {
            System.Windows.MessageBox.Show(
                this,
                "No line geometry has been changed. Selection by itself is temporary and is not saved in KML/KMZ. Select lines and click Split, or use Delete Selected, or click Cancel to close without changes.",
                "No line edits to apply",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

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

        if (_draftCutPoints.Count > 0)
        {
            var preview = _geometry.PreviewSplitWithStroke(
                new[]
                {
                    _draftCutPoints[^1],
                    point
                },
                GetSelectedTargetSet());

            MergePreview(
                preview,
                _committedPreviewLines,
                _committedPreviewPoints);
        }

        _draftCutPoints.Add(point);
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        var editingGuide = GetEditingGuide();

        EditorSurface.SetEditableGuide(
            editingGuide is null
                ? null
                : ProjectGuide(editingGuide.Guide));

        EditImportedLineButton.Content =
            editingGuide is null
                ? "Edit Imported Line"
                : "Done Editing Line";
    }

    private void AppendDraftPoints(
        IReadOnlyList<Point> points)
    {
        foreach (var point in points)
            AddDraftCutPoint(point);

        ResetActiveStrokePreview();
    }

    private void UndoLastDraftCutPoint()
    {
        if (_draftCutPoints.Count == 0)
        {
            UpdateStatus(
                "There are no split points to undo.");
            return;
        }

        _draftCutPoints.RemoveAt(
            _draftCutPoints.Count - 1);

        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateLiveSplitPreview();

        UpdateStatus(
            _draftCutPoints.Count == 0
                ? "Drawn split line cleared."
                : $"Removed the last split point. {_draftCutPoints.Count:N0} point(s) remain.");
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
        ResetIncrementalSplitPreview();
        EditorSurface.SetCutPoints(_cutPoints);

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        if (switchToSelect)
            SetTool(LineEditorTool.Select);
        else
            UpdateCutScopeDisplay();
    }

    private void UpdateLiveSplitPreview()
    {
        if (_draftCutPoints.Count == 0)
        {
            UpdatePendingGuidePreview();
            return;
        }

        LineSplitPreview? transient = null;

        if (_draftCutPreviewPoint.HasValue &&
            _draftCutPoints.Count > 0)
        {
            transient = _geometry.PreviewSplitWithStroke(
                new[]
                {
                    _draftCutPoints[^1],
                    _draftCutPreviewPoint.Value
                },
                GetSelectedTargetSet());
        }

        ShowCombinedSplitPreview(transient);
    }

    private void RebuildCommittedSplitPreview()
    {
        _committedPreviewLines.Clear();
        _committedPreviewPoints.Clear();

        for (var index = 1;
             index < _draftCutPoints.Count;
             index++)
        {
            var preview = _geometry.PreviewSplitWithStroke(
                new[]
                {
                    _draftCutPoints[index - 1],
                    _draftCutPoints[index]
                },
                GetSelectedTargetSet());

            MergePreview(
                preview,
                _committedPreviewLines,
                _committedPreviewPoints);
        }
    }

    private void ResetActiveStrokePreview()
    {
        _activeStrokePreviewLines.Clear();
        _activeStrokePreviewPoints.Clear();
        _activeStrokePreviewProcessedPoints = 0;
    }

    private void ResetIncrementalSplitPreview()
    {
        _committedPreviewLines.Clear();
        _committedPreviewPoints.Clear();
        ResetActiveStrokePreview();
    }

    private void ShowCombinedSplitPreview(
        LineSplitPreview? transient = null)
    {
        var lines = new HashSet<XElement>(
            _committedPreviewLines);

        lines.UnionWith(
            _activeStrokePreviewLines);

        var points = new List<Point>();

        foreach (var point in _committedPreviewPoints)
            AddUniquePreviewPoint(points, point);

        foreach (var point in _activeStrokePreviewPoints)
            AddUniquePreviewPoint(points, point);

        if (transient.HasValue)
        {
            lines.UnionWith(
                transient.Value.AffectedLines);

            foreach (var point in
                     transient.Value.CutPoints)
            {
                AddUniquePreviewPoint(
                    points,
                    point);
            }
        }

        _cutPoints.Clear();
        _cutPoints.AddRange(points);
        EditorSurface.SetCutPoints(_cutPoints);

        UpdateCutScopeDisplay(
            new LineSplitPreview(
                lines.Count,
                points.Count,
                points,
                lines.ToList()));
    }

    private static void MergePreview(
        LineSplitPreview preview,
        HashSet<XElement> lines,
        List<Point> points)
    {
        lines.UnionWith(preview.AffectedLines);

        foreach (var point in preview.CutPoints)
            AddUniquePreviewPoint(points, point);
    }

    private static void AddUniquePreviewPoint(
        List<Point> points,
        Point point)
    {
        const double tolerance = 0.0000000001d;

        if (points.Any(existing =>
                (existing - point).Length <= tolerance))
        {
            return;
        }

        points.Add(point);
    }

    private void UpdatePendingGuidePreview()
    {
        if (_draftCutPoints.Count > 0)
        {
            UpdateLiveSplitPreview();
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
                _geometry.PreviewSplitWithStroke(
                    ProjectGuide(
                        guideState.Guide),
                    targetSet);

            intersections +=
                preview.IntersectionCount;

            allPoints.AddRange(
                preview.CutPoints);

            foreach (var line in preview.AffectedLines)
                lines.Add(line);
        }

        _cutPoints.Clear();
        _cutPoints.AddRange(allPoints);

        EditorSurface.SetCutPoints(_cutPoints);

        var previewSummary =
            new LineSplitPreview(
                lines.Count,
                intersections,
                allPoints,
                lines.ToList());

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
        LineSplitPreview? preview = null)
    {
        var validSelected =
            GetValidSelectedLines();

        MapScopeText.Text =
            validSelected.Count > 0
                ? $"Split affects: {validSelected.Count:N0} selected line(s)"
                : $"Split affects: all {_geometry.Features.Count:N0} line(s) in the document";

        if (preview.HasValue)
        {
            ToolHintText.Text =
                $"{ToolHintForCurrentTool()} · Will split {preview.Value.LinesAffected:N0} line(s) at {preview.Value.IntersectionCount:N0} point(s)";
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
                "H Hand · right-drag / Space+drag / middle-drag pan · +/- zoom · F fit",
            LineEditorTool.Select =>
                "V Select · drag across lines · Alt+drag deselect · Delete remove · right-drag pan · Space+drag pan",
            _ =>
                "S Split · click points or drag freehand · right-click options · double-click/Enter finish · Backspace undo point · Esc cancel"
        };
    }

    private void UpdateScopeText()
    {
        var currentPlacemarkCount =
            _workingDocument
                .Descendants(
                    KmlNs + "Placemark")
                .Count();

        ScopeText.Text =
            $"All {_geometry.Features.Count:N0} line(s) in {currentPlacemarkCount:N0} placemark(s) are editable. Nothing is preselected when the Line Editor opens.";
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

        EditorSurface.SetDocument(
            _geometry.Features,
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
            _geometry.WorldBounds;

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

    private string CurrentLineLabel => "line(s)";

    private CutGuideState? GetEditingGuide()
    {
        if (!_editingGuideId.HasValue)
            return null;

        return _guides.FirstOrDefault(
            guide =>
                guide.Id == _editingGuideId.Value &&
                !guide.Applied);
    }

    private void StartEditingGuide(
        CutGuideState guide)
    {
        if (guide.Applied)
            return;

        _editingGuideId = guide.Id;
        SetTool(LineEditorTool.Split);

        EditorSurface.SetEditableGuide(
            ProjectGuide(guide.Guide));

        EditImportedLineButton.Content =
            "Done Editing Line";
    }

    private void StopEditingGuide()
    {
        _editingGuideId = null;
        EditorSurface.SetEditableGuide(null);
        EditImportedLineButton.Content =
            "Edit Imported Line";
    }

    private void UpdateGuideFromWorldPoints(
        CutGuideState state,
        IReadOnlyList<Point> worldPoints)
    {
        var previous = state.Guide.Coordinates;
        var coordinates = new List<KmlCoordinate>(
            worldPoints.Count);

        for (var index = 0;
             index < worldPoints.Count;
             index++)
        {
            var unprojected =
                _geometry.Unproject(worldPoints[index]);

            var altitude =
                index < previous.Count
                    ? previous[index].Altitude
                    : null;

            coordinates.Add(
                new KmlCoordinate(
                    unprojected.Longitude,
                    unprojected.Latitude,
                    altitude));
        }

        state.Guide = new KmlCutGuide(
            state.Guide.Name,
            coordinates);
    }

    private void AddVertexToEditingGuide(
        CutGuideState state,
        Point worldPoint)
    {
        var points = ProjectGuide(state.Guide).ToList();

        if (points.Count < 2)
            return;

        var bestSegment = 0;
        var bestDistance = double.MaxValue;

        for (var index = 0;
             index < points.Count - 1;
             index++)
        {
            var distance = DistanceToSegment(
                worldPoint,
                points[index],
                points[index + 1]);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSegment = index;
            }
        }

        points.Insert(
            bestSegment + 1,
            worldPoint);

        UpdateGuideFromWorldPoints(
            state,
            points);

        RefreshSurface();
        UpdatePendingGuidePreview();

        UpdateStatus(
            $"Added a split-line vertex. {points.Count:N0} vertices now.");
    }

    private void DeleteNearestVertexFromEditingGuide(
        CutGuideState state,
        Point worldPoint)
    {
        var points = ProjectGuide(state.Guide).ToList();

        if (points.Count <= 2)
            return;

        var nearestIndex = -1;
        var nearestDistance =
            EditorSurface.WorldToleranceFromPixels(14d);

        for (var index = 0;
             index < points.Count;
             index++)
        {
            var distance =
                (points[index] - worldPoint).Length;

            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        if (nearestIndex < 0)
        {
            UpdateStatus(
                "Right-click closer to a split-line vertex to delete it.");
            return;
        }

        points.RemoveAt(nearestIndex);

        UpdateGuideFromWorldPoints(
            state,
            points);

        RefreshSurface();
        UpdatePendingGuidePreview();

        UpdateStatus(
            $"Deleted one split-line vertex. {points.Count:N0} vertices remain.");
    }

    private static double DistanceToSegment(
        Point point,
        Point start,
        Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= double.Epsilon)
            return (point - start).Length;

        var t =
            ((point.X - start.X) * dx +
             (point.Y - start.Y) * dy) /
            lengthSquared;

        t = Math.Clamp(t, 0d, 1d);

        var projection = new Point(
            start.X + (t * dx),
            start.Y + (t * dy));

        return (point - projection).Length;
    }

    private void UpdateStatus(
        string message)
    {
        StatusText.Text =
            $"{message}  " +
            $"{_geometry.Features.Count:N0} editable {CurrentLineLabel}; " +
            $"{_selected.Count:N0} selected; " +
            $"{_guides.Count:N0} split line(s); " +
            $"{_undo.Count:N0} undo step(s).";
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

        public KmlCutGuide Guide { get; set; }

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
