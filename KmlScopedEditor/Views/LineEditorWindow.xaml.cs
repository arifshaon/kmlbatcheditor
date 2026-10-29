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
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.SolidColorBrush;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

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
    private readonly Stack<PendingGuideEditState> _pendingEditUndo = new();
    private PendingGuideEditState? _pendingGuideDragUndoState;
    private Point? _lastPointerWorldPoint;

    private bool _hasPerformedCut;
    private int _workflowStep = 1;
    private int _nextAppliedSequence;
    private bool _hideAppliedSplitHistory;
    private string? _step3InstructionOverride;
    private MediaColor _appliedHistoryColor =
        MediaColor.FromRgb(142, 132, 168);
    private double _appliedHistoryThickness = 2d;
    private bool _historyAppearanceInitializing;
    private bool _lassoControlsUpdating;
    private bool _lassoModeEnabled;
    private bool _lassoRemoveMode;

    public LineEditorWindow(
        XDocument sourceDocument,
        string? sourceName = null,
        LineEditorSessionState? sessionState = null)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(sourceDocument);

        _originalDocument = new XDocument(sourceDocument);
        _workingDocument = new XDocument(sourceDocument);

        _geometry = new KmlLineGeometryService(
            _workingDocument);

        _projectionScale = _geometry.LongitudeScale;

        RestoreSessionState(sessionState);
        InitializeAppliedHistoryAppearanceControls();

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
            UpdateStatus("Ready. Optional cleanup can remove whole lines before splitting, or you can go straight to Split.");
        };
    }

    public XDocument EditedDocument => CreateOutputDocument();

    public LineEditorSessionState SessionState =>
        CreateSessionState();

    private void RestoreSessionState(
        LineEditorSessionState? sessionState)
    {
        if (sessionState is null)
            return;

        _hideAppliedSplitHistory =
            sessionState.HideAppliedSplitHistory;

        if (!string.IsNullOrWhiteSpace(
                sessionState.AppliedHistoryColor))
        {
            try
            {
                _appliedHistoryColor =
                    (MediaColor)
                    System.Windows.Media.ColorConverter
                        .ConvertFromString(
                            sessionState.AppliedHistoryColor);
            }
            catch
            {
                _appliedHistoryColor =
                    MediaColor.FromRgb(
                        142,
                        132,
                        168);
            }
        }

        _appliedHistoryThickness =
            double.IsFinite(
                sessionState.AppliedHistoryThickness)
                ? Math.Clamp(
                    sessionState.AppliedHistoryThickness,
                    1d,
                    8d)
                : 2d;

        foreach (var item in sessionState.AppliedGuides)
        {
            var guide =
                new KmlCutGuide(
                    item.Name,
                    item.GuideCoordinates.ToList());

            var state =
                new CutGuideState(
                    guide,
                    removeMatchingSourceBeforeCut: false)
                {
                    Applied = true,
                    AppliedSequence =
                        item.AppliedSequence,
                    AppliedCutPoints =
                        item.CutPoints
                            .Select(_geometry.Project)
                            .ToList()
                };

            _guides.Add(state);
        }

        _nextAppliedSequence =
            _guides
                .Where(guide => guide.Applied)
                .Select(guide =>
                    guide.AppliedSequence)
                .DefaultIfEmpty(0)
                .Max();
    }

    private LineEditorSessionState
        CreateSessionState()
    {
        var appliedGuides =
            _guides
                .Where(guide => guide.Applied)
                .Select(guide =>
                    new LineEditorSessionGuideState(
                        guide.Guide.Name,
                        guide.Guide.Coordinates.ToList(),
                        guide.AppliedSequence,
                        guide.AppliedCutPoints
                            .Select(_geometry.Unproject)
                            .ToList()))
                .ToList();

        return new LineEditorSessionState(
            appliedGuides,
            _hideAppliedSplitHistory,
            _appliedHistoryColor.ToString(),
            _appliedHistoryThickness);
    }

    private void InitializeAppliedHistoryAppearanceControls()
    {
        _historyAppearanceInitializing = true;

        try
        {
            ShowPreviousSplitsCheckBox.IsChecked =
                !_hideAppliedSplitHistory;

            AppliedHistoryColorButton.Background =
                new MediaBrush(
                    _appliedHistoryColor);

            AppliedHistoryColorButton.Foreground =
                new MediaBrush(
                    IsDarkColor(
                        _appliedHistoryColor)
                        ? MediaColor.FromRgb(
                            255,
                            255,
                            255)
                        : MediaColor.FromRgb(
                            20,
                            20,
                            20));

            AppliedHistoryLegendLine.Fill =
                new MediaBrush(
                    _appliedHistoryColor);
            AppliedHistoryLegendLine.Height =
                Math.Clamp(
                    _appliedHistoryThickness,
                    1d,
                    6d);

            var targetThickness =
                Math.Clamp(
                    Math.Round(
                        _appliedHistoryThickness),
                    1d,
                    6d);

            var matchingItem =
                AppliedHistoryWidthComboBox
                    .Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item =>
                        double.TryParse(
                            item.Tag?.ToString(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var value) &&
                        Math.Abs(
                            value -
                            targetThickness) <
                        0.000001d);

            AppliedHistoryWidthComboBox.SelectedItem =
                matchingItem ??
                AppliedHistoryWidthComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault();

            EditorSurface.SetAppliedGuideStyle(
                _appliedHistoryColor,
                _appliedHistoryThickness);
        }
        finally
        {
            _historyAppearanceInitializing = false;
        }
    }

    private void OnShowPreviousSplitsChanged(
        object sender,
        RoutedEventArgs e)
    {
        // XAML raises Checked/Unchecked during InitializeComponent, before
        // the geometry service and the rest of the editor state exist.
        if (_historyAppearanceInitializing ||
            !IsLoaded)
        {
            return;
        }

        _hideAppliedSplitHistory =
            ShowPreviousSplitsCheckBox.IsChecked != true;

        RefreshSurface();

        UpdateStatus(
            _hideAppliedSplitHistory
                ? "Previous split lines hidden."
                : "Previous split lines shown.");
    }

    private void OnAppliedHistoryColorClick(
        object sender,
        RoutedEventArgs e)
    {
        using var dialog =
            new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color =
                    System.Drawing.Color.FromArgb(
                        _appliedHistoryColor.A,
                        _appliedHistoryColor.R,
                        _appliedHistoryColor.G,
                        _appliedHistoryColor.B)
            };

        if (dialog.ShowDialog() !=
            System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        _appliedHistoryColor =
            MediaColor.FromArgb(
                dialog.Color.A,
                dialog.Color.R,
                dialog.Color.G,
                dialog.Color.B);

        ApplyAppliedHistoryAppearance();
        UpdateStatus(
            "Applied split-history color updated.");
    }

    private void OnAppliedHistoryWidthChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_historyAppearanceInitializing ||
            !IsLoaded ||
            AppliedHistoryWidthComboBox
                .SelectedItem is not ComboBoxItem item ||
            !double.TryParse(
                item.Tag?.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var thickness))
        {
            return;
        }

        _appliedHistoryThickness =
            Math.Clamp(
                thickness,
                1d,
                8d);

        ApplyAppliedHistoryAppearance();
        UpdateStatus(
            $"Applied split-history width set to {_appliedHistoryThickness:0.#} px.");
    }

    private void ApplyAppliedHistoryAppearance()
    {
        AppliedHistoryColorButton.Background =
            new MediaBrush(
                _appliedHistoryColor);

        AppliedHistoryColorButton.Foreground =
            new MediaBrush(
                IsDarkColor(
                    _appliedHistoryColor)
                    ? MediaColor.FromRgb(
                        255,
                        255,
                        255)
                    : MediaColor.FromRgb(
                        20,
                        20,
                        20));

        AppliedHistoryLegendLine.Fill =
            new MediaBrush(
                _appliedHistoryColor);
        AppliedHistoryLegendLine.Height =
            Math.Clamp(
                _appliedHistoryThickness,
                1d,
                6d);

        EditorSurface.SetAppliedGuideStyle(
            _appliedHistoryColor,
            _appliedHistoryThickness);
    }

    private static bool IsDarkColor(
        MediaColor color)
    {
        var luminance =
            (0.2126d * color.R) +
            (0.7152d * color.G) +
            (0.0722d * color.B);

        return luminance < 145d;
    }

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
        _step3InstructionOverride = null;
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
        StopEditingGuide();
        CancelDraftCutLine(
            switchToSelect: false);
        RefreshSurface();
        BeginSplitMode();

        UpdateStatus(
            _guides.Any(guide => !guide.Applied)
                ? "Draw another split line. Existing imported or drawn split lines stay pending. Press Enter or double-click to finish this drawing; nothing is applied until you choose Apply split."
                : "Draw the split line by clicking points or dragging. Press Enter or double-click to finish the drawing; nothing is applied until you choose Apply split.");
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

    private void FinishDraftCutDrawing()
    {
        var state =
            CommitDraftCutAsPendingGuide();

        if (state is null)
        {
            UpdateStatus(
                "The split line needs at least two points before it can be finished.");
            return;
        }

        StartEditingGuide(state);
        RefreshSurface();
        UpdatePendingGuidePreview();

        UpdateStatus(
            "Split line finished and added to the pending split lines. Drag its orange handles to reshape it, draw another line, or choose Apply split when all split lines are ready.");
    }

    private CutGuideState?
        CommitDraftCutAsPendingGuide()
    {
        if (_draftCutPoints.Count < 2)
            return null;

        var guide =
            new KmlCutGuide(
                "Split line",
                _draftCutPoints
                    .Select(_geometry.Unproject)
                    .ToList());

        var state =
            new CutGuideState(
                guide,
                removeMatchingSourceBeforeCut: false);

        PushPendingGuideEditUndo();
        _guides.Add(state);

        _draftCutPoints.Clear();
        _draftCutPreviewPoint = null;
        _draftCutDrawing = false;
        ResetIncrementalSplitPreview();

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        return state;
    }

    private PendingGuideEditState
        CapturePendingGuideEditState()
    {
        return new PendingGuideEditState(
            _guides
                .Where(guide => !guide.Applied)
                .Select(guide =>
                    new PendingGuideSnapshot(
                        guide.Guide.Name,
                        guide.Guide.Coordinates.ToList(),
                        guide.RemoveMatchingSourceBeforeCut))
                .ToList());
    }

    private void PushPendingGuideEditUndo()
    {
        _pendingEditUndo.Push(
            CapturePendingGuideEditState());
    }

    private void PushPendingGuideEditUndo(
        PendingGuideEditState state)
    {
        _pendingEditUndo.Push(state);
    }

    private void RestorePendingGuideEditState(
        PendingGuideEditState state)
    {
        StopEditingGuide();

        _guides.RemoveAll(
            guide => !guide.Applied);

        foreach (var snapshot in state.Guides)
        {
            _guides.Add(
                new CutGuideState(
                    new KmlCutGuide(
                        snapshot.Name,
                        snapshot.Coordinates.ToList()),
                    snapshot.RemoveMatchingSourceBeforeCut));
        }

        CancelDraftCutLine(
            switchToSelect: false);
        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();
    }

    private void RemovePendingSplitSet()
    {
        if (!_guides.Any(
                guide => !guide.Applied))
        {
            UpdateStatus(
                "There is no pending split line to remove.");
            return;
        }

        PushPendingGuideEditUndo();
        StopEditingGuide();
        _guides.RemoveAll(
            guide => !guide.Applied);
        _cutPoints.Clear();
        ResetIncrementalSplitPreview();
        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();
        UpdateStatus(
            "Pending split line(s) removed. Ctrl+Z restores them.");
    }

    private void RemovePendingGuide(
        CutGuideState guide)
    {
        if (guide.Applied ||
            !_guides.Contains(guide))
        {
            return;
        }

        PushPendingGuideEditUndo();
        StopEditingGuide();
        _guides.Remove(guide);
        _cutPoints.Clear();
        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();
        UpdateStatus(
            "Split line removed. Ctrl+Z restores it.");
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
                "Optional cleanup before splitting. Select whole lines you do not want, then Delete selected.",
            2 =>
                "Nothing needs to be selected. Import a split line or draw one; orange dots show where lines will be split.",
            _ =>
                _step3InstructionOverride ??
                "Cleanup after splitting. Select the new split pieces you do not want, then Delete selected."
        };

        ToolHintText.Text = tool switch
        {
            LineEditorTool.Pan =>
                "Right-drag / Space+drag / middle-drag pans · mouse wheel zooms · F fits the view",
            LineEditorTool.Select =>
                "Drag across lines · Shift+click adds · Alt+click/drag removes · Delete removes selected lines · right-drag pans",
            _ =>
                "Click points or drag freehand · click any pending split line to edit · Enter finishes drawing · only Apply split performs the split · right-drag pans"
        };

        UpdateCutScopeDisplay();
    }

    private void OnStrokeCompleted(
        object? sender,
        LineEditorStrokeEventArgs e)
    {
        if (e.Tool == LineEditorTool.Select)
        {
            var matches =
                e.IsLasso
                    ? _geometry.FindFeaturesInPolygon(
                        e.WorldPoints)
                    : _geometry.FindIntersectingFeatures(
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
                    ? e.IsLasso
                        ? $"No {noun} found inside or crossing that lasso."
                        : $"No {noun} crossed by that selection stroke."
                    : e.IsDeselect
                        ? e.IsLasso
                            ? $"Removed {matches.Count:N0} {noun} with the lasso."
                            : $"Removed {matches.Count:N0} crossed {noun} from the selection."
                        : e.IsLasso
                            ? $"Added {matches.Count:N0} {noun} with the lasso."
                            : $"Added {matches.Count:N0} crossed {noun} to the selection.");

            return;
        }

        if (e.Tool != LineEditorTool.Split)
            return;

        // A drag away from an editable pending line starts another ordinary
        // split line; it does not discard the line that was being edited.
        if (GetEditingGuide() is not null &&
            _draftCutPoints.Count == 0)
        {
            StopEditingGuide();
        }

        _draftCutDrawing = true;
        AppendDraftPoints(e.WorldPoints);
        _draftCutPreviewPoint = null;

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        UpdateLiveSplitPreview();

        UpdateStatus(
            $"Drawn split line has {_draftCutPoints.Count:N0} sampled point(s). " +
            "Continue drawing, or press Enter to finish. Choose Apply split when you are ready to perform the split.");
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
            if (_draftCutPoints.Count == 0)
            {
                var pendingGuide =
                    FindPendingGuideNearPoint(
                        e.WorldPoint,
                        EditorSurface
                            .WorldToleranceFromPixels(8d));

                if (pendingGuide is not null)
                {
                    _draftCutDrawing = false;
                    StartEditingGuide(
                        pendingGuide);
                    RefreshSurface();
                    UpdateStatus(
                        "Split line selected for editing. Drag the orange handles to reshape it.");
                    return;
                }

                if (GetEditingGuide() is not null)
                {
                    StopEditingGuide();
                    RefreshSurface();
                    UpdateStatus(
                        "Finished editing the split line. Click or drag again on empty space to draw another pending split line.");
                    return;
                }

                _draftCutDrawing = true;
            }
            else if (!_draftCutDrawing)
            {
                _draftCutDrawing = true;
            }

            AddDraftCutPoint(e.WorldPoint);
            UpdateLiveSplitPreview();

            if (e.IsDoubleClick &&
                _draftCutPoints.Count >= 2)
            {
                FinishDraftCutDrawing();
                return;
            }

            UpdateStatus(
                _draftCutPoints.Count < 2
                    ? "Split line started. Add at least one more point."
                    : $"Split line has {_draftCutPoints.Count:N0} point(s). Press Enter or double-click to finish this drawing. It will remain pending until you choose Apply split.");

            return;
        }

        if (e.Tool != LineEditorTool.Select)
            return;

        // The first click of a double-click already changed the selection.
        // Ignore the second click so it does not immediately toggle it back.
        if (e.IsDoubleClick)
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

        var element =
            feature.LineStringElement;

        if (e.IsDeselect)
        {
            _selected.Remove(element);
            UpdateStatus(
                $"Removed 1 {CurrentLineLabel.TrimEnd('(', 's', ')')} ({_selected.Count:N0} selected).");
        }
        else if (e.IsAdditive)
        {
            _selected.Add(element);
            UpdateStatus(
                $"Added 1 {CurrentLineLabel.TrimEnd('(', 's', ')')} ({_selected.Count:N0} selected).");
        }
        else if (_workflowStep == 2)
        {
            _selected.Clear();
            _selected.Add(element);
            UpdateStatus(
                "Selected one line for the Step 2 scope.");
        }
        else if (_selected.Contains(element))
        {
            _selected.Remove(element);
            UpdateStatus(
                $"Removed 1 {(_workflowStep == 3 ? "piece" : "line")} ({_selected.Count:N0} selected).");
        }
        else
        {
            _selected.Add(element);
            UpdateStatus(
                $"Added 1 {(_workflowStep == 3 ? "piece" : "line")} ({_selected.Count:N0} selected).");
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
        _lastPointerWorldPoint =
            e.WorldPoint;

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
        _lastPointerWorldPoint = null;

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
        var clickedPendingGuide =
            FindPendingGuideNearPoint(
                e.WorldPoint,
                EditorSurface.WorldToleranceFromPixels(8d));

        if (clickedPendingGuide is not null &&
            !ReferenceEquals(
                clickedPendingGuide,
                editingGuide))
        {
            AddContextMenuItem(
                menu,
                "Edit this split line",
                () =>
                {
                    StartEditingGuide(
                        clickedPendingGuide);
                    RefreshSurface();
                    UpdateStatus(
                        "Split line selected for editing.");
                });

            AddContextMenuItem(
                menu,
                "Remove this split line",
                () => RemovePendingGuide(
                    clickedPendingGuide));

            AddContextMenuItem(
                menu,
                "Apply split",
                ApplyCurrentSplit);

            menu.IsOpen = true;
            return;
        }

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
                "Finish editing this split line",
                StopEditingGuide);

            AddContextMenuItem(
                menu,
                "Remove this split line",
                () => RemovePendingGuide(
                    editingGuide));

            AddContextMenuItem(
                menu,
                "Start new split line here",
                () =>
                {
                    StopEditingGuide();
                    CancelDraftCutLine(
                        switchToSelect: false);
                    _draftCutDrawing = true;
                    AddDraftCutPoint(
                        e.WorldPoint);
                    UpdateLiveSplitPreview();
                });

            AddContextMenuItem(
                menu,
                "Apply split",
                ApplyCurrentSplit);

            menu.IsOpen = true;
            return;
        }

        if (clickedPendingGuide is not null)
        {
            AddContextMenuItem(
                menu,
                "Edit this split line",
                () =>
                {
                    StartEditingGuide(
                        clickedPendingGuide);
                    RefreshSurface();
                    UpdateStatus(
                        "Split line selected for editing.");
                });

            AddContextMenuItem(
                menu,
                "Remove this split line",
                () => RemovePendingGuide(
                    clickedPendingGuide));

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
                        AddDraftCutPoint(
                            e.WorldPoint);
                        FinishDraftCutDrawing();
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
                    SetWorkflowStep(2);
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

        // Imported and drawn paths share the same pending collection.
        // Importing adds paths without removing existing pending lines.
        var importedStates =
            new List<CutGuideState>();

        try
        {
            foreach (var fileName in dialog.FileNames)
            {
                var guides =
                    _cutGuideLoader.Load(fileName);

                foreach (var guide in guides)
                {
                    importedStates.Add(
                        new CutGuideState(
                            guide,
                            removeMatchingSourceBeforeCut: false));
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

        if (importedStates.Count == 0)
        {
            UpdateStatus(
                "No LineString paths were found in the selected split-line file.");
            return;
        }

        CancelDraftCutLine(
            switchToSelect: false);
        PushPendingGuideEditUndo();
        _guides.AddRange(
            importedStates);

        RefreshSurface(fitView: true);

        var lastImportedGuide =
            importedStates[^1];

        StartEditingGuide(
            lastImportedGuide);

        UpdatePendingGuidePreview();

        UpdateStatus(
            $"Imported {importedStates.Count:N0} split path(s). They remain pending alongside any existing split lines. Drag orange handles to reshape a path, draw another line, or choose Apply split when ready.");
    }

    private void OnEditableGuideChanged(
        object? sender,
        LineEditorGuideEditEventArgs e)
    {
        var state = GetEditingGuide();

        if (state is null)
            return;

        if (!e.IsComplete &&
            _pendingGuideDragUndoState is null)
        {
            _pendingGuideDragUndoState =
                CapturePendingGuideEditState();
        }

        if (e.IsComplete &&
            _pendingGuideDragUndoState is null)
        {
            _pendingGuideDragUndoState =
                CapturePendingGuideEditState();
        }

        UpdateGuideFromWorldPoints(
            state,
            e.WorldPoints);

        UpdatePendingGuidePreview();

        if (e.IsComplete)
        {
            if (_pendingGuideDragUndoState is not null)
            {
                PushPendingGuideEditUndo(
                    _pendingGuideDragUndoState);
                _pendingGuideDragUndoState = null;
            }

            RefreshSurface();
            UpdateStatus(
                $"Moved split-line vertex {e.VertexIndex + 1:N0}. Ctrl+Z restores the previous position.");
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

        var createdGuide =
            new CutGuideState(
                guide,
                removeMatchingSourceBeforeCut: true);

        CancelDraftCutLine(
            switchToSelect: false);
        PushPendingGuideEditUndo();
        _guides.Add(
            createdGuide);

        // This selection identified the helper line. Clearing it prevents the
        // helper from also becoming the split target scope.
        _selected.Clear();
        EditorSurface.SetSelection(_selected);

        RefreshSurface();
        StartEditingGuide(createdGuide);
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();

        UpdateStatus(
            "Selected line was added as another pending split line and is editable. Drag its orange handles if needed. Nothing is split until you choose Apply split.");
    }

    private void OnApplySplitClick(
        object sender,
        RoutedEventArgs e)
    {
        ApplyCurrentSplit();
    }

    private void OnLassoModeChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (_lassoControlsUpdating ||
            !IsLoaded)
            return;

        var enabled =
            sender is
                System.Windows.Controls.Primitives.ToggleButton button &&
            button.IsChecked == true;

        _lassoModeEnabled = enabled;

        _lassoControlsUpdating = true;

        try
        {
            Step1LassoButton.IsChecked =
                enabled;
            Step3LassoButton.IsChecked =
                enabled;
        }
        finally
        {
            _lassoControlsUpdating = false;
        }

        EditorSurface.LassoMode =
            enabled;
        EditorSurface.LassoRemove =
            _lassoRemoveMode;

        UpdateStatus(
            enabled
                ? $"Lasso mode on ({(_lassoRemoveMode ? "Remove" : "Add")}). Drag a closed outline around lines."
                : "Lasso mode off. Ctrl+drag still performs a one-off additive lasso.");
    }

    private void OnLassoActionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_lassoControlsUpdating ||
            !IsLoaded)
            return;

        var combo =
            sender as
                System.Windows.Controls.ComboBox;

        if (combo?.SelectedItem is not
            ComboBoxItem selected)
        {
            return;
        }

        _lassoRemoveMode =
            string.Equals(
                selected.Tag?.ToString(),
                "Remove",
                StringComparison.OrdinalIgnoreCase);

        _lassoControlsUpdating = true;

        try
        {
            Step1LassoActionComboBox.SelectedIndex =
                _lassoRemoveMode ? 1 : 0;
            Step3LassoActionComboBox.SelectedIndex =
                _lassoRemoveMode ? 1 : 0;
        }
        finally
        {
            _lassoControlsUpdating = false;
        }

        EditorSurface.LassoRemove =
            _lassoRemoveMode;

        if (_lassoModeEnabled)
        {
            UpdateStatus(
                $"Lasso action set to {(_lassoRemoveMode ? "Remove" : "Add")}.");
        }
    }

    private void OnOnlySplitSelectedChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

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
        if (_draftCutPoints.Count == 1)
        {
            UpdateStatus(
                "The current drawing needs at least one more point before it can be applied. Add another point or press Esc to cancel it.");
            return;
        }

        if (_draftCutPoints.Count >= 2)
            CommitDraftCutAsPendingGuide();

        if (_guides.Any(
                guide => !guide.Applied))
        {
            ApplyPendingGuides();
            return;
        }

        UpdateStatus(
            "There is no pending split line. Import one or draw one first.");
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
                    ? "No split lines are loaded."
                    : "All split lines have already been applied.");
            return;
        }

        ApplyGuideStates(
            pending,
            "Split applied.");
    }

    private void ApplyGuideStates(
        IReadOnlyList<CutGuideState> guideStates,
        string successPrefix)
    {
        if (guideStates.Count == 0)
            return;

        var selectedTargets = GetValidSelectedLines();
        var limitToSelection =
            OnlySplitSelectedCheckBox.IsChecked == true;

        if (limitToSelection &&
            selectedTargets.Count == 0)
        {
            UpdateStatus(
                "Only split the selected lines is ticked, but no lines are selected.");
            return;
        }

        var snapshot =
            CaptureUndoState(
                UndoActionKind.Split);
        var useSelectedTargets =
            limitToSelection &&
            selectedTargets.Count > 0;

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
        var changed = false;
        var applySequence =
            _nextAppliedSequence + 1;
        var usedAnyGuide = false;

        try
        {
            foreach (var guideState in guideStates)
            {
                var helperRemovedForGuide = 0;

                if (guideState.RemoveMatchingSourceBeforeCut)
                {
                    var helper = FindMatchingFeature(
                        guideState.Guide.Coordinates);

                    if (helper is not null)
                    {
                        helperRemovedForGuide =
                            _geometry.DeleteLines(
                                new[]
                                {
                                    helper.LineStringElement
                                });

                        helperLinesRemoved +=
                            helperRemovedForGuide;

                        if (helperRemovedForGuide > 0)
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

                var guideWasUsed =
                    result.LinesSplit > 0 ||
                    helperRemovedForGuide > 0;

                if (result.LinesSplit > 0)
                    changed = true;

                if (guideWasUsed)
                {
                    guideState.Applied = true;
                    guideState.AppliedSequence =
                        applySequence;
                    guideState.AppliedCutPoints =
                        result.CutPoints.ToList();
                    usedAnyGuide = true;
                }
                else
                {
                    guideState.Applied = false;
                    guideState.AppliedSequence = 0;
                    guideState.AppliedCutPoints.Clear();
                }
            }

            if (usedAnyGuide)
                _nextAppliedSequence = applySequence;
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

        if (totalLines > 0)
            _hasPerformedCut = true;

        if (changed)
        {
            _undo.Push(snapshot);
            _pendingEditUndo.Clear();
            _pendingGuideDragUndoState = null;
        }

        RefreshSurface();
        UpdateCutScopeDisplay();

        if (!changed)
        {
            UpdateStatus(
                "The split line did not split any lines in the current split target. It remains ready so you can adjust it or try again.");
            return;
        }

        UpdateStatus(
            $"{successPrefix} " +
            $"{totalLines:N0} line(s) split at " +
            $"{totalIntersections:N0} intersection(s), producing " +
            $"{totalPieces:N0} piece(s)." +
            (helperLinesRemoved > 0
                ? $" Removed {helperLinesRemoved:N0} helper line(s) before splitting."
                : string.Empty));

        var remainingPending =
            _guides.Count(
                guide => !guide.Applied);

        if (remainingPending > 0)
        {
            SetWorkflowStep(2);
            RefreshSurface();
            UpdatePendingGuidePreview();
            UpdateStatus(
                $"Split applied to the lines that crossed geometry. {remainingPending:N0} pending split line(s) did not split anything and remain editable.");
            return;
        }

        _step3InstructionOverride =
            $"Split applied: {totalLines:N0} line(s) split at {totalIntersections:N0} point(s). Select the pieces you want to remove.";
        SetWorkflowStep(3);
        RefreshSurface();
    }

    private void OnRemovePendingSplitLineClick(
        object sender,
        RoutedEventArgs e)
    {
        CancelDraftCutLine(
            switchToSelect: false);
        RemovePendingSplitSet();
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

        var snapshot =
            CaptureUndoState(
                UndoActionKind.Deletion);

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
        if (_workflowStep == 2 &&
            _draftCutPoints.Count > 0)
        {
            UndoLastDraftCutPoint();
            return;
        }

        if (_pendingEditUndo.Count > 0)
        {
            var editState =
                _pendingEditUndo.Pop();

            RestorePendingGuideEditState(
                editState);

            UpdateStatus(
                "Split-line edit undone.");
            return;
        }

        if (_undo.Count == 0)
        {
            UpdateStatus(
                "There is no split-line edit, split, or deletion to undo.");
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
            if (state.GuideStates.TryGetValue(
                    guide.Id,
                    out var guideState))
            {
                guide.Applied =
                    guideState.Applied;
                guide.AppliedSequence =
                    guideState.AppliedSequence;
                guide.AppliedCutPoints =
                    guideState.AppliedCutPoints.ToList();
            }
            else
            {
                guide.Applied = false;
                guide.AppliedSequence = 0;
                guide.AppliedCutPoints =
                    new List<Point>();
            }
        }

        _cutPoints.Clear();

        if (state.Kind == UndoActionKind.Deletion)
        {
            _cutPoints.AddRange(
                state.CutPoints);
        }

        _selected.Clear();

        if (state.Kind == UndoActionKind.Split)
        {
            _step3InstructionOverride = null;
            SetWorkflowStep(2);
        }

        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();

        var restoredPendingSplit =
            state.Kind == UndoActionKind.Split &&
            _guides.Any(guide => !guide.Applied);

        UpdateStatus(
            state.Kind == UndoActionKind.Split
                ? restoredPendingSplit
                    ? "Split undone — the split line is ready to apply again."
                    : "Split undone."
                : "Deletion undone.");
    }

    private void HandleStep2Delete()
    {
        if (_draftCutPoints.Count > 0)
        {
            UpdateStatus(
                "A split line is currently being drawn. Use Backspace to remove its last point or Esc to cancel the drawing.");
            return;
        }

        var editingGuide =
            GetEditingGuide();

        if (editingGuide is not null)
        {
            if (_lastPointerWorldPoint.HasValue)
            {
                var vertexIndex =
                    FindNearestGuideVertexIndex(
                        editingGuide,
                        _lastPointerWorldPoint.Value,
                        EditorSurface.WorldToleranceFromPixels(14d));

                if (vertexIndex >= 0 &&
                    editingGuide.Guide.Coordinates.Count > 2)
                {
                    PushPendingGuideEditUndo();

                    var points =
                        ProjectGuide(
                            editingGuide.Guide)
                            .ToList();

                    points.RemoveAt(
                        vertexIndex);

                    UpdateGuideFromWorldPoints(
                        editingGuide,
                        points);

                    RefreshSurface();
                    UpdatePendingGuidePreview();
                    UpdateStatus(
                        "Deleted one split-line point. Ctrl+Z restores it.");
                    return;
                }
            }

            RemovePendingGuide(
                editingGuide);
            return;
        }

        if (!_guides.Any(
                guide => !guide.Applied))
        {
            UpdateStatus(
                "There is no pending split line to remove.");
            return;
        }

        var result =
            System.Windows.MessageBox.Show(
                this,
                "Remove the pending split line?",
                "Remove split line",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (result ==
            MessageBoxResult.Yes)
        {
            RemovePendingSplitSet();
        }
    }

    private int FindNearestGuideVertexIndex(
        CutGuideState state,
        Point worldPoint,
        double tolerance)
    {
        var points =
            ProjectGuide(
                state.Guide);

        var nearestIndex = -1;
        var nearestDistance = tolerance;

        for (var index = 0;
             index < points.Count;
             index++)
        {
            var distance =
                (points[index] - worldPoint)
                    .Length;

            if (distance <=
                nearestDistance)
            {
                nearestDistance =
                    distance;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private void OnHideMapInstructionsClick(
        object sender,
        RoutedEventArgs e)
    {
        MapInstructionPanel.Visibility =
            Visibility.Collapsed;
        MapInstructionShowButton.Visibility =
            Visibility.Visible;
        EditorSurface.Focus();
    }

    private void OnShowMapInstructionsClick(
        object sender,
        RoutedEventArgs e)
    {
        MapInstructionPanel.Visibility =
            Visibility.Visible;
        MapInstructionShowButton.Visibility =
            Visibility.Collapsed;
        EditorSurface.Focus();
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
            if (_workflowStep == 2)
            {
                HandleStep2Delete();
            }
            else
            {
                OnDeleteSelectedClick(
                    this,
                    new RoutedEventArgs());
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            if (_workflowStep == 2 &&
                _draftCutPoints.Count > 0)
            {
                UndoLastDraftCutPoint();
            }
            else if (_workflowStep == 2)
            {
                HandleStep2Delete();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (_draftCutPoints.Count > 0)
            {
                FinishDraftCutDrawing();
            }
            else
            {
                UpdateStatus(
                    "Split line(s) are ready. Choose Apply split when you want to perform the split.");
            }

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
                UpdateStatus(
                    "Split-line drawing cancelled. Existing pending split lines were kept.");
            }
            else if (GetEditingGuide() is not null)
            {
                StopEditingGuide();
                RefreshSurface();
                UpdateStatus(
                    "Finished editing the split line.");
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
            CommitDraftCutAsPendingGuide();
        }
        else if (_draftCutPoints.Count > 0)
        {
            CancelDraftCutLine(
                switchToSelect: false);
        }

        if (_guides.Any(
                guide => !guide.Applied))
        {
            var result =
                System.Windows.MessageBox.Show(
                    this,
                    "One or more split lines are still pending and have not been applied. Close without applying them?",
                    "Pending split lines",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result !=
                MessageBoxResult.Yes)
            {
                UpdateStatus(
                    "Pending split lines were not applied. Choose Apply split when you are ready.");
                return;
            }

            StopEditingGuide();
            _guides.RemoveAll(
                guide => !guide.Applied);
            _cutPoints.Clear();
            RefreshSurface();
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

        _pendingEditUndo.Clear();
        _pendingGuideDragUndoState = null;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(
        object sender,
        RoutedEventArgs e)
    {
        _pendingEditUndo.Clear();
        _pendingGuideDragUndoState = null;
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
                ? "Drawn split line cleared. Existing pending split lines were kept."
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
        var lines =
            new HashSet<XElement>();

        var points =
            new List<Point>();

        MergePendingGuidePreviews(
            lines,
            points);

        lines.UnionWith(
            _committedPreviewLines);

        lines.UnionWith(
            _activeStrokePreviewLines);

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
        UpdateLiveSplitPreview();
    }

    private void MergePendingGuidePreviews(
        HashSet<XElement> lines,
        List<Point> points)
    {
        var pending =
            _guides
                .Where(guide =>
                    !guide.Applied)
                .ToList();

        if (pending.Count == 0)
            return;

        var selectedTargets =
            GetSelectedTargetSet();

        foreach (var guideState in pending)
        {
            var targetSet =
                selectedTargets is null
                    ? _geometry.Features
                        .Select(feature =>
                            feature.LineStringElement)
                        .ToHashSet()
                    : new HashSet<XElement>(
                        selectedTargets);

            if (guideState.RemoveMatchingSourceBeforeCut)
            {
                var helper =
                    FindMatchingFeature(
                        guideState.Guide.Coordinates);

                if (helper is not null)
                {
                    targetSet.Remove(
                        helper.LineStringElement);
                }
            }

            var preview =
                _geometry.PreviewSplitWithStroke(
                    ProjectGuide(
                        guideState.Guide),
                    targetSet);

            MergePreview(
                preview,
                lines,
                points);
        }
    }

    private IReadOnlySet<XElement>?
        GetSelectedTargetSet()
    {
        if (OnlySplitSelectedCheckBox.IsChecked != true)
            return null;

        var valid = GetValidSelectedLines();

        return valid.Count == 0
            ? new HashSet<XElement>()
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

        UseSelectedLineButton.IsEnabled =
            validSelected.Count == 1;

        OnlySplitSelectedCheckBox.IsEnabled =
            validSelected.Count > 0;

        if (validSelected.Count == 0 &&
            OnlySplitSelectedCheckBox.IsChecked == true)
        {
            OnlySplitSelectedCheckBox.IsChecked = false;
        }

        SelectedSplitScopeText.Text =
            validSelected.Count == 0
                ? "(none selected)"
                : OnlySplitSelectedCheckBox.IsChecked == true
                    ? $"{validSelected.Count:N0} selected — split limited to selection"
                    : $"{validSelected.Count:N0} lines selected — the split will still cross all lines. Tick to limit it.";

        EditorSurface.SetSelectionSoft(
            _workflowStep == 2 &&
            validSelected.Count > 0 &&
            OnlySplitSelectedCheckBox.IsChecked != true);

        var pendingGuides =
            _guides.Count(guide => !guide.Applied);

        SplitLineSummaryText.Text =
            _draftCutPoints.Count >= 2
                ? $"new line · {_draftCutPoints.Count:N0} points"
                : pendingGuides > 0
                    ? pendingGuides == 1
                        ? "split line ready"
                        : $"split line ready · {pendingGuides:N0} paths"
                    : "No split line yet";

        RemovePendingSplitLineButton.IsEnabled =
            pendingGuides > 0;

        ApplySplitButton.IsEnabled =
            _draftCutPoints.Count >= 2 ||
            pendingGuides > 0;

        var deleteCount =
            validSelected.Count;

        Step1DeleteButton.Content =
            $"Delete selected lines ({deleteCount:N0})";
        Step1DeleteButton.IsEnabled =
            deleteCount > 0;

        Step3DeleteButton.Content =
            $"Delete selected pieces ({deleteCount:N0})";
        Step3DeleteButton.IsEnabled =
            deleteCount > 0;

        if (preview.HasValue)
        {
            SplitPreviewSummaryText.Text =
                $"will split {preview.Value.LinesAffected:N0} line(s) at {preview.Value.IntersectionCount:N0} point(s)";
        }
        else
        {
            SplitPreviewSummaryText.Text =
                "draw or import a split line";
        }

        ToolHintText.Text =
            ToolHintForCurrentTool();

        UpdateScopeText();
    }

    private string ToolHintForCurrentTool()
    {
        return EditorSurface.Tool switch
        {
            LineEditorTool.Pan =>
                "H Hand · right-drag / Space+drag / middle-drag pan · +/- zoom · F fit",
            LineEditorTool.Select =>
                "V Select · click toggles · drag crosses lines · Ctrl+drag lasso adds · Lasso controls can add/remove · Delete removes selected · right-drag pan",
            _ =>
                "Split · import or draw lines · click a pending line to edit · Enter finishes drawing · only Apply split performs the split · Delete removes pending split lines, never map lines · right-drag pans"
        };
    }

    private void UpdateScopeText()
    {
        var pendingSplitCount =
            _guides.Count(guide => !guide.Applied) +
            (_draftCutPoints.Count >= 2 ? 1 : 0);

        ScopeText.Text =
            $"{_geometry.Features.Count:N0} lines · " +
            $"{GetValidSelectedLines().Count:N0} selected · " +
            (pendingSplitCount > 0
                ? $"{pendingSplitCount:N0} split line(s) waiting to be applied · "
                : string.Empty) +
            "pan: right-drag or Space+drag";
    }

    private UndoState CaptureUndoState(
        UndoActionKind kind)
    {
        return new UndoState(
            kind,
            new XDocument(_workingDocument),
            _hasPerformedCut,
            _guides.ToDictionary(
                guide => guide.Id,
                guide => new GuideUndoState(
                    guide.Applied,
                    guide.AppliedSequence,
                    guide.AppliedCutPoints.ToList())),
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

        var pendingGuides =
            _guides
                .Where(guide => !guide.Applied)
                .ToList();

        var appliedGuides =
            _guides
                .Where(guide => guide.Applied)
                .OrderBy(guide =>
                    guide.AppliedSequence)
                .ToList();

        var visibleGuides =
            new List<CutGuideState>();

        if (!_hideAppliedSplitHistory)
        {
            visibleGuides.AddRange(
                appliedGuides);
        }

        if (pendingGuides.Count > 0)
        {
            // Pending split lines are editor content, not a Step-2-only
            // decoration. Keep them visible while moving between workflow
            // steps; only editing handles and preview dots are Step-2-specific.
            visibleGuides.AddRange(
                pendingGuides);
        }

        var guideVisuals =
            visibleGuides
                .Select(guide =>
                    new LineEditorGuideVisual(
                        ProjectGuide(
                            guide.Guide),
                        guide.Applied)
                .ToList();

        EditorSurface.SetDocument(
            _geometry.Features,
            _selected,
            GetDisplayBounds(
                guideVisuals),
            guideVisuals,
            _workflowStep == 2
                ? _cutPoints
                : Array.Empty<Point>(),
            fitView);

        var appliedCutPoints =
            appliedGuides
                .SelectMany(guide =>
                    guide.AppliedCutPoints)
                .ToList();

        EditorSurface.SetAppliedCutPoints(
            !_hideAppliedSplitHistory
                ? appliedCutPoints
                : Array.Empty<Point>());

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            _draftCutPreviewPoint);

        var editingGuide = GetEditingGuide();

        EditorSurface.SetEditableGuide(
            _workflowStep == 2 &&
            editingGuide is not null
                ? ProjectGuide(editingGuide.Guide)
                : null);
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

    private string CurrentLineLabel =>
        _workflowStep == 3
            ? "piece(s)"
            : "line(s)";

    private CutGuideState?
        FindPendingGuideNearPoint(
            Point worldPoint,
            double tolerance)
    {
        CutGuideState? bestGuide = null;
        var bestDistance = tolerance;

        foreach (var guide in
                 _guides.Where(
                     item => !item.Applied))
        {
            var points =
                ProjectGuide(
                    guide.Guide);

            for (var index = 0;
                 index < points.Count - 1;
                 index++)
            {
                var distance =
                    DistanceToSegment(
                        worldPoint,
                        points[index],
                        points[index + 1]);

                if (distance <=
                    bestDistance)
                {
                    bestDistance =
                        distance;
                    bestGuide = guide;
                }
            }
        }

        return bestGuide;
    }

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

        _draftCutDrawing = false;
        _draftCutPreviewPoint = null;
        _editingGuideId = guide.Id;
        SetTool(LineEditorTool.Split);

        EditorSurface.SetEditableGuide(
            ProjectGuide(guide.Guide));

    }

    private void StopEditingGuide()
    {
        _editingGuideId = null;
        EditorSurface.SetEditableGuide(null);
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

        PushPendingGuideEditUndo();

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

        PushPendingGuideEditUndo();

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
            $"{_pendingEditUndo.Count + _undo.Count:N0} undo step(s).";

        UpdateUndoPresentation();
    }

    private void UpdateUndoPresentation()
    {
        string tooltip;
        var enabled = true;

        if (_workflowStep == 2 &&
            _draftCutPoints.Count > 0)
        {
            tooltip =
                "Undo the last point in the current split-line drawing (Ctrl+Z)";
        }
        else if (_pendingEditUndo.Count > 0)
        {
            tooltip =
                "Undo the last split-line edit (Ctrl+Z)";
        }
        else if (_undo.Count > 0)
        {
            tooltip =
                _undo.Peek().Kind ==
                    UndoActionKind.Split
                    ? "Undo the last applied split (Ctrl+Z)"
                    : "Undo the last line deletion (Ctrl+Z)";
        }
        else
        {
            tooltip =
                "Nothing to undo";
            enabled = false;
        }

        UndoButton.ToolTip = tooltip;
        UndoButton.IsEnabled = enabled;
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

        public int AppliedSequence { get; set; }

        public List<Point> AppliedCutPoints { get; set; } =
            new();
    }

    private enum UndoActionKind
    {
        Split,
        Deletion
    }

    private sealed record GuideUndoState(
        bool Applied,
        int AppliedSequence,
        IReadOnlyList<Point> AppliedCutPoints);

    private sealed record UndoState(
        UndoActionKind Kind,
        XDocument Document,
        bool HasPerformedCut,
        Dictionary<Guid, GuideUndoState> GuideStates,
        IReadOnlyList<Point> CutPoints);

    private sealed record PendingGuideSnapshot(
        string Name,
        IReadOnlyList<KmlCoordinate> Coordinates,
        bool RemoveMatchingSourceBeforeCut);

    private sealed record PendingGuideEditState(
        IReadOnlyList<PendingGuideSnapshot> Guides);
}

public sealed record LineEditorSessionGuideState(
    string Name,
    IReadOnlyList<KmlCoordinate> GuideCoordinates,
    int AppliedSequence,
    IReadOnlyList<KmlCoordinate> CutPoints);

public sealed record LineEditorSessionState(
    IReadOnlyList<LineEditorSessionGuideState> AppliedGuides,
    bool HideAppliedSplitHistory,
    string AppliedHistoryColor,
    double AppliedHistoryThickness);
