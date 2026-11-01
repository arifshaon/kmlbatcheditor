using System.Windows;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;
using Point = System.Windows.Point;

namespace KmlScopedEditor.Views;

/// <summary>Closure helpers retained from the boundary editor; destructive
/// clipping has moved to Step 3 side selection and ordinary deletion.</summary>
public partial class LineEditorWindow
{
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
                if (ClosePendingGuide(pendingOpen[0]))
                    UpdateStatus("Boundary loop closed.");
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

    private bool ClosePendingGuide(CutGuideState guide)
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
        StartEditingGuide(guide);
        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();
        return true;
    }

}
