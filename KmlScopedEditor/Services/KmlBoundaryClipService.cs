using System.Windows;
using System.Xml.Linq;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Vector = System.Windows.Vector;

namespace KmlScopedEditor.Services;

/// <summary>
/// Geometry helpers for clipping editable KML lines to an open or closed
/// boundary. All calculations use the Line Editor's projected world
/// coordinates, where one degree is approximately 111.32 km.
/// </summary>
public static class KmlBoundaryClipService
{
    public const double MetresPerWorldUnit = 111320d;
    public const double BoundaryToleranceMetres = 1d;

    public static double WorldUnitsFromMetres(double metres) =>
        Math.Max(0d, metres) / MetresPerWorldUnit;

    public static Rect BuildClipBounds(
        Rect geometryBounds,
        IReadOnlyList<Point> boundary)
    {
        var bounds = geometryBounds;

        foreach (var point in boundary)
        {
            if (bounds.IsEmpty)
                bounds = new Rect(point, point);
            else
                bounds.Union(point);
        }

        if (bounds.IsEmpty)
            return Rect.Empty;

        var span = Math.Max(
            Math.Max(bounds.Width, bounds.Height),
            WorldUnitsFromMetres(100d));

        var margin = Math.Max(
            span * 0.20d,
            WorldUnitsFromMetres(25d));

        bounds.Inflate(margin, margin);
        return bounds;
    }

    public static bool TryPrepareOpenBoundary(
        IReadOnlyList<Point> points,
        Rect geometryBounds,
        double directionSampleDistance,
        out PreparedOpenBoundary? prepared,
        out string error)
    {
        prepared = null;
        error = string.Empty;

        if (points.Count < 2)
        {
            error =
                "This line doesn’t divide the map. Draw a simple line across the area, or close it into a loop.";
            return false;
        }

        var cleaned = RemoveAdjacentDuplicates(points);

        if (cleaned.Count < 2 ||
            PolylineSelfIntersects(cleaned))
        {
            error =
                "This line doesn’t divide the map. Draw a simple line across the area, or close it into a loop.";
            return false;
        }

        var bounds = BuildClipBounds(
            geometryBounds,
            cleaned);

        if (bounds.IsEmpty)
        {
            error =
                "This line doesn’t divide the map. Draw a simple line across the area, or close it into a loop.";
            return false;
        }

        var totalLength = PolylineLength(cleaned);

        if (totalLength <= double.Epsilon)
        {
            error =
                "This line doesn’t divide the map. Draw a simple line across the area, or close it into a loop.";
            return false;
        }

        var sampleDistance = Math.Clamp(
            directionSampleDistance,
            Math.Max(totalLength * 0.01d, double.Epsilon),
            Math.Max(totalLength * 0.35d, double.Epsilon));

        var startSample =
            PointAtDistanceFromStart(
                cleaned,
                sampleDistance);

        var endSample =
            PointAtDistanceFromEnd(
                cleaned,
                sampleDistance);

        var startDirection =
            cleaned[0] - startSample;

        var endDirection =
            cleaned[^1] - endSample;

        if (!TryRayToRect(
                cleaned[0],
                startDirection,
                bounds,
                out var startExit) ||
            !TryRayToRect(
                cleaned[^1],
                endDirection,
                bounds,
                out var endExit) ||
            (startExit - endExit).Length <=
                Math.Max(
                    bounds.Width,
                    bounds.Height) *
                0.000000001d)
        {
            error =
                "This line doesn’t divide the map. Draw a simple line across the area, or close it into a loop.";
            return false;
        }

        var extended = new List<Point>(
            cleaned.Count + 2)
        {
            startExit
        };

        if ((cleaned[0] - startExit).Length >
            double.Epsilon)
        {
            extended.AddRange(cleaned);
        }
        else
        {
            extended.AddRange(
                cleaned.Skip(1));
        }

        if ((extended[^1] - endExit).Length >
            double.Epsilon)
        {
            extended.Add(endExit);
        }

        extended =
            RemoveAdjacentDuplicates(
                extended);

        if (extended.Count < 2 ||
            PolylineSelfIntersects(
                extended))
        {
            error =
                "This line doesn’t divide the map. Draw a simple line across the area, or close it into a loop.";
            return false;
        }

        var clockwise =
            RectangleBoundaryPathClockwise(
                endExit,
                startExit,
                bounds);

        var counterClockwise =
            RectangleBoundaryPathClockwise(
                startExit,
                endExit,
                bounds)
            .AsEnumerable()
            .Reverse()
            .ToList();

        var regionA =
            extended
                .Concat(
                    clockwise.Skip(1))
                .ToList();

        var regionB =
            extended
                .Concat(
                    counterClockwise.Skip(1))
                .ToList();

        if (regionA.Count < 3 ||
            regionB.Count < 3)
        {
            error =
                "This line doesn’t divide the map. Draw a simple line across the area, or close it into a loop.";
            return false;
        }

        prepared =
            new PreparedOpenBoundary(
                extended,
                regionA,
                regionB,
                new[]
                {
                    (IReadOnlyList<Point>)
                        new[]
                        {
                            startExit,
                            cleaned[0]
                        },
                    (IReadOnlyList<Point>)
                        new[]
                        {
                            cleaned[^1],
                            endExit
                        }
                },
                bounds);

        return true;
    }

