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
            Math.Max(totalLength * 0.000001d, double.Epsilon),
            Math.Max(totalLength * 0.45d, double.Epsilon));

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
                .SelectMany(BuildSegments)
                .ToList();
        var polygons = validLoops
            .Select(loop => new PreparedPolygon(loop))
            .ToList();

        return Classify(
            features,
            boundarySegments,
            featurePoint =>
            {
                var insideAny =
                    polygons.Any(polygon => polygon.Contains(featurePoint));

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

        var regionA = new PreparedPolygon(boundary.RegionA);
        var regionB = new PreparedPolygon(boundary.RegionB);
        var inA = regionA.Contains(keepPoint);
        var inB = regionB.Contains(keepPoint);

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
                        ? regionA.Contains(point)
                        : regionB.Contains(point),
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
            WorldUnitsFromMetres(boundaryToleranceMetres);
        var distanceIndex = new BoundarySegmentIndex(boundarySegments);

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
                    distanceIndex,
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

    private static Point FindFarthestRepresentativePoint(
        IReadOnlyList<Point> line,
        BoundarySegmentIndex boundary,
        out double farthestDistance)
    {
        // A successfully split piece stays on one side of the divider.
        // Prefer an obviously off-boundary vertex/segment midpoint;
        // retain the full-distance fallback for pieces close to the divider.
        var farThreshold = WorldUnitsFromMetres(5d);
        var best = line[0];
        var bestDistance = -1d;

        for (var i = 0; i < line.Count; i++)
        {
            var point = line[i];
            if (!boundary.WithinDistance(point, farThreshold))
            {
                farthestDistance = farThreshold;
                return point;
            }

            var distance = boundary.Distance(point);
            if (distance > bestDistance)
            {
                best = point;
                bestDistance = distance;
            }

            if (i == line.Count - 1)
                continue;

            var middle = new Point(
                (point.X + line[i + 1].X) / 2d,
                (point.Y + line[i + 1].Y) / 2d);
            if (!boundary.WithinDistance(middle, farThreshold))
            {
                farthestDistance = farThreshold;
                return middle;
            }

            distance = boundary.Distance(middle);
            if (distance > bestDistance)
            {
                best = middle;
                bestDistance = distance;
            }
        }

        farthestDistance = bestDistance;
        return best;
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

    private static List<Point> RectangleBoundaryPathClockwise(
        Point from, Point to, Rect rect)
    {
        var perimeter = 2d * (rect.Width + rect.Height);
        var fromPosition = RectanglePerimeterPosition(from, rect);
        var toPosition = RectanglePerimeterPosition(to, rect);
        const double epsilon = 1e-12d;

        if (toPosition <= fromPosition + epsilon)
            toPosition += perimeter;

        // 0 and perimeter are the very same corner. Unwrap corner
        // positions FIRST, then sort them by distance along this walk.
        var corners = new[]
        {
            0d,
            rect.Width,
            rect.Width + rect.Height,
            (2d * rect.Width) + rect.Height
        };

        var interiorCorners = corners
            .Select(position =>
                position <= fromPosition + epsilon
                    ? position + perimeter
                    : position)
            .Where(position =>
                position > fromPosition + epsilon &&
                position < toPosition - epsilon)
            .Distinct()
            .OrderBy(position => position);

        var result = new List<Point> { from };
        foreach (var corner in interiorCorners)
            result.Add(PointAtPerimeterPosition(corner % perimeter, rect));

        result.Add(to);
        return RemoveAdjacentDuplicates(result);
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

    public static bool LoopSelfIntersects(IReadOnlyList<Point> loop) =>
        PolylineSelfIntersects(loop);

    private static bool PolylineSelfIntersects(IReadOnlyList<Point> points)
    {
        if (points.Count < 4)
            return false;

        var isClosed = (points[0] - points[^1]).Length <= PointEpsilon;
        var lastIndex = points.Count - 2;
        var segments = Enumerable.Range(0, points.Count - 1)
            .Select(i => new
            {
                Index = i,
                A = points[i],
                B = points[i + 1],
                MinX = Math.Min(points[i].X, points[i + 1].X),
                MaxX = Math.Max(points[i].X, points[i + 1].X),
                MinY = Math.Min(points[i].Y, points[i + 1].Y),
                MaxY = Math.Max(points[i].Y, points[i + 1].Y)
            })
            .OrderBy(item => item.MinX)
            .ToList();

        // Sweep along X instead of testing every segment against every
        // other segment. The active set retains only boxes whose X ranges
        // overlap. Most ordinary 2,000+ vertex contours are near-linear.
        var active = new List<int>();
        for (var i = 0; i < segments.Count; i++)
        {
            var current = segments[i];
            active.RemoveAll(index =>
                segments[index].MaxX < current.MinX - PointEpsilon);

            foreach (var index in active)
            {
                var earlier = segments[index];
                if (Math.Abs(earlier.Index - current.Index) <= 1 ||
                    (isClosed &&
                     ((earlier.Index == 0 && current.Index == lastIndex) ||
                      (current.Index == 0 && earlier.Index == lastIndex))))
                {
                    continue;
                }

                if (earlier.MaxY < current.MinY - PointEpsilon ||
                    current.MaxY < earlier.MinY - PointEpsilon)
                {
                    continue;
                }

                if (SegmentsIntersect(
                    earlier.A, earlier.B, current.A, current.B))
                    return true;
            }

            active.Add(i);
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

    // Indexed even-odd point-in-polygon. A zero-length closing edge
    // never makes the entire map count as on the boundary.
    private sealed class PreparedPolygon
    {
        private readonly List<(Point A, Point B)>[] _buckets;
        private readonly double _minimumY;
        private readonly double _bucketHeight;
        private readonly int _bucketCount;
        private readonly Rect _bounds;

        public PreparedPolygon(IReadOnlyList<Point> polygon)
        {
            var edges = BuildSegments(polygon).ToList();
            if (polygon.Count >= 2 &&
                (polygon[0] - polygon[^1]).LengthSquared >
                    PointEpsilon * PointEpsilon)
            {
                edges.Add((polygon[^1], polygon[0]));
            }

            // Rect is mutable; build locally before assigning the readonly field.
            var bounds = Rect.Empty;
            foreach (var point in polygon)
            {
                if (bounds.IsEmpty)
                    bounds = new Rect(point, point);
                else
                    bounds.Union(point);
            }
            _bounds = bounds;

            _minimumY = _bounds.IsEmpty ? 0d : _bounds.Top;
            _bucketCount = Math.Clamp(edges.Count / 4, 8, 512);
            _bucketHeight = Math.Max(
                _bounds.IsEmpty ? 0d : _bounds.Height / _bucketCount,
                1e-12d);
            _buckets = Enumerable.Range(0, _bucketCount)
                .Select(_ => new List<(Point A, Point B)>())
                .ToArray();

            foreach (var edge in edges)
            {
                var lo = Bucket(Math.Min(edge.A.Y, edge.B.Y));
                var hi = Bucket(Math.Max(edge.A.Y, edge.B.Y));
                for (var i = lo; i <= hi; i++)
                    _buckets[i].Add(edge);
            }
        }

        private int Bucket(double y) =>
            Math.Clamp((int)((y - _minimumY) / _bucketHeight),
                0, _bucketCount - 1);

        public bool Contains(Point point)
        {
            if (_bounds.IsEmpty ||
                point.X < _bounds.Left - PointEpsilon ||
                point.X > _bounds.Right + PointEpsilon ||
                point.Y < _bounds.Top - PointEpsilon ||
                point.Y > _bounds.Bottom + PointEpsilon)
            {
                return false;
            }

            var inside = false;
            foreach (var (a, b) in _buckets[Bucket(point.Y)])
            {
                if (PointOnSegment(point, a, b))
                    return true;

                if ((a.Y > point.Y) == (b.Y > point.Y))
                    continue;

                var xAtY = a.X + (b.X - a.X) *
                    (point.Y - a.Y) / (b.Y - a.Y);
                if (point.X < xAtY)
                    inside = !inside;
            }

            return inside;
        }
    }

    // Approximately one millimetre in projected world coordinates.
    // This geometric predicate tolerance is intentionally independent
    // of the one-metre retention band in Classify().
    private const double PointEpsilon = 1d / MetresPerWorldUnit / 1000d;

    private static bool PointOnSegment(Point point, Point a, Point b) =>
        PointToSegmentDistance(point, a, b) <= PointEpsilon;

    private static bool SegmentsIntersect(
        Point a, Point b, Point c, Point d)
    {
        var ab = b - a;
        var cd = d - c;
        var abLength = ab.Length;
        var cdLength = cd.Length;

        if (abLength <= PointEpsilon)
            return PointOnSegment(a, c, d);
        if (cdLength <= PointEpsilon)
            return PointOnSegment(c, a, b);

        var denominator = Cross(ab, cd);
        if (Math.Abs(denominator) <=
            1e-12d * abLength * cdLength)
        {
            return PointOnSegment(a, c, d) ||
                   PointOnSegment(b, c, d) ||
                   PointOnSegment(c, a, b) ||
                   PointOnSegment(d, a, b);
        }

        var ac = c - a;
        var t = Cross(ac, cd) / denominator;
        var u = Cross(ac, ab) / denominator;

        var tEpsilon = PointEpsilon / abLength;
        var uEpsilon = PointEpsilon / cdLength;
        return t >= -tEpsilon && t <= 1d + tEpsilon &&
               u >= -uEpsilon && u <= 1d + uEpsilon;
    }

    // Binary bounding-box tree: nearest-distance and within-radius queries
    // prune segments that cannot affect the answer. Unlike a fixed-size
    // grid, long segments do not populate thousands of cells.
    private sealed class BoundarySegmentIndex
    {
        private sealed class Node
        {
            public Rect Bounds { get; init; }
            public Node? Left { get; init; }
            public Node? Right { get; init; }
            public (Point A, Point B)[]? Segments { get; init; }
        }

        private readonly Node? _root;

        public BoundarySegmentIndex(
            IReadOnlyList<(Point A, Point B)> segments)
        {
            _root = segments.Count == 0
                ? null
                : Build(segments.ToArray());
        }

        private static Node Build((Point A, Point B)[] segments)
        {
            var minX = segments.Min(s => Math.Min(s.A.X, s.B.X));
            var maxX = segments.Max(s => Math.Max(s.A.X, s.B.X));
            var minY = segments.Min(s => Math.Min(s.A.Y, s.B.Y));
            var maxY = segments.Max(s => Math.Max(s.A.Y, s.B.Y));
            var bounds = new Rect(
                new Point(minX, minY), new Point(maxX, maxY));

            if (segments.Length <= 12)
                return new Node { Bounds = bounds, Segments = segments };

            if (bounds.Width >= bounds.Height)
                Array.Sort(segments, (a, b) =>
                    (a.A.X + a.B.X).CompareTo(b.A.X + b.B.X));
            else
                Array.Sort(segments, (a, b) =>
                    (a.A.Y + a.B.Y).CompareTo(b.A.Y + b.B.Y));

            var middle = segments.Length / 2;
            return new Node
            {
                Bounds = bounds,
                Left = Build(segments[..middle]),
                Right = Build(segments[middle..])
            };
        }

        private static double BoxDistanceSquared(Point point, Rect bounds)
        {
            var dx = Math.Max(0d, Math.Max(
                bounds.Left - point.X, point.X - bounds.Right));
            var dy = Math.Max(0d, Math.Max(
                bounds.Top - point.Y, point.Y - bounds.Bottom));
            return dx * dx + dy * dy;
        }

        public bool WithinDistance(Point point, double distance)
        {
            var squared = distance * distance;
            return _root is not null && AnyWithin(_root, point, squared);
        }

        private static bool AnyWithin(Node node, Point point, double limit)
        {
            if (BoxDistanceSquared(point, node.Bounds) > limit)
                return false;
            if (node.Segments is not null)
                return node.Segments.Any(s =>
                {
                    var d = PointToSegmentDistance(point, s.A, s.B);
                    return d * d <= limit;
                });
            return (node.Left is not null && AnyWithin(node.Left, point, limit)) ||
                   (node.Right is not null && AnyWithin(node.Right, point, limit));
        }

        public double Distance(Point point)
        {
            var bestSquared = double.PositiveInfinity;
            if (_root is not null)
                Nearest(_root, point, ref bestSquared);
            return Math.Sqrt(bestSquared);
        }

        private static void Nearest(
            Node node, Point point, ref double bestSquared)
        {
            if (BoxDistanceSquared(point, node.Bounds) > bestSquared)
                return;

            if (node.Segments is not null)
            {
                foreach (var (a, b) in node.Segments)
                {
                    var distance = PointToSegmentDistance(point, a, b);
                    bestSquared = Math.Min(bestSquared, distance * distance);
                }
                return;
            }

            if (node.Left is null || node.Right is null)
                return;

            var left = BoxDistanceSquared(point, node.Left.Bounds);
            var right = BoxDistanceSquared(point, node.Right.Bounds);
            if (left < right)
            {
                Nearest(node.Left, point, ref bestSquared);
                Nearest(node.Right, point, ref bestSquared);
            }
            else
            {
                Nearest(node.Right, point, ref bestSquared);
                Nearest(node.Left, point, ref bestSquared);
            }
        }
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
