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
            if (pendingOpen.Count == 1)
            {
                if (ClosePendingClipGuide(pendingOpen[0]))
                    UpdateStatus("Pending boundary closed. Choose Keep inside or Keep outside.");
                return;
            }

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
        _clipPreviewGeneration++;
        _clipPreviewInProgress = false;
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

        if (_clipPreviewInProgress)
        {
            ApplyClipButton.IsEnabled = false;
            CancelClipPreviewButton.IsEnabled = true;
            ClipPreviewSummaryText.Text = "Calculating clip preview...";
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

    // Each asynchronous preview has a generation. Cancelling or editing a
    // boundary invalidates old results even if worker calculation completes.
    private long _clipPreviewGeneration;
    private bool _clipPreviewInProgress;

    private async void BuildAndShowClipPreview(
        ClipMode mode, Point? keepPoint)
    {
        if (!TryPrepareGuidesForClip(out var pending))
            return;

        var directionSampleDistance =
            EditorSurface.WorldToleranceFromPixels(10d);

        if (!TryCaptureClipWork(pending, mode, keepPoint,
                out var work, out var captureError))
        {
            CancelClipPreviewInternal(updateStatus: false);
            UpdateStatus(captureError);
            return;
        }

        CancelClipPreviewInternal(updateStatus: false);
        var generation = ++_clipPreviewGeneration;
        var geometryRevision = _geometryRevision;
        var onlySelected = OnlySplitSelectedCheckBox.IsChecked == true;
        var selectedLines = GetValidSelectedLines().ToHashSet();
        var guideVersions = CaptureClipGuides(pending);
        _clipPreviewInProgress = true;
        ApplyClipButton.IsEnabled = false;
        CancelClipPreviewButton.IsEnabled = true;
        ClipPreviewSummaryText.Text = "Calculating clip preview...";
        UpdateStatus("Calculating clip preview. No geometry has been changed.");

        try
        {
            // Work owns a separate XDocument and copied guide coordinate
            // arrays. It never touches live XML, selected XElements or WPF
            // controls on the worker thread.
            var result = await Task.Run(() =>
            {
                var ok = TryBuildClipPlan(
                    work, mode, keepPoint, directionSampleDistance,
                    out var plan, out var error);
                return (Ok: ok, Plan: plan, Error: error);
            });

            if (generation != _clipPreviewGeneration)
                return;

            _clipPreviewInProgress = false;
            if (geometryRevision != _geometryRevision ||
                onlySelected != (OnlySplitSelectedCheckBox.IsChecked == true) ||
                !selectedLines.SetEquals(GetValidSelectedLines()) ||
                !ClipGuidesMatch(guideVersions,
                    _guides.Where(guide => !guide.Applied).ToList()))
            {
                CancelClipPreviewInternal(updateStatus: false);
                UpdateStatus("The boundary or selection changed. Preview Clip again.");
                return;
            }

            if (!result.Ok)
            {
                CancelClipPreviewInternal(updateStatus: false);
                if (pending.Count == 1 && !pending[0].Guide.IsClosed &&
                    result.Error.Contains("doesn’t divide", StringComparison.Ordinal))
                    OfferToCloseRefusedClipGuide(pending[0]);
                else
                    UpdateStatus(result.Error);
                return;
            }

            var plan = result.Plan;
            _clipPreviewState = new ClipPreviewState(
                mode, keepPoint, plan, geometryRevision,
                onlySelected, selectedLines, guideVersions);

            EditorSurface.SetClipPreview(plan.Overlay);
            ApplyClipButton.IsEnabled = plan.DeletedPieces > 0;
            CancelClipPreviewButton.IsEnabled = true;
            ClipPreviewSummaryText.Text = FormatClipSummary(plan);
            UpdateStatus(plan.DeletedPieces == 0
                ? "Clip preview: nothing is on the delete side. No changes have been made."
                : $"Clip preview: will delete {plan.DeletedPieces:N0} piece(s) and keep {plan.KeptPieces:N0}. Choose Apply clip to continue.");
        }
        catch (Exception exception)
        {
            if (generation != _clipPreviewGeneration)
                return;
            CancelClipPreviewInternal(updateStatus: false);
            UpdateStatus($"Clip preview failed: {exception.Message}");
        }
    }

    private bool TryCaptureClipWork(
        IReadOnlyList<CutGuideState> pending,
        ClipMode mode,
        Point? keepPoint,
        out ClipWorkSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        error = string.Empty;
        var open = pending.Where(guide => !guide.Guide.IsClosed).ToList();
        if (open.Count > 0)
        {
            if (open.Count != 1 || pending.Count != 1 ||
                mode != ClipMode.KeepClickedSide || !keepPoint.HasValue)
            {
                error = "Clip uses one open line at a time. Choose the side to keep by clicking the map.";
                return false;
            }
        }
        else if (mode == ClipMode.KeepClickedSide)
        {
            error = "Choose side is for one open boundary. Use Keep inside or Keep outside for closed loops.";
            return false;
        }

        var selected = GetValidSelectedLines();
        var limit = OnlySplitSelectedCheckBox.IsChecked == true;
        if (limit && selected.Count == 0)
        {
            error = "Clip is limited to selected lines, but no lines are selected. Select lines or turn the limit off.";
            return false;
        }

        var protectedIds = pending
            .Where(guide => guide.RemoveMatchingSourceBeforeCut &&
                !string.IsNullOrWhiteSpace(guide.SourceLineId))
            .Select(guide => guide.SourceLineId!)
            .ToHashSet(StringComparer.Ordinal);
        var targetCount = _geometry.Features.Count(feature =>
            !protectedIds.Contains(
                feature.LineStringElement.Attribute(SourceGuideMarkerAttribute)?.Value ?? "") &&
            (!limit || selected.Contains(feature.LineStringElement)));
        if (targetCount == 0)
        {
            error = "There are no eligible lines to clip. The boundary itself is protected.";
            return false;
        }

        var clone = new XDocument(_workingDocument);
        MarkCloneTargets(clone, selected, limit, protectedIds);
        snapshot = new ClipWorkSnapshot(
            clone, _projectionScale, CaptureClipGuides(pending),
            limit, targetCount);
        return true;
    }

    private static bool TryBuildClipPlan(
        ClipWorkSnapshot work,
        ClipMode mode,
        Point? keepPoint,
        double directionSampleDistance,
        out ClipPlan plan,
        out string error)
    {
        plan = ClipPlan.Empty;
        error = string.Empty;
        var open = work.Guides.Where(guide => !guide.IsClosed).ToList();
        var clone = work.Document;
        var previewGeometry = new KmlLineGeometryService(
            clone, work.ProjectionScale);

        // Clip preserves source lines used as boundaries; Apply split retains
        // its existing source-helper deletion behaviour.

        var closedLoops =
            work.Guides
                .Where(
                    guide =>
                        guide.IsClosed)
                .Select(
                    guide =>
                        (IReadOnlyList<Point>)
                        guide.Coordinates
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
                    .Coordinates
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

        foreach (var guide in work.Guides)
        {
            IReadOnlyList<Point> stroke;

            if (guide.IsClosed)
            {
                stroke =
                    guide.Coordinates
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
            work.LimitToSelection
                ? $"limited to {work.TargetCount:N0} selected line(s)"
                : "all eligible lines",
            crossingLoop);

        return true;
    }

    private void ApplyClip(ClipPreviewState previewState)
    {
        var pending = _guides.Where(guide => !guide.Applied).ToList();
        if (previewState.GeometryRevision != _geometryRevision ||
            previewState.OnlySelected != (OnlySplitSelectedCheckBox.IsChecked == true) ||
            !previewState.SelectedLines.SetEquals(GetValidSelectedLines()) ||
            !ClipGuidesMatch(previewState.Guides, pending))
        {
            CancelClipPreviewInternal(updateStatus: false);
            UpdateStatus("The lines, boundary, or selection changed. Preview Clip again before applying.");
            return;
        }

        var plan = previewState.Plan;
        if (plan.DeletedPieces == 0)
        {
            UpdateStatus("Nothing is on the delete side. No geometry was changed.");
            return;
        }

        var snapshot = CaptureUndoState(UndoActionKind.Clip);
        try
        {
            // The exact document that produced the preview becomes the result:
            // no second split or classification, and no scope fallback.
            _workingDocument = new XDocument(plan.ResultDocument);
            _geometry = new KmlLineGeometryService(
                _workingDocument, _projectionScale);

            var applySequence = _nextAppliedSequence + 1;
            foreach (var guide in pending)
            {
                guide.Applied = true;
                guide.AppliedSequence = applySequence;
                guide.AppliedCutPoints = plan.CutPointsByGuide
                    .GetValueOrDefault(guide.Id, new List<Point>())
                    .ToList();
            }

            _nextAppliedSequence = applySequence;
            _hasPerformedCut = _hasPerformedCut || plan.LinesSplit > 0;
            PushDocumentUndo(snapshot);
            _pendingEditUndo.Clear();
            _pendingGuideDragUndoState = null;
            MarkGeometryChanged();
            _selected.Clear();
            _cutPoints.Clear();

            CancelClipPreviewInternal(updateStatus: false);
            _step3InstructionOverride =
                $"Clip applied: {plan.DeletedPieces:N0} unwanted line piece(s) deleted.";
            SetWorkflowStep(3);
            RefreshSurface();
            UpdateCutScopeDisplay();
            UpdateStatus(
                $"Clipped to boundary: {plan.LinesSplit:N0} line(s) split, " +
                $"{plan.KeptPieces:N0} piece(s) kept, {plan.DeletedPieces:N0} deleted.");
        }
        catch (Exception exception)
        {
            _workingDocument = new XDocument(snapshot.Document);
            _geometry = new KmlLineGeometryService(
                _workingDocument, _projectionScale);
            foreach (var guide in _guides)
            {
                if (!snapshot.GuideStates.TryGetValue(guide.Id, out var state))
                    continue;
                guide.Applied = state.Applied;
                guide.AppliedSequence = state.AppliedSequence;
                guide.AppliedCutPoints = state.AppliedCutPoints.ToList();
            }

            RefreshSurface();
            UpdatePendingGuidePreview();
            UpdateCutScopeDisplay();
            UpdateStatus($"Clip was not applied. {exception.Message}");
        }
    }

    private static string FormatClipSummary(ClipPlan plan) =>
        (plan.DeletedPieces == 0
            ? $"Nothing on the delete side; keep {plan.KeptPieces:N0} line piece(s)"
            : $"Will delete {plan.DeletedPieces:N0} line piece(s), keep {plan.KeptPieces:N0}") +
        $" — {plan.ScopeDescription}. Clip removes lines only." +
        (plan.HasSelfCrossingLoop
            ? " This loop crosses itself; inside follows the even-odd rule."
            : "");

    private static List<ClipGuideVersion> CaptureClipGuides(
        IReadOnlyList<CutGuideState> pending) =>
        pending.Select(guide => new ClipGuideVersion(
            guide.Id, guide.Guide.IsClosed, guide.SourceLineId,
            guide.Guide.Coordinates.ToList())).ToList();

    private static bool ClipGuidesMatch(
        IReadOnlyList<ClipGuideVersion> captured,
        IReadOnlyList<CutGuideState> pending)
    {
        if (captured.Count != pending.Count)
            return false;

        for (var i = 0; i < captured.Count; i++)
        {
            var version = captured[i];
            var current = pending[i];
            if (version.Id != current.Id ||
                version.IsClosed != current.Guide.IsClosed ||
                version.SourceLineId != current.SourceLineId ||
                !version.Coordinates.SequenceEqual(current.Guide.Coordinates))
                return false;
        }
        return true;
    }

    private void OfferToCloseRefusedClipGuide(CutGuideState guide)
    {
        var choice = System.Windows.MessageBox.Show(
            this,
            "This open boundary does not divide the map. Join its ends and use it as a closed loop? " +
            "You can then choose Keep inside or Keep outside.",
            "Close boundary loop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes)
        {
            if (ClosePendingClipGuide(guide))
                UpdateStatus("Boundary loop closed. Choose Keep inside or Keep outside.");
        }
        else
        {
            UpdateStatus("This line does not divide the map. No geometry was changed.");
        }
    }

    private bool ClosePendingClipGuide(CutGuideState guide)
    {
        if (guide.Applied || guide.Guide.IsClosed)
            return false;

        var coordinates = guide.Guide.Coordinates.ToList();
        var distinct = coordinates.Select(_geometry.Project).Distinct().ToList();
        if (distinct.Count < 3)
        {
            UpdateStatus("A closed loop needs at least three distinct points.");
            return false;
        }

        if (IsCoordinateLoopClosed(coordinates))
            coordinates[^1] = coordinates[0];
        else
            coordinates.Add(coordinates[0]);

        var points = coordinates.Select(_geometry.Project).ToList();
        if (points.Take(points.Count - 1).Distinct().Count() < 3)
        {
            UpdateStatus("Closing this line would leave fewer than three distinct vertices.");
            return false;
        }

        // A self-crossing loop may have zero signed area but still enclose
        // even-odd regions. Reject only an entirely collinear boundary.
        var nonCollinear = false;
        for (var i = 1; i < points.Count - 2; i++)
        {
            var ab = points[i] - points[0];
            var ac = points[i + 1] - points[0];
            if (Math.Abs(ab.X * ac.Y - ab.Y * ac.X) > 1e-18d)
            {
                nonCollinear = true;
                break;
            }
        }
        if (!nonCollinear)
        {
            UpdateStatus("Joining these endpoints would make a degenerate loop.");
            return false;
        }

        PushPendingGuideEditUndo();
        guide.Guide = new KmlCutGuide(
            guide.Guide.Name, coordinates, true);
        CancelClipPreviewInternal(updateStatus: false);
        StartEditingGuide(guide);
        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();
        return true;
    }

    private void MarkCloneTargets(
        XDocument clone,
        IReadOnlyCollection<XElement> selected,
        bool limitToSelection,
        IReadOnlySet<string> protectedSourceIds)
    {
        var sourceLines = _workingDocument
            .Descendants(KmlNs + "LineString").ToList();
        var cloneLines = clone.Descendants(KmlNs + "LineString").ToList();
        var selectedSet = selected.ToHashSet();

        for (var i = 0; i < Math.Min(sourceLines.Count, cloneLines.Count); i++)
        {
            var original = sourceLines[i];
            var protectedSource = protectedSourceIds.Contains(
                original.Attribute(SourceGuideMarkerAttribute)?.Value ?? "");
            if (!protectedSource &&
                (!limitToSelection || selectedSet.Contains(original)))
            {
                cloneLines[i].SetAttributeValue(
                    CutTargetMarkerAttribute, "true");
            }
        }
    }

    private sealed record ClipWorkSnapshot(
        XDocument Document,
        double ProjectionScale,
        IReadOnlyList<ClipGuideVersion> Guides,
        bool LimitToSelection,
        int TargetCount);

    private sealed record ClipGuideVersion(
        Guid Id,
        bool IsClosed,
        string? SourceLineId,
        IReadOnlyList<KmlCoordinate> Coordinates);

    private sealed record ClipPreviewState(
        ClipMode Mode,
        Point? KeepPoint,
        ClipPlan Plan,
        long GeometryRevision,
        bool OnlySelected,
        HashSet<XElement> SelectedLines,
        IReadOnlyList<ClipGuideVersion> Guides);

    private sealed record ClipPlan(
        int LinesSplit,
        int KeptPieces,
        int DeletedPieces,
        LineEditorClipPreviewOverlay Overlay,
        IReadOnlyDictionary<Guid, List<Point>> CutPointsByGuide,
        PreparedOpenBoundary? OpenBoundary,
        XDocument ResultDocument,
        string ScopeDescription,
        bool HasSelfCrossingLoop)
    {
        public static ClipPlan Empty { get; } = new(
            0, 0, 0,
            new LineEditorClipPreviewOverlay(
                Rect.Empty,
                Array.Empty<IReadOnlyList<Point>>(),
                false,
                Array.Empty<IReadOnlyList<Point>>(),
                Array.Empty<IReadOnlyList<Point>>()),
            new Dictionary<Guid, List<Point>>(),
            null,
            new XDocument(),
            "no eligible lines",
            false);
    }
}
