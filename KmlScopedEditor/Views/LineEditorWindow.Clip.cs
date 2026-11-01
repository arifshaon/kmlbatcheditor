using System.Windows;
using System.Xml.Linq;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace KmlScopedEditor.Views;

public partial class LineEditorWindow
{
    private bool _clipSidePickActive;
    private ClipPreviewState? _clipPreviewState;

    private enum ClipMode
    {
        KeepInside,
        KeepOutside,
        KeepClickedSide
    }

    private void OnCloseLoopClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_draftCutPoints.Count < 3)
        {
            var pendingOpen = _guides
                .Where(guide => !guide.Applied && !guide.Guide.IsClosed)
                .ToList();
            if (pendingOpen.Count == 1 && ClosePendingClipGuide(pendingOpen[0]))
                UpdateStatus("Pending boundary closed. Choose Keep inside or Keep outside.");
            else
                UpdateStatus("Draw at least three points, or use one pending open boundary.");
            return;
        }

        if (!IsWorldLoopClosed(
                _draftCutPoints))
        {
            _draftCutPoints.Add(
                _draftCutPoints[0]);
        }
        else
        {
            _draftCutPoints[^1] =
                _draftCutPoints[0];
        }

        EditorSurface.SetDraftCut(
            _draftCutPoints,
            null);

        FinishDraftCutDrawing();
    }

    private static bool IsWorldLoopClosed(
        IReadOnlyList<Point> points)
    {
        if (points.Count < 4)
            return false;

        return
            (points[0] -
             points[^1]).Length <=
            KmlBoundaryClipService
                .WorldUnitsFromMetres(
                    0.5d);
    }

    private bool IsCoordinateLoopClosed(
        IReadOnlyList<KmlCoordinate> coordinates)
    {
        if (coordinates.Count < 4)
            return false;

        return
            (_geometry.Project(
                 coordinates[0]) -
             _geometry.Project(
                 coordinates[^1]))
            .Length <=
            KmlBoundaryClipService
                .WorldUnitsFromMetres(
                    0.5d);
    }

    private void OnClipKeepInsideClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryPrepareGuidesForClip(
                out var pending))
        {
            return;
        }

        if (pending.Any(
                guide =>
                    !guide.Guide.IsClosed))
        {
            UpdateStatus(
                "Keep inside requires closed boundary loops. For an open line, choose Keep the side I click.");
            return;
        }

        BuildAndShowClipPreview(
            ClipMode.KeepInside,
            keepPoint: null);
    }

    private void OnClipKeepOutsideClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryPrepareGuidesForClip(
                out var pending))
        {
            return;
        }

        if (pending.Any(
                guide =>
                    !guide.Guide.IsClosed))
        {
            UpdateStatus(
                "Keep outside requires closed boundary loops. For an open line, choose Keep the side I click.");
            return;
        }

        BuildAndShowClipPreview(
            ClipMode.KeepOutside,
            keepPoint: null);
    }

    private void OnClipChooseSideClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryPrepareGuidesForClip(
                out var pending))
        {
            return;
        }

        var openCount =
            pending.Count(
                guide =>
                    !guide.Guide.IsClosed);

        if (openCount != 1 ||
            pending.Count != 1)
        {
            UpdateStatus(
                "Clip uses one open line at a time. Remove other pending boundaries, or use closed loops with Keep inside/Keep outside.");
            return;
        }

        CancelClipPreviewInternal(
            updateStatus: false);

        StopEditingGuide();
        _clipSidePickActive = true;
        _draftCutDrawing = false;
        _draftCutPreviewPoint = null;
        EditorSurface.SetDraftCut(
            _draftCutPoints,
            null);
        EditorSurface.Cursor =
            System.Windows.Input.Cursors.Cross;

        UpdateStatus(
            "Click clearly on the side of the open boundary that you want to keep.");
    }

    private void HandleClipSideClick(
        Point worldPoint)
    {
        if (!_clipSidePickActive)
            return;

        _clipSidePickActive = false;

        BuildAndShowClipPreview(
            ClipMode.KeepClickedSide,
            worldPoint);
    }

    private void OnApplyClipClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_clipPreviewState is null)
        {
            UpdateStatus(
                "Create a clip preview first.");
            return;
        }

        ApplyClip(
            _clipPreviewState);
    }

    private void OnCancelClipPreviewClick(
        object sender,
        RoutedEventArgs e)
    {
        CancelClipPreviewInternal(
            updateStatus: true);
    }

    private void CancelClipPreviewInternal(
        bool updateStatus)
    {
        var hadPreview =
            _clipPreviewState is not null ||
            _clipSidePickActive;

        _clipPreviewState = null;
        _clipSidePickActive = false;

        if (EditorSurface is not null)
        {
            EditorSurface.ClearClipPreview();

            EditorSurface.Cursor =
                EditorSurface.Tool switch
                {
                    LineEditorTool.Pan =>
                        System.Windows.Input.Cursors.Hand,
                    LineEditorTool.Split =>
                        System.Windows.Input.Cursors.Cross,
                    _ =>
                        System.Windows.Input.Cursors.Arrow
                };
        }

        if (ApplyClipButton is not null)
            ApplyClipButton.IsEnabled = false;

        if (CancelClipPreviewButton is not null)
            CancelClipPreviewButton.IsEnabled = false;

        if (ClipPreviewSummaryText is not null)
        {
            ClipPreviewSummaryText.Text =
                "Closed loop: keep inside/outside. Open line: choose a side.";
        }

        if (updateStatus &&
            hadPreview)
        {
            UpdateStatus(
                "Clip preview cancelled. No geometry was changed.");
        }
    }

    private void UpdateClipControlState()
    {
        if (ApplyClipButton is null ||
            CancelClipPreviewButton is null ||
            ClipPreviewSummaryText is null)
        {
            return;
        }

        ApplyClipButton.IsEnabled =
            _clipPreviewState is not null &&
            _clipPreviewState.Plan.DeletedPieces > 0;

        CancelClipPreviewButton.IsEnabled =
            _clipPreviewState is not null ||
            _clipSidePickActive;

        if (_clipPreviewState is not null)
        {
            var plan = _clipPreviewState.Plan;
            ClipPreviewSummaryText.Text = FormatClipSummary(plan);
            return;
        }

        var pending =
            _guides
                .Where(
                    guide =>
                        !guide.Applied)
                .ToList();

        if (pending.Count == 0)
        {
            ClipPreviewSummaryText.Text =
                "Add a pending open line or closed loop to use as a clip boundary.";
            return;
        }

        var openCount =
            pending.Count(
                guide =>
                    !guide.Guide.IsClosed);

        if (openCount == 0)
        {
            ClipPreviewSummaryText.Text =
                pending.Count == 1
                    ? "Closed loop ready — choose Keep inside or Keep outside."
                    : $"{pending.Count:N0} closed loops ready — inside means inside any loop.";
        }
        else if (pending.Count == 1)
        {
            ClipPreviewSummaryText.Text =
                "Open line ready — choose a side, then click the map on the side to keep.";
        }
        else
        {
            ClipPreviewSummaryText.Text =
                "Clip uses one open line at a time; open lines cannot be combined with other pending boundaries.";
        }
    }

    private bool TryPrepareGuidesForClip(
        out List<CutGuideState> pending)
    {
        pending =
            new List<CutGuideState>();

        if (_draftCutPoints.Count == 1)
        {
            UpdateStatus(
                "The current boundary needs at least one more point before it can be used.");
            return false;
        }

        if (_draftCutPoints.Count >= 2)
            CommitDraftCutAsPendingGuide();

        pending =
            _guides
                .Where(
                    guide =>
                        !guide.Applied)
                .ToList();

        if (pending.Count == 0)
        {
            UpdateStatus(
                "There is no pending boundary. Import, draw, or use a selected line first.");
            return false;
        }

        var openCount =
            pending.Count(
                guide =>
                    !guide.Guide.IsClosed);

        if (openCount > 0 &&
            pending.Count > 1)
        {
            UpdateStatus(
                "Clip uses one open line at a time. Remove the other pending boundaries first.");
            return false;
        }

        return true;
    }

    private void BuildAndShowClipPreview(
        ClipMode mode,
        Point? keepPoint)
    {
        if (!TryPrepareGuidesForClip(
                out var pending))
        {
            return;
        }

        var directionSampleDistance =
            EditorSurface
                .WorldToleranceFromPixels(
                    10d);

        if (!TryBuildClipPlan(
                pending,
                mode,
                keepPoint,
                directionSampleDistance,
                out var plan,
                out var error))
        {
            CancelClipPreviewInternal(
                updateStatus: false);
            if (pending.Count == 1 &&
                !pending[0].Guide.IsClosed &&
                error.Contains("doesn’t divide", StringComparison.Ordinal))
                OfferToCloseRefusedClipGuide(pending[0]);
            else
                UpdateStatus(error);
            return;
        }

        _clipPreviewState = new ClipPreviewState(
            mode,
            keepPoint,
            plan,
            _geometryRevision,
            OnlySplitSelectedCheckBox.IsChecked == true,
            GetValidSelectedLines().ToHashSet(),
            CaptureClipGuides(pending));

        EditorSurface.SetClipPreview(
            plan.Overlay);

        ApplyClipButton.IsEnabled =
            plan.DeletedPieces > 0;

        CancelClipPreviewButton.IsEnabled =
            true;

        ClipPreviewSummaryText.Text = FormatClipSummary(plan);

        UpdateStatus(
            plan.DeletedPieces == 0
                ? "Clip preview: nothing is on the delete side. No changes have been made."
                : $"Clip preview: will delete {plan.DeletedPieces:N0} piece(s) and keep {plan.KeptPieces:N0}. Choose Apply clip to continue.");
    }

    private bool TryBuildClipPlan(
        IReadOnlyList<CutGuideState> pending,
        ClipMode mode,
        Point? keepPoint,
        double directionSampleDistance,
        out ClipPlan plan,
        out string error)
    {
        plan =
            ClipPlan.Empty;
        error =
            string.Empty;

        var open =
            pending
                .Where(
                    guide =>
                        !guide.Guide.IsClosed)
                .ToList();

        if (open.Count > 0)
        {
            if (open.Count != 1 ||
                pending.Count != 1 ||
                mode !=
                    ClipMode.KeepClickedSide ||
                !keepPoint.HasValue)
            {
                error =
                    "Clip uses one open line at a time. Choose the side to keep by clicking the map.";
                return false;
            }
        }
        else if (mode ==
                 ClipMode.KeepClickedSide)
        {
            error =
                "Choose side is for one open boundary. Use Keep inside or Keep outside for closed loops.";
            return false;
        }

        var sourceSelected =
            GetValidSelectedLines();

        var limitToSelection = OnlySplitSelectedCheckBox.IsChecked == true;
        if (limitToSelection && sourceSelected.Count == 0)
        {
            error = "Clip is limited to selected lines, but no lines are selected. Select lines or turn the limit off.";
            return false;
        }

        var protectedSourceIds = pending
            .Where(guide => guide.RemoveMatchingSourceBeforeCut &&
                !string.IsNullOrWhiteSpace(guide.SourceLineId))
            .Select(guide => guide.SourceLineId!)
            .ToHashSet(StringComparer.Ordinal);
        var scopedTargets = _geometry.Features.Count(feature =>
            !protectedSourceIds.Contains(
                feature.LineStringElement.Attribute(SourceGuideMarkerAttribute)?.Value ?? "") &&
            (!limitToSelection ||
                sourceSelected.Contains(feature.LineStringElement)));
        if (scopedTargets == 0)
        {
            error = "There are no eligible lines to clip. The boundary itself is protected.";
            return false;
        }

        var clone =
            new XDocument(
                _workingDocument);

        MarkCloneTargets(
            clone,
            sourceSelected,
            limitToSelection,
            protectedSourceIds);

        var previewGeometry =
            new KmlLineGeometryService(
                clone,
                _projectionScale);

        // Clip preserves source lines used as boundaries; Apply split retains
        // its existing source-helper deletion behaviour.

        var closedLoops =
            pending
                .Where(
                    guide =>
                        guide.Guide.IsClosed)
                .Select(
                    guide =>
                        (IReadOnlyList<Point>)
                        guide.Guide.Coordinates
                            .Select(
                                previewGeometry.Project)
                            .ToList())
                .ToList();

        PreparedOpenBoundary?
            openBoundary = null;

        if (open.Count == 1)
        {
            var openPoints =
                open[0]
                    .Guide.Coordinates
                    .Select(
                        previewGeometry.Project)
                    .ToList();

            if (!KmlBoundaryClipService
                    .TryPrepareOpenBoundary(
                        openPoints,
                        previewGeometry.WorldBounds,
                        directionSampleDistance,
                        out openBoundary,
                        out error))
            {
                return false;
            }
        }

        var totalLinesSplit = 0;
        var cutPointsByGuide =
            new Dictionary<
                Guid,
                List<Point>>();

        foreach (var guide in pending)
        {
            IReadOnlyList<Point> stroke;

            if (guide.Guide.IsClosed)
            {
                stroke =
                    guide.Guide.Coordinates
                        .Select(
                            previewGeometry.Project)
                        .ToList();
            }
            else
            {
                stroke =
                    openBoundary!
                        .ExtendedPath;
            }

            var result =
                previewGeometry
                    .SplitWithStroke(
                        stroke,
                        CutTargetMarkerAttribute);

            totalLinesSplit +=
                result.LinesSplit;

            cutPointsByGuide[
                guide.Id] =
                result.CutPoints.ToList();
        }

        ClipClassificationResult
            classification;

        LineEditorClipPreviewOverlay
            overlay;

        if (openBoundary is not null)
        {
            if (!KmlBoundaryClipService
                    .TryClassifyOpen(
                        previewGeometry.Features,
                        openBoundary,
                        keepPoint!.Value,
                        CutTargetMarkerAttribute,
                        out classification,
                        out var keepRegionA,
                        out error))
            {
                return false;
            }

            overlay =
                new LineEditorClipPreviewOverlay(
                    openBoundary.Bounds,
                    Array.Empty<
                        IReadOnlyList<Point>>(),
                    false,
                    new[]
                    {
                        (IReadOnlyList<Point>)
                            (keepRegionA
                                ? openBoundary.RegionB
                                : openBoundary.RegionA)
                    },
                    openBoundary.ExtensionSegments);
        }
        else
        {
            var keepInside =
                mode ==
                ClipMode.KeepInside;

            classification =
                KmlBoundaryClipService
                    .ClassifyClosed(
                        previewGeometry.Features,
                        closedLoops,
                        keepInside,
                        CutTargetMarkerAttribute);

            var allBoundaryPoints =
                closedLoops
                    .SelectMany(
                        loop => loop)
                    .ToList();

            var bounds =
                KmlBoundaryClipService
                    .BuildClipBounds(
                        previewGeometry.WorldBounds,
                        allBoundaryPoints);

            overlay =
                new LineEditorClipPreviewOverlay(
                    bounds,
                    closedLoops,
                    keepInside,
                    Array.Empty<
                        IReadOnlyList<Point>>(),
                    Array.Empty<
                        IReadOnlyList<Point>>());
        }

        // Store the complete prepared document, so Apply need not recalculate.
        var deletedCount = classification.DeletedLines.Count;
        previewGeometry.DeleteLines(classification.DeletedLines);
        foreach (var line in clone.Descendants(KmlNs + "LineString"))
            line.Attribute(CutTargetMarkerAttribute)?.Remove();

        var crossingLoop = closedLoops.Any(
            KmlBoundaryClipService.LoopSelfIntersects);
        plan = new ClipPlan(
            totalLinesSplit,
            classification.KeptLines.Count,
            deletedCount,
            overlay,
            cutPointsByGuide,
            openBoundary,
            clone,
            limitToSelection
                ? $"limited to {scopedTargets:N0} selected line(s)"
                : "all eligible lines",
            crossingLoop);

        return true;
    }

    private void ApplyClip(
        ClipPreviewState previewState)
    {
        var mode =
            previewState.Mode;
        var keepPoint =
            previewState.KeepPoint;

        if (!TryPrepareGuidesForClip(
                out var pending))
        {
            return;
        }

        // Re-run the preview calculation immediately before changing the real
        // document. This keeps Apply aligned with what the user is seeing and
        // also protects against a stale preview after scope/guide changes.
        if (!TryBuildClipPlan(
                pending,
                mode,
                keepPoint,
                previewState.DirectionSampleDistance,
                out var previewPlan,
                out var error))
        {
            UpdateStatus(error);
            return;
        }

        if (previewPlan.DeletedPieces == 0)
        {
            UpdateStatus(
                "Nothing is on the delete side. No geometry was changed.");
            return;
        }

        var snapshot =
            CaptureUndoState(
                UndoActionKind.Clip);

        var selectedTargets =
            GetValidSelectedLines();

        var limitToSelection =
            OnlySplitSelectedCheckBox.IsChecked ==
                true &&
            selectedTargets.Count > 0;

        ClearCutTargetMarkers();

        if (limitToSelection)
        {
            foreach (var line in
                     selectedTargets)
            {
                if (line.Document ==
                    _workingDocument)
                {
                    line.SetAttributeValue(
                        CutTargetMarkerAttribute,
                        "true");
                }
            }
        }

        var openBoundary =
            previewPlan.OpenBoundary;

        var totalLinesSplit = 0;
        var applySequence =
            _nextAppliedSequence + 1;
        var cutPointsByGuide =
            new Dictionary<
                Guid,
                List<Point>>();

        try
        {
            RemoveHelperLinesFromGeometry(
                _geometry,
                pending);

            foreach (var guide in pending)
            {
                IReadOnlyList<Point> stroke =
                    guide.Guide.IsClosed
                        ? ProjectGuide(
                            guide.Guide)
                        : openBoundary!
                            .ExtendedPath;

                var result =
                    _geometry
                        .SplitWithStroke(
                            stroke,
                            CutTargetMarkerAttribute);

                totalLinesSplit +=
                    result.LinesSplit;

                cutPointsByGuide[
                    guide.Id] =
                    result.CutPoints.ToList();
            }

            ClipClassificationResult
                classification;

            if (openBoundary is not null)
            {
                if (!KmlBoundaryClipService
                        .TryClassifyOpen(
                            _geometry.Features,
                            openBoundary,
                            keepPoint!.Value,
                            CutTargetMarkerAttribute,
                            out classification,
                            out _,
                            out error))
                {
                    throw new InvalidOperationException(
                        error);
                }
            }
            else
            {
                var loops =
                    pending
                        .Select(
                            guide =>
                                (IReadOnlyList<Point>)
                                ProjectGuide(
                                    guide.Guide))
                        .ToList();

                classification =
                    KmlBoundaryClipService
                        .ClassifyClosed(
                            _geometry.Features,
                            loops,
                            mode ==
                                ClipMode.KeepInside,
                            CutTargetMarkerAttribute);
            }

            var deleted =
                _geometry.DeleteLines(
                    classification.DeletedLines);

            if (deleted == 0)
            {
                throw new InvalidOperationException(
                    "Nothing is on the delete side. No geometry was changed.");
            }

            foreach (var guide in pending)
            {
                guide.Applied = true;
                guide.AppliedSequence =
                    applySequence;
                guide.AppliedCutPoints =
                    cutPointsByGuide
                        .GetValueOrDefault(
                            guide.Id,
                            new List<Point>());
            }

            _nextAppliedSequence =
                applySequence;

            _hasPerformedCut =
                _hasPerformedCut ||
                totalLinesSplit > 0;

            PushDocumentUndo(
                snapshot);
            _pendingEditUndo.Clear();
            _pendingGuideDragUndoState = null;
            MarkGeometryChanged();

            _selected.Clear();
            _cutPoints.Clear();

            var kept =
                classification
                    .KeptLines.Count;

            CancelClipPreviewInternal(
                updateStatus: false);

            _step3InstructionOverride =
                $"Clip applied: {deleted:N0} unwanted piece(s) deleted. Select any leftovers you want to tidy.";

            SetWorkflowStep(3);
            RefreshSurface();
            UpdateCutScopeDisplay();

            UpdateStatus(
                $"Clipped to boundary: {totalLinesSplit:N0} line(s) split, {kept:N0} piece(s) kept, {deleted:N0} deleted.");
        }
        catch (Exception exception)
        {
            // Restore the single pre-clip snapshot so a failed transaction
            // cannot leave a partially split/deleted working document.
            _workingDocument =
                new XDocument(
                    snapshot.Document);

            _geometry =
                new KmlLineGeometryService(
                    _workingDocument,
                    _projectionScale);

            foreach (var guide in
                     _guides)
            {
                if (snapshot.GuideStates
                    .TryGetValue(
                        guide.Id,
                        out var state))
                {
                    guide.Applied =
                        state.Applied;
                    guide.AppliedSequence =
                        state.AppliedSequence;
                    guide.AppliedCutPoints =
                        state.AppliedCutPoints
                            .ToList();
                }
            }

            RefreshSurface();
            UpdatePendingGuidePreview();
            UpdateCutScopeDisplay();

            UpdateStatus(
                $"Clip was not applied. {exception.Message}");
        }
        finally
        {
            ClearCutTargetMarkers();
        }
    }

    private void MarkCloneTargets(
        XDocument clone,
        IReadOnlyCollection<XElement> selected,
        bool limitToSelection)
    {
        if (!limitToSelection)
            return;

        var sourceLines =
            _workingDocument
                .Descendants(
                    KmlNs +
                    "LineString")
                .ToList();

        var cloneLines =
            clone
                .Descendants(
                    KmlNs +
                    "LineString")
                .ToList();

        var selectedSet =
            selected.ToHashSet();

        var count =
            Math.Min(
                sourceLines.Count,
                cloneLines.Count);

        for (var index = 0;
             index < count;
             index++)
        {
            if (selectedSet.Contains(
                    sourceLines[index]))
            {
                cloneLines[index]
                    .SetAttributeValue(
                        CutTargetMarkerAttribute,
                        "true");
            }
        }
    }

    private static void RemoveHelperLinesFromGeometry(
        KmlLineGeometryService geometry,
        IReadOnlyList<CutGuideState> guides)
    {
        var sourceIds =
            guides
                .Where(
                    guide =>
                        guide.RemoveMatchingSourceBeforeCut &&
                        !string.IsNullOrWhiteSpace(
                            guide.SourceLineId))
                .Select(
                    guide =>
                        guide.SourceLineId!)
                .ToHashSet(
                    StringComparer.Ordinal);

        if (sourceIds.Count == 0)
            return;

        var helpers =
            geometry.Features
                .Where(
                    feature =>
                        sourceIds.Contains(
                            feature.LineStringElement
                                .Attribute(
                                    SourceGuideMarkerAttribute)
                                ?.Value ??
                            string.Empty))
                .Select(
                    feature =>
                        feature.LineStringElement)
                .ToList();

        if (helpers.Count > 0)
            geometry.DeleteLines(helpers);
    }

    private sealed record ClipPreviewState(
        ClipMode Mode,
        Point? KeepPoint,
        double DirectionSampleDistance,
        int KeptPieces,
        int DeletedPieces);

    private sealed record ClipPlan(
        int LinesSplit,
        int KeptPieces,
        int DeletedPieces,
        LineEditorClipPreviewOverlay Overlay,
        IReadOnlyDictionary<
            Guid,
            List<Point>> CutPointsByGuide,
        PreparedOpenBoundary? OpenBoundary)
    {
        public static ClipPlan Empty { get; } =
            new(
                0,
                0,
                0,
                new LineEditorClipPreviewOverlay(
                    Rect.Empty,
                    Array.Empty<
                        IReadOnlyList<Point>>(),
                    false,
                    Array.Empty<
                        IReadOnlyList<Point>>(),
                    Array.Empty<
                        IReadOnlyList<Point>>()),
                new Dictionary<
                    Guid,
                    List<Point>>(),
                null);
    }
}
