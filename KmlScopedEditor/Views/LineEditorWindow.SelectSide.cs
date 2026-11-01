using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using KmlScopedEditor.Services;
using KmlScopedEditor.Models;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace KmlScopedEditor.Views;

public partial class LineEditorWindow
{
    private bool _sideSelectActive;
    private bool _sideSelectToggleUpdating;
    private KmlBoundaryClipService.SideSelectionContext? _sideSelectContext;
    private List<IReadOnlyList<Point>> _sideSelectClosedLoops = new();
    private PreparedOpenBoundary? _sideSelectOpenBoundary;
    private long _sideSelectRevision = -1;
    private int _sideSelectSequence = -1;

    private void OnStep3SelectSideChanged(object sender, RoutedEventArgs e)
    {
        if (_sideSelectToggleUpdating || !IsLoaded)
            return;

        if (Step3SelectSideButton.IsChecked == true)
        {
            if (!BeginSideSelectMode())
                EndSideSelectMode();
        }
        else
        {
            EndSideSelectMode();
        }
    }

    private bool BeginSideSelectMode()
    {
        if (_workflowStep != 3)
            return false;

        if (!TryBuildSideSelectContext(out var error))
        {
            UpdateStatus(error);
            return false;
        }

        _sideSelectActive = true;
        Step3LassoButton.IsChecked = false;
        Step1LassoButton.IsChecked = false;
        _lassoModeEnabled = false;
        EditorSurface.LassoMode = false;
        SetTool(LineEditorTool.Select);
        EditorSurface.Cursor = System.Windows.Input.Cursors.Cross;
        ShowSideSelectBoundary();
        var crossedLoop = _sideSelectClosedLoops.Any(
            KmlBoundaryClipService.LoopSelfIntersects);
        UpdateStatus("Click the side to select. Shift-click adds; Alt-click removes." +
            (crossedLoop
                ? " Warning: the last closed loop crosses itself; inside uses the even-odd rule."
                : ""));
        return true;
    }

    private bool TryBuildSideSelectContext(out string error)
    {
        error = string.Empty;
        var last = _guides.Where(guide => guide.Applied)
            .Select(guide => guide.AppliedSequence)
            .DefaultIfEmpty(0).Max();

        if (last <= 0)
        {
            error = "Apply a split first.";
            return false;
        }

        var latest = _guides.Where(guide =>
            guide.Applied && guide.AppliedSequence == last).ToList();
        var open = latest.Where(guide => !guide.Guide.IsClosed).ToList();
        if (open.Count > 1 || (open.Count == 1 && latest.Count != 1))
        {
            error = "Select side works with the last split's closed loops, or a single open line.";
            return false;
        }

        var loops = latest.Where(guide => guide.Guide.IsClosed)
            .Select(guide => (IReadOnlyList<Point>)ProjectGuide(guide.Guide).ToList())
            .ToList();

        PreparedOpenBoundary? prepared = null;
        if (open.Count == 1)
        {
            var points = ProjectGuide(open[0].Guide);
            if (!KmlBoundaryClipService.TryPrepareOpenBoundary(
                points, _geometry.WorldBounds,
                KmlBoundaryClipService.WorldUnitsFromMetres(10d),
                out prepared, out error))
            {
                error = "This split line doesn't divide the map, so it has no sides. " +
                    "Undo the split, use Close loop, then split again.";
                return false;
            }
        }

        if (!KmlBoundaryClipService.TryPrepareSideSelection(
            loops, prepared, out var context, out error))
            return false;

        _sideSelectContext = context;
        _sideSelectClosedLoops = loops;
        _sideSelectOpenBoundary = prepared;
        _sideSelectRevision = _geometryRevision;
        _sideSelectSequence = last;
        return true;
    }

    private void ShowSideSelectBoundary()
    {
        if (!_sideSelectActive)
            return;

        var paths = _sideSelectOpenBoundary is not null
            ? new List<IReadOnlyList<Point>>
                { _sideSelectOpenBoundary.ExtendedPath }
            : _sideSelectClosedLoops.ToList();

        var bounds = _sideSelectOpenBoundary?.Bounds ??
            KmlBoundaryClipService.BuildClipBounds(
                _geometry.WorldBounds,
                paths.SelectMany(path => path).ToList());

        EditorSurface.SetClipPreview(new LineEditorClipPreviewOverlay(
            bounds, Array.Empty<IReadOnlyList<Point>>(), false,
            Array.Empty<IReadOnlyList<Point>>(), paths));
    }