    public static ClipClassificationResult
        ClassifyClosed(
            IEnumerable<KmlLineFeature> features,
            IReadOnlyList<IReadOnlyList<Point>> loops,
            bool keepInside,
            XName? targetMarkerAttribute = null,
            double boundaryToleranceMetres =
                BoundaryToleranceMetres)
    {
        var validLoops =
            loops
                .Where(loop =>
                    loop.Count >= 4)
                .ToList();

        var boundarySegments =
            validLoops
                .SelectMany(
                    BuildSegments)
                .ToList();

        return Classify(
            features,
            boundarySegments,
            featurePoint =>
            {
                var insideAny =
                    validLoops.Any(
                        loop =>
                            PointInPolygonOrOnBoundary(
                                featurePoint,
                                loop));

                return keepInside
                    ? insideAny
                    : !insideAny;
            },
            targetMarkerAttribute,
            boundaryToleranceMetres);
    }

    public static bool TryClassifyOpen(
        IEnumerable<KmlLineFeature> features,
        PreparedOpenBoundary boundary,
        Point keepPoint,
        XName? targetMarkerAttribute,
        out ClipClassificationResult result,
        out bool keepRegionA,
        out string error,
        double boundaryToleranceMetres =
            BoundaryToleranceMetres)
    {
        result =
            new ClipClassificationResult(
                Array.Empty<XElement>(),
                Array.Empty<XElement>());

        keepRegionA = false;
        error = string.Empty;

        var toleranceWorld =
            WorldUnitsFromMetres(
                boundaryToleranceMetres);

        var distanceToDivider =
            PointToPolylineDistance(
                keepPoint,
                boundary.ExtendedPath);

        if (distanceToDivider <=
            toleranceWorld)
        {
            error =
                "Click farther away from the boundary to choose the side to keep.";
            return false;
        }

        var inA =
            PointInPolygonOrOnBoundary(
                keepPoint,
                boundary.RegionA);

        var inB =
            PointInPolygonOrOnBoundary(
                keepPoint,
                boundary.RegionB);

        if (inA == inB)
        {
            error =
                "The clicked point could not identify one side of the boundary. Click clearly on the side you want to keep.";
            return false;
        }

        keepRegionA = inA;
        var keepA = inA;

        var boundarySegments =
            BuildSegments(
                boundary.ExtendedPath)
            .ToList();

        result =
            Classify(
                features,
                boundarySegments,
                point =>
                    keepA
                        ? PointInPolygonOrOnBoundary(
                            point,
                            boundary.RegionA)
                        : PointInPolygonOrOnBoundary(
                            point,
                            boundary.RegionB),
                targetMarkerAttribute,
                boundaryToleranceMetres);

        return true;
    }