    private void EndSideSelectMode()
    {
        if (!_sideSelectActive &&
            (Step3SelectSideButton.IsChecked != true))
            return;

        _sideSelectActive = false;
        _sideSelectContext = null;
        _sideSelectClosedLoops.Clear();
        _sideSelectOpenBoundary = null;
        _sideSelectRevision = -1;
        _sideSelectSequence = -1;
        _sideSelectToggleUpdating = true;
        try
        {
            Step3SelectSideButton.IsChecked = false;
        }
        finally
        {
            _sideSelectToggleUpdating = false;
        }
        EditorSurface.ClearClipPreview();
        EditorSurface.Cursor = EditorSurface.Tool == LineEditorTool.Pan
            ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
    }

    private void RefreshSideSelectAfterGeometryChange()
    {
        if (!_sideSelectActive)
            return;

        if (!TryBuildSideSelectContext(out var error))
        {
            EndSideSelectMode();
            UpdateStatus(error);
            return;
        }
        ShowSideSelectBoundary();
    }

    private void HandleSideSelectClick(
        LineEditorClickEventArgs e, bool allowRepairPrompt = true)
    {
        if (!_sideSelectActive)
            return;

        var newest = _guides.Where(guide => guide.Applied)
            .Select(guide => guide.AppliedSequence)
            .DefaultIfEmpty(0).Max();
        if (_sideSelectContext is null ||
            _sideSelectRevision != _geometryRevision ||
            _sideSelectSequence != newest)
        {
            RefreshSideSelectAfterGeometryChange();
            if (!_sideSelectActive)
                return;
        }

        if (!_sideSelectContext!.TrySelect(
            _geometry.Features, e.WorldPoint, out var result, out var error))
        {
            UpdateStatus(error);
            return;
        }

        if (!e.IsAdditive && !e.IsDeselect)
            _selected.Clear();

        if (e.IsDeselect)
        {
            foreach (var line in result.ClickedSide)
                _selected.Remove(line);
        }
        else
        {
            foreach (var line in result.ClickedSide)
                _selected.Add(line);
        }

        EditorSurface.SetSelection(_selected);
        UpdateCutScopeDisplay();
        UpdatePendingGuidePreview();
        UpdateStatus(
            $"Selected {result.ClickedSide.Count:N0} pieces on that side. " +
            $"{result.OnBoundary.Count:N0} lie on the split line and " +
            $"{result.Crossing.Count:N0} cross it; neither kind was included. " +
            "Press Delete selected pieces to remove them.");

        if (!allowRepairPrompt || result.Crossing.Count == 0)
            return;

        var choice = System.Windows.MessageBox.Show(this,
            $"{result.Crossing.Count:N0} line(s) cross the split line but weren't split. " +
            "Split them now along the split line, extended to the map edge?",
            "Split crossing lines", MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice != MessageBoxResult.Yes)
            return;

        if (RepairCrossingLines(result.Crossing))
        {
            RefreshSideSelectAfterGeometryChange();
            if (_sideSelectActive)
                HandleSideSelectClick(e, allowRepairPrompt: false);
        }
    }

    private bool RepairCrossingLines(IReadOnlyList<XElement> crossingLines)
    {
        var valid = crossingLines
            .Where(line => line.Document == _workingDocument)
            .ToHashSet();
        if (valid.Count == 0)
            return false;

        var paths = _sideSelectOpenBoundary is null
            ? _sideSelectClosedLoops
            : new List<IReadOnlyList<Point>> { _sideSelectOpenBoundary.ExtendedPath };

        var snapshot = CaptureUndoState(UndoActionKind.RepairSplit);
        var totalSplits = 0;
        try
        {
            ClearCutTargetMarkers();
            foreach (var feature in _geometry.Features)
            {
                if (valid.Contains(feature.LineStringElement))
                    feature.LineStringElement.SetAttributeValue(
                        CutTargetMarkerAttribute, "true");
            }

            foreach (var path in paths)
                totalSplits += _geometry.SplitWithStroke(
                    path, CutTargetMarkerAttribute).LinesSplit;

            if (totalSplits == 0)
            {
                UpdateStatus("The extra split made no changes; no undo step was added.");
                return false;
            }

            PushDocumentUndo(snapshot);
            MarkGeometryChanged();
            _selected.Clear();
            RefreshSurface();
            UpdateCutScopeDisplay();
            UpdateStatus($"Split {totalSplits:N0} crossing line(s). Selection updated.");
            return true;
        }
        catch (Exception exception)
        {
            _workingDocument = new XDocument(snapshot.Document);
            _geometry = new KmlLineGeometryService(
                _workingDocument, _projectionScale);
            _selected.Clear();
            RefreshSurface();
            UpdateCutScopeDisplay();
            UpdateStatus($"Extra split failed and was rolled back: {exception.Message}");
            return false;
        }
        finally
        {
            ClearCutTargetMarkers();
        }
    }
}