    private static ClipClassificationResult
        Classify(
            IEnumerable<KmlLineFeature> features,
            IReadOnlyList<(Point A, Point B)>
                boundarySegments,
            Func<Point, bool> keepPredicate,
            XName? targetMarkerAttribute,
            double boundaryToleranceMetres)
    {
        var kept =
            new List<XElement>();
        var deleted =
            new List<XElement>();

        var toleranceWorld =
            WorldUnitsFromMetres(
                boundaryToleranceMetres);

        foreach (var feature in features)
        {
            if (targetMarkerAttribute is not null &&
                !string.Equals(
                    feature.LineStringElement
                        .Attribute(
                            targetMarkerAttribute)
                        ?.Value,
                    "true",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (feature.WorldPoints.Count < 2)
                continue;

            var representative =
                FindFarthestRepresentativePoint(
                    feature.WorldPoints,
                    boundarySegments,
                    out var farthestDistance);

            // A line/piece that lies effectively on the boundary is retained
            // regardless of which side is being removed.
            var keep =
                farthestDistance <=
                    toleranceWorld ||
                keepPredicate(
                    representative);

            if (keep)
            {
                kept.Add(
                    feature.LineStringElement);
            }
            else
            {
                deleted.Add(
                    feature.LineStringElement);
            }
        }

        return new ClipClassificationResult(
            kept,
            deleted);
    }

    private static Point
        FindFarthestRepresentativePoint(
            IReadOnlyList<Point> line,
            IReadOnlyList<(Point A, Point B)>
                boundarySegments,
            out double farthestDistance)
    {
        var bestPoint =
            line[0];
        var bestDistance = -1d;

        void Consider(Point candidate)
        {
            var distance =
                PointToSegmentsDistance(
                    candidate,
                    boundarySegments);

            if (distance >
                bestDistance)
            {
                bestDistance =
                    distance;
                bestPoint =
                    candidate;
            }
        }

        for (var index = 0;
             index < line.Count;
             index++)
        {
            Consider(line[index]);

            if (index >=
                line.Count - 1)
            {
                continue;
            }

            Consider(
                new Point(
                    (line[index].X +
                     line[index + 1].X) /
                    2d,
                    (line[index].Y +
                     line[index + 1].Y) /
                    2d));
        }

        farthestDistance =
            bestDistance;

        return bestPoint;
    }

    private static List<Point>
        RemoveAdjacentDuplicates(
            IReadOnlyList<Point> points)
    {
        var result =
            new List<Point>(
                points.Count);

        foreach (var point in points)
        {
            if (result.Count == 0 ||
                (result[^1] - point).Length >
                    0.000000000001d)
            {
                result.Add(point);
            }
        }

        return result;
    }

    private static double PolylineLength(
        IReadOnlyList<Point> points)
    {
        var length = 0d;

        for (var index = 0;
             index < points.Count - 1;
             index++)
        {
            length +=
                (points[index + 1] -
                 points[index]).Length;
        }

        return length;
    }

    private static Point
        PointAtDistanceFromStart(
            IReadOnlyList<Point> points,
            double distance)
    {
        var remaining =
            Math.Max(0d, distance);

        for (var index = 0;
             index < points.Count - 1;
             index++)
        {
            var a = points[index];
            var b = points[index + 1];
            var segmentLength =
                (b - a).Length;

            if (segmentLength <=
                double.Epsilon)
            {
                continue;
            }

            if (remaining <=
                segmentLength)
            {
                var t =
                    remaining /
                    segmentLength;

                return new Point(
                    a.X +
                    ((b.X - a.X) * t),
                    a.Y +
                    ((b.Y - a.Y) * t));
            }

            remaining -=
                segmentLength;
        }

        return points[^1];
    }

    private static Point
        PointAtDistanceFromEnd(
            IReadOnlyList<Point> points,
            double distance)
    {
        var reversed =
            points
                .Reverse()
                .ToList();

        return PointAtDistanceFromStart(
            reversed,
            distance);
    }

    private static bool TryRayToRect(
        Point origin,
        Vector direction,
        Rect rect,
        out Point intersection)
    {
        intersection = default;

        var length =
            direction.Length;

        if (length <=
            double.Epsilon)
        {
            return false;
        }

        direction.Normalize();

        var candidates =
            new List<(double T, Point Point)>();

        void Consider(
            double t,
            Point point)
        {
            const double epsilon =
                0.0000000001d;

            if (t <= epsilon)
                return;

            if (point.X >=
                    rect.Left - epsilon &&
                point.X <=
                    rect.Right + epsilon &&
                point.Y >=
                    rect.Top - epsilon &&
                point.Y <=
                    rect.Bottom + epsilon)
            {
                candidates.Add(
                    (t, point));
            }
        }

        if (Math.Abs(direction.X) >
            0.000000000001d)
        {
            var tLeft =
                (rect.Left -
                 origin.X) /
                direction.X;

            Consider(
                tLeft,
                new Point(
                    rect.Left,
                    origin.Y +
                    (tLeft *
                     direction.Y)));

            var tRight =
                (rect.Right -
                 origin.X) /
                direction.X;

            Consider(
                tRight,
                new Point(
                    rect.Right,
                    origin.Y +
                    (tRight *
                     direction.Y)));
        }

        if (Math.Abs(direction.Y) >
            0.000000000001d)
        {
            var tTop =
                (rect.Top -
                 origin.Y) /
                direction.Y;

            Consider(
                tTop,
                new Point(
                    origin.X +
                    (tTop *
                     direction.X),
                    rect.Top));

            var tBottom =
                (rect.Bottom -
                 origin.Y) /
                direction.Y;

            Consider(
                tBottom,
                new Point(
                    origin.X +
                    (tBottom *
                     direction.X),
                    rect.Bottom));
        }

        if (candidates.Count == 0)
            return false;

        intersection =
            candidates
                .OrderBy(item =>
                    item.T)
                .First()
                .Point;

        return true;
    }

    private static List<Point>
        RectangleBoundaryPathClockwise(
            Point from,
            Point to,
            Rect rect)
    {
        var perimeter =
            2d *
            (rect.Width +
             rect.Height);

        var fromPosition =
            RectanglePerimeterPosition(
                from,
                rect);

        var toPosition =
            RectanglePerimeterPosition(
                to,
                rect);

        if (toPosition <=
            fromPosition +
            0.000000000001d)
        {
            toPosition +=
                perimeter;
        }

        var cornerPositions =
            new[]
            {
                0d,
                rect.Width,
                rect.Width +
                    rect.Height,
                (2d * rect.Width) +
                    rect.Height,
                perimeter
            };

        var result =
            new List<Point>
            {
                from
            };

        foreach (var corner in
                 cornerPositions)
        {
            var candidate =
                corner;

            while (candidate <=
                   fromPosition +
                   0.000000000001d)
            {
                candidate +=
                    perimeter;
            }

            if (candidate <
                toPosition -
                0.000000000001d)
            {
                result.Add(
                    PointAtPerimeterPosition(
                        candidate %
                        perimeter,
                        rect));
            }
        }

        result.Add(to);
        return result;
    }

    private static double
        RectanglePerimeterPosition(
            Point point,
            Rect rect)
    {
        const double epsilon =
            0.00000001d;

        if (Math.Abs(
                point.Y -
                rect.Top) <=
            epsilon)
        {
            return Math.Clamp(
                point.X -
                rect.Left,
                0d,
                rect.Width);
        }

        if (Math.Abs(
                point.X -
                rect.Right) <=
            epsilon)
        {
            return rect.Width +
                Math.Clamp(
                    point.Y -
                    rect.Top,
                    0d,
                    rect.Height);
        }

        if (Math.Abs(
                point.Y -
                rect.Bottom) <=
            epsilon)
        {
            return rect.Width +
                rect.Height +
                Math.Clamp(
                    rect.Right -
                    point.X,
                    0d,
                    rect.Width);
        }

        return
            (2d * rect.Width) +
            rect.Height +
            Math.Clamp(
                rect.Bottom -
                point.Y,
                0d,
                rect.Height);
    }

    private static Point
        PointAtPerimeterPosition(
            double position,
            Rect rect)
    {
        var p =
            Math.Max(0d, position);

        if (p <= rect.Width)
        {
            return new Point(
                rect.Left + p,
                rect.Top);
        }

        p -= rect.Width;

        if (p <= rect.Height)
        {
            return new Point(
                rect.Right,
                rect.Top + p);
        }

        p -= rect.Height;

        if (p <= rect.Width)
        {
            return new Point(
                rect.Right - p,
                rect.Bottom);
        }

        p -= rect.Width;

        return new Point(
            rect.Left,
            rect.Bottom -
            Math.Min(
                p,
                rect.Height));
    }

    private static bool
        PolylineSelfIntersects(
            IReadOnlyList<Point> points)
    {
        for (var first = 0;
             first < points.Count - 1;
             first++)
        {
            for (var second =
                     first + 2;
                 second <
                 points.Count - 1;
                 second++)
            {
                if (first == 0 &&
                    second ==
                    points.Count - 2 &&
                    (points[0] -
                     points[^1]).Length <=
                    0.000000000001d)
                {
                    continue;
                }

                if (SegmentsIntersect(
                        points[first],
                        points[first + 1],
                        points[second],
                        points[second + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<
        (Point A, Point B)>
        BuildSegments(
            IReadOnlyList<Point> points)
    {
        for (var index = 0;
             index < points.Count - 1;
             index++)
        {
            yield return (
                points[index],
                points[index + 1]);
        }
    }

    private static double
        PointToSegmentsDistance(
            Point point,
            IReadOnlyList<(Point A, Point B)>
                segments)
    {
        if (segments.Count == 0)
            return double.PositiveInfinity;

        var best =
            double.PositiveInfinity;

        foreach (var segment in
                 segments)
        {
            best = Math.Min(
                best,
                PointToSegmentDistance(
                    point,
                    segment.A,
                    segment.B));
        }

        return best;
    }

    private static double
        PointToPolylineDistance(
            Point point,
            IReadOnlyList<Point> line) =>
        PointToSegmentsDistance(
            point,
            BuildSegments(line).ToList());

    private static double
        PointToSegmentDistance(
            Point point,
            Point a,
            Point b)
    {
        var dx =
            b.X - a.X;
        var dy =
            b.Y - a.Y;

        var lengthSquared =
            (dx * dx) +
            (dy * dy);

        if (lengthSquared <=
            double.Epsilon)
        {
            return
                (point - a).Length;
        }

        var t =
            ((point.X - a.X) *
                 dx +
             (point.Y - a.Y) *
                 dy) /
            lengthSquared;

        t =
            Math.Clamp(
                t,
                0d,
                1d);

        var projection =
            new Point(
                a.X +
                (t * dx),
                a.Y +
                (t * dy));

        return
            (point -
             projection).Length;
    }

    private static bool
        PointInPolygonOrOnBoundary(
            Point point,
            IReadOnlyList<Point> polygon)
    {
        var inside = false;

        for (int index = 0,
                 previous =
                     polygon.Count - 1;
             index < polygon.Count;
             previous = index++)
        {
            var a =
                polygon[previous];
            var b =
                polygon[index];

            if (PointOnSegment(
                    point,
                    a,
                    b))
            {
                return true;
            }

            var crosses =
                (a.Y > point.Y) !=
                (b.Y > point.Y);

            if (!crosses)
                continue;

            var xAtY =
                ((b.X - a.X) *
                 (point.Y - a.Y) /
                 (b.Y - a.Y)) +
                a.X;

            if (point.X < xAtY)
                inside = !inside;
        }

        return inside;
    }

    private static bool
        PointOnSegment(
            Point point,
            Point a,
            Point b)
    {
        const double epsilon =
            0.0000000001d;

        var ab =
            b - a;
        var ap =
            point - a;

        if (Math.Abs(
                Cross(ab, ap)) >
            epsilon)
        {
            return false;
        }

        var dot =
            (ap.X * ab.X) +
            (ap.Y * ab.Y);

        if (dot < -epsilon)
            return false;

        var lengthSquared =
            (ab.X * ab.X) +
            (ab.Y * ab.Y);

        return dot <=
            lengthSquared +
            epsilon;
    }

    private static bool
        SegmentsIntersect(
            Point a,
            Point b,
            Point c,
            Point d)
    {
        const double epsilon =
            0.0000000001d;

        var ab =
            b - a;
        var cd =
            d - c;
        var ac =
            c - a;

        var denominator =
            Cross(ab, cd);

        if (Math.Abs(denominator) <=
            epsilon)
        {
            return
                PointOnSegment(a, c, d) ||
                PointOnSegment(b, c, d) ||
                PointOnSegment(c, a, b) ||
                PointOnSegment(d, a, b);
        }

        var t =
            Cross(ac, cd) /
            denominator;

        var u =
            Cross(ac, ab) /
            denominator;

        return t >= -epsilon &&
               t <= 1d + epsilon &&
               u >= -epsilon &&
               u <= 1d + epsilon;
    }

    private static double Cross(
        Vector a,
        Vector b) =>
        (a.X * b.Y) -
        (a.Y * b.X);
}

public sealed record PreparedOpenBoundary(
    IReadOnlyList<Point> ExtendedPath,
    IReadOnlyList<Point> RegionA,
    IReadOnlyList<Point> RegionB,
    IReadOnlyList<IReadOnlyList<Point>> ExtensionSegments,
    Rect Bounds);

public sealed record ClipClassificationResult(
    IReadOnlyList<XElement> KeptLines,
    IReadOnlyList<XElement> DeletedLines);
