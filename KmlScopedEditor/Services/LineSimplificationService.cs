using System.Threading;

namespace KmlScopedEditor.Services;

/// <summary>
/// Pure geometry engine for line simplification previews. It never mutates KML,
/// WPF state, or the input coordinate collections.
/// </summary>
public sealed class LineSimplificationService
{
    private readonly object _baselineGate =
        new();

    private BaselineCache? _baselineCache;

    private const double ClosedToleranceDegrees = 0.000000001d;
    private const int OutputDecimalPlaces = 8;
    private const double TopologyGridTargetMetres = 40d;
    private const double TopologyLocationToleranceMetres = 0.001d;
    private const double GeometryEpsilonMetres = 0.000001d;

    public LineSimplificationPreview Preview(
        IReadOnlyList<LineSimplificationInput> lines,
        LineSimplificationOptions options,
        long geometryRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (!double.IsFinite(options.ToleranceMetres) ||
            options.ToleranceMetres < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Simplification tolerance must be zero or a positive finite number.");
        }

        var usableLines =
            lines
                .Where(line =>
                    line.Coordinates is not null &&
                    line.Coordinates.Count >= 2)
                .ToList();

        if (usableLines.Count == 0)
        {
            return new LineSimplificationPreview(
                geometryRevision,
                0,
                0,
                0,
                0,
                0d,
                Array.Empty<LineSimplificationReplacement>(),
                Array.Empty<LineTopologyIssue>());
        }

        var targetLines =
            usableLines
                .Where(line =>
                    line.IsTarget)
                .ToList();

        if (targetLines.Count == 0)
        {
            return new LineSimplificationPreview(
                geometryRevision,
                0,
                0,
                0,
                0,
                0d,
                Array.Empty<LineSimplificationReplacement>(),
                Array.Empty<LineTopologyIssue>());
        }

        if (options.ToleranceMetres <= 0d &&
            !options.Smooth)
        {
            var noOpReplacements =
                targetLines
                    .Select(line =>
                        new LineSimplificationReplacement(
                            line.FeatureIndex,
                            line.Coordinates,
                            false))
                    .ToList();

            var pointCount =
                targetLines.Sum(line =>
                    line.Coordinates.Count);

            return new LineSimplificationPreview(
                geometryRevision,
                targetLines.Count,
                0,
                pointCount,
                pointCount,
                0d,
                noOpReplacements,
                Array.Empty<LineTopologyIssue>());
        }

        var baseline =
            GetOrCreateBaseline(
                usableLines,
                geometryRevision,
                cancellationToken);

        var projection =
            baseline.Projection;

        var replacements =
            new List<LineSimplificationReplacement>();
        var changedFeatureIndices =
            new HashSet<int>();

        var targetLineCount = 0;
        var changedLineCount = 0;
        var originalPointCount = 0;
        var resultPointCount = 0;
        var maximumDeviationMetres = 0d;

        foreach (var line in usableLines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!line.IsTarget)
                continue;

            targetLineCount++;
            originalPointCount +=
                line.Coordinates.Count;

            var transformed =
                TransformLine(
                    line.Coordinates,
                    options,
                    projection);

            resultPointCount +=
                transformed.Count;

            var changed =
                !CoordinatesExactlyEqual(
                    line.Coordinates,
                    transformed);

            if (changed)
            {
                changedLineCount++;
                changedFeatureIndices.Add(
                    line.FeatureIndex);

                maximumDeviationMetres =
                    Math.Max(
                        maximumDeviationMetres,
                        MeasureSymmetricDeviation(
                            line.Coordinates,
                            transformed,
                            projection,
                            cancellationToken));
            }

            replacements.Add(
                new LineSimplificationReplacement(
                    line.FeatureIndex,
                    transformed,
                    changed));
        }

        IReadOnlyList<LineTopologyIssue> newIssues =
            Array.Empty<LineTopologyIssue>();

        if (changedFeatureIndices.Count > 0)
        {
            var replacementsByIndex =
                replacements
                    .Where(item => item.Changed)
                    .ToDictionary(
                        item => item.FeatureIndex,
                        item => item.Coordinates);

            var afterLines =
                BuildMetricLinesFromBaseline(
                    baseline.Lines,
                    replacementsByIndex,
                    projection);

            var beforeIssues =
                baseline.Issues
                    .Where(issue =>
                        changedFeatureIndices.Contains(
                            issue.FirstFeatureIndex) ||
                        changedFeatureIndices.Contains(
                            issue.SecondFeatureIndex))
                    .ToList();

            var afterIssues =
                FindTopologyIssues(
                    afterLines,
                    changedFeatureIndices,
                    cancellationToken);

            newIssues =
                FindNewTopologyIssues(
                    beforeIssues,
                    afterIssues,
                    projection);
        }

        return new LineSimplificationPreview(
            geometryRevision,
            targetLineCount,
            changedLineCount,
            originalPointCount,
            resultPointCount,
            maximumDeviationMetres,
            replacements,
            newIssues);
    }

    private BaselineCache GetOrCreateBaseline(
        IReadOnlyList<LineSimplificationInput> lines,
        long geometryRevision,
        CancellationToken cancellationToken)
    {
        var totalPointCount =
            lines.Sum(line =>
                line.Coordinates.Count);

        lock (_baselineGate)
        {
            if (_baselineCache is not null &&
                _baselineCache.GeometryRevision ==
                    geometryRevision &&
                _baselineCache.FeatureCount ==
                    lines.Count &&
                _baselineCache.TotalPointCount ==
                    totalPointCount)
            {
                return _baselineCache;
            }
        }

        var projection =
            MetricProjection.Create(
                lines.SelectMany(line =>
                    line.Coordinates));

        var metricLines =
            BuildMetricLines(
                lines,
                null,
                projection);

        var allFeatureIndices =
            metricLines
                .Select(line =>
                    line.FeatureIndex)
                .ToHashSet();

        var issues =
            FindTopologyIssues(
                metricLines,
                allFeatureIndices,
                cancellationToken);

        var created =
            new BaselineCache(
                geometryRevision,
                lines.Count,
                totalPointCount,
                projection,
                metricLines,
                issues);

        lock (_baselineGate)
        {
            if (_baselineCache is null ||
                _baselineCache.GeometryRevision <=
                    geometryRevision)
            {
                _baselineCache =
                    created;
            }

            if (_baselineCache.GeometryRevision ==
                    geometryRevision &&
                _baselineCache.FeatureCount ==
                    lines.Count &&
                _baselineCache.TotalPointCount ==
                    totalPointCount)
            {
                return _baselineCache;
            }
        }

        return created;
    }

    private static IReadOnlyList<MetricLine>
        BuildMetricLinesFromBaseline(
            IReadOnlyList<MetricLine> baselineLines,
            IReadOnlyDictionary<
                int,
                IReadOnlyList<KmlCoordinate>>
                replacements,
            MetricProjection projection)
    {
        var result =
            new List<MetricLine>(
                baselineLines.Count);

        foreach (var baselineLine in
                 baselineLines)
        {
            if (!replacements.TryGetValue(
                    baselineLine.FeatureIndex,
                    out var coordinates))
            {
                result.Add(
                    baselineLine);
                continue;
            }

            result.Add(
                new MetricLine(
                    baselineLine.FeatureIndex,
                    coordinates
                        .Select(
                            projection.Project)
                        .ToList(),
                    IsClosed(
                        coordinates)));
        }

        return result;
    }

    private static IReadOnlyList<KmlCoordinate>
        TransformLine(
            IReadOnlyList<KmlCoordinate> original,
            LineSimplificationOptions options,
            MetricProjection projection)
    {
        if (original.Count < 2 ||
            (options.ToleranceMetres <= 0d &&
             !options.Smooth))
        {
            return original;
        }

        var closed =
            IsClosed(original);

        var sourceCount =
            closed
                ? original.Count - 1
                : original.Count;

        if (sourceCount < 2)
            return original;

        var source =
            original
                .Take(sourceCount)
                .ToList();

        var transformed =
            source;

        if (options.ToleranceMetres > 0d &&
            source.Count > 2)
        {
            var metric =
                source
                    .Select(projection.Project)
                    .ToList();

            var keepIndices =
                DouglasPeuckerIndices(
                    metric,
                    options.ToleranceMetres);

            if (closed &&
                keepIndices.Count < 3)
            {
                keepIndices =
                    Enumerable.Range(
                            0,
                            source.Count)
                        .ToList();
            }

            transformed =
                keepIndices
                    .Select(index =>
                        source[index])
                    .ToList();
        }

        if (closed)
        {
            if (transformed.Count < 3)
                return original;

            transformed.Add(
                transformed[0]);
        }

        if (options.Smooth)
        {
            transformed =
                Chaikin(
                    transformed,
                    closed);

            transformed =
                Chaikin(
                    transformed,
                    closed);
        }

        transformed =
            QuantizeForOutput(
                transformed,
                original,
                closed);

        if (closed)
        {
            var distinctCount =
                Math.Max(
                    0,
                    transformed.Count - 1);

            if (distinctCount < 3)
                return original;
        }
        else if (transformed.Count < 2)
        {
            return original;
        }

        return transformed;
    }

    private static List<int>
        DouglasPeuckerIndices(
            IReadOnlyList<MetricPoint> points,
            double toleranceMetres)
    {
        if (points.Count <= 2 ||
            toleranceMetres <= 0d)
        {
            return Enumerable
                .Range(0, points.Count)
                .ToList();
        }

        var keep =
            new bool[points.Count];

        keep[0] = true;
        keep[^1] = true;

        var stack =
            new Stack<(int Start, int End)>();

        stack.Push(
            (0, points.Count - 1));

        var toleranceSquared =
            toleranceMetres *
            toleranceMetres;

        while (stack.Count > 0)
        {
            var (start, end) =
                stack.Pop();

            if (end <= start + 1)
                continue;

            var maxDistanceSquared =
                -1d;
            var maxIndex =
                -1;

            for (var index = start + 1;
                 index < end;
                 index++)
            {
                var distanceSquared =
                    PointToSegmentDistanceSquared(
                        points[index],
                        points[start],
                        points[end]);

                if (distanceSquared >
                    maxDistanceSquared)
                {
                    maxDistanceSquared =
                        distanceSquared;
                    maxIndex = index;
                }
            }

            if (maxIndex < 0 ||
                maxDistanceSquared <=
                toleranceSquared)
            {
                continue;
            }

            keep[maxIndex] = true;

            stack.Push(
                (start, maxIndex));
            stack.Push(
                (maxIndex, end));
        }

        var result =
            new List<int>();

        for (var index = 0;
             index < keep.Length;
             index++)
        {
            if (keep[index])
                result.Add(index);
        }

        return result;
    }

    private static List<KmlCoordinate>
        Chaikin(
            IReadOnlyList<KmlCoordinate> points,
            bool closed)
    {
        if (closed)
        {
            var distinct =
                points.Count > 1
                    ? points.Take(
                            points.Count - 1)
                        .ToList()
                    : points.ToList();

            if (distinct.Count < 3)
                return points.ToList();

            var result =
                new List<KmlCoordinate>(
                    (distinct.Count * 2) + 1);

            for (var index = 0;
                 index < distinct.Count;
                 index++)
            {
                var a =
                    distinct[index];
                var b =
                    distinct[
                        (index + 1) %
                        distinct.Count];

                result.Add(
                    Interpolate(
                        a,
                        b,
                        0.25d));
                result.Add(
                    Interpolate(
                        a,
                        b,
                        0.75d));
            }

            result.Add(
                result[0]);

            return result;
        }

        if (points.Count <= 2)
            return points.ToList();

        var openResult =
            new List<KmlCoordinate>(
                points.Count * 2);

        openResult.Add(
            points[0]);

        for (var index = 0;
             index < points.Count - 1;
             index++)
        {
            openResult.Add(
                Interpolate(
                    points[index],
                    points[index + 1],
                    0.25d));

            openResult.Add(
                Interpolate(
                    points[index],
                    points[index + 1],
                    0.75d));
        }

        openResult.Add(
            points[^1]);

        return openResult;
    }

    private static KmlCoordinate Interpolate(
        KmlCoordinate a,
        KmlCoordinate b,
        double t)
    {
        var altitude =
            a.Altitude.HasValue &&
            b.Altitude.HasValue
                ? a.Altitude.Value +
                  ((b.Altitude.Value -
                    a.Altitude.Value) * t)
                : a.Altitude ??
                  b.Altitude;

        return new KmlCoordinate(
            a.Longitude +
            ((b.Longitude -
              a.Longitude) * t),
            a.Latitude +
            ((b.Latitude -
              a.Latitude) * t),
            altitude);
    }

    private static List<KmlCoordinate>
        QuantizeForOutput(
            IReadOnlyList<KmlCoordinate> points,
            IReadOnlyList<KmlCoordinate> original,
            bool closed)
    {
        if (points.Count == 0)
            return new List<KmlCoordinate>();

        if (closed)
        {
            var distinct =
                points.Count > 1
                    ? points.Take(
                            points.Count - 1)
                        .Select(QuantizeCoordinate)
                        .ToList()
                    : points
                        .Select(QuantizeCoordinate)
                        .ToList();

            distinct =
                RemoveConsecutiveSamePlanarPoints(
                    distinct);

            while (distinct.Count > 1 &&
                   SamePlanarPoint(
                       distinct[0],
                       distinct[^1]))
            {
                distinct.RemoveAt(
                    distinct.Count - 1);
            }

            if (distinct.Count < 3)
                return original.ToList();

            distinct.Add(
                distinct[0]);

            return distinct;
        }

        var result =
            new List<KmlCoordinate>(
                points.Count);

        for (var index = 0;
             index < points.Count;
             index++)
        {
            if (index == 0)
            {
                result.Add(
                    original[0]);
            }
            else if (index ==
                     points.Count - 1)
            {
                result.Add(
                    original[^1]);
            }
            else
            {
                result.Add(
                    QuantizeCoordinate(
                        points[index]));
            }
        }

        result =
            RemoveConsecutiveSamePlanarPoints(
                result);

        if (result.Count < 2)
            return original.ToList();

        result[0] =
            original[0];
        result[^1] =
            original[^1];

        return result;
    }

    private static KmlCoordinate
        QuantizeCoordinate(
            KmlCoordinate coordinate)
    {
        return new KmlCoordinate(
            Math.Round(
                coordinate.Longitude,
                OutputDecimalPlaces,
                MidpointRounding.AwayFromZero),
            Math.Round(
                coordinate.Latitude,
                OutputDecimalPlaces,
                MidpointRounding.AwayFromZero),
            coordinate.Altitude);
    }

    private static List<KmlCoordinate>
        RemoveConsecutiveSamePlanarPoints(
            IEnumerable<KmlCoordinate> points)
    {
        var result =
            new List<KmlCoordinate>();

        foreach (var point in points)
        {
            if (result.Count == 0 ||
                !SamePlanarPoint(
                    result[^1],
                    point))
            {
                result.Add(point);
            }
        }

        return result;
    }

    private static bool SamePlanarPoint(
        KmlCoordinate a,
        KmlCoordinate b)
    {
        return a.Longitude == b.Longitude &&
               a.Latitude == b.Latitude;
    }

    private static bool IsClosed(
        IReadOnlyList<KmlCoordinate> coordinates)
    {
        if (coordinates.Count < 3)
            return false;

        return Math.Abs(
                   coordinates[0].Longitude -
                   coordinates[^1].Longitude) <
               ClosedToleranceDegrees &&
               Math.Abs(
                   coordinates[0].Latitude -
                   coordinates[^1].Latitude) <
               ClosedToleranceDegrees;
    }

    private static bool CoordinatesExactlyEqual(
        IReadOnlyList<KmlCoordinate> left,
        IReadOnlyList<KmlCoordinate> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0;
             index < left.Count;
             index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
    }

    private static double
        MeasureSymmetricDeviation(
            IReadOnlyList<KmlCoordinate> original,
            IReadOnlyList<KmlCoordinate> transformed,
            MetricProjection projection,
            CancellationToken cancellationToken)
    {
        var originalMetric =
            original
                .Select(projection.Project)
                .ToList();

        var transformedMetric =
            transformed
                .Select(projection.Project)
                .ToList();

        var transformedIndex =
            new MetricSegmentGrid(
                transformedMetric);

        var originalIndex =
            new MetricSegmentGrid(
                originalMetric);

        var maximum =
            0d;

        foreach (var point in originalMetric)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            maximum =
                Math.Max(
                    maximum,
                    transformedIndex
                        .DistanceToPolyline(
                            point));
        }

        foreach (var point in transformedMetric)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            maximum =
                Math.Max(
                    maximum,
                    originalIndex
                        .DistanceToPolyline(
                            point));
        }

        return maximum;
    }

    private static IReadOnlyList<MetricLine>
        BuildMetricLines(
            IReadOnlyList<LineSimplificationInput> lines,
            IReadOnlyDictionary<
                int,
                IReadOnlyList<KmlCoordinate>>?
                replacements,
            MetricProjection projection)
    {
        var result =
            new List<MetricLine>(
                lines.Count);

        foreach (var line in lines)
        {
            var coordinates =
                replacements is not null &&
                replacements.TryGetValue(
                    line.FeatureIndex,
                    out var replacement)
                    ? replacement
                    : line.Coordinates;

            result.Add(
                new MetricLine(
                    line.FeatureIndex,
                    coordinates
                        .Select(
                            projection.Project)
                        .ToList(),
                    IsClosed(coordinates)));
        }

        return result;
    }

    private static IReadOnlyList<MetricTopologyIssue>
        FindTopologyIssues(
            IReadOnlyList<MetricLine> lines,
            IReadOnlySet<int> changedFeatureIndices,
            CancellationToken cancellationToken)
    {
        if (lines.Count == 0 ||
            changedFeatureIndices.Count == 0)
        {
            return Array.Empty<MetricTopologyIssue>();
        }

        var segments =
            new List<TopologySegment>();

        foreach (var line in lines)
        {
            for (var index = 0;
                 index < line.Points.Count - 1;
                 index++)
            {
                segments.Add(
                    new TopologySegment(
                        segments.Count,
                        line.FeatureIndex,
                        index,
                        line.Points.Count - 1,
                        line.Closed,
                        line.Points[index],
                        line.Points[index + 1]));
            }
        }

        if (segments.Count == 0)
            return Array.Empty<MetricTopologyIssue>();

        var grid =
            new TopologySegmentGrid(
                segments,
                TopologyGridTargetMetres);

        var testedPairs =
            new HashSet<SegmentPairKey>();

        var issues =
            new List<MetricTopologyIssue>();

        foreach (var bucket in grid.Buckets)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            for (var leftIndex = 0;
                 leftIndex < bucket.Count;
                 leftIndex++)
            {
                var left =
                    bucket[leftIndex];

                for (var rightIndex =
                         leftIndex + 1;
                     rightIndex <
                     bucket.Count;
                     rightIndex++)
                {
                    var right =
                        bucket[rightIndex];

                    if (left.Id ==
                        right.Id)
                    {
                        continue;
                    }

                    var pair =
                        SegmentPairKey.Create(
                            left.Id,
                            right.Id);

                    if (!testedPairs.Add(pair))
                        continue;

                    if (!changedFeatureIndices.Contains(
                            left.FeatureIndex) &&
                        !changedFeatureIndices.Contains(
                            right.FeatureIndex))
                    {
                        continue;
                    }

                    var sameLine =
                        left.FeatureIndex ==
                        right.FeatureIndex;

                    if (sameLine &&
                        SegmentsAreAdjacent(
                            left,
                            right))
                    {
                        continue;
                    }

                    if (!BoundsOverlap(
                            left,
                            right))
                    {
                        continue;
                    }

                    var classification =
                        ClassifyIntersection(
                            left,
                            right,
                            sameLine);

                    if (!classification.HasValue)
                        continue;

                    AddTopologyIssueIfNew(
                        issues,
                        new MetricTopologyIssue(
                            classification.Value.Kind,
                            Math.Min(
                                left.FeatureIndex,
                                right.FeatureIndex),
                            Math.Max(
                                left.FeatureIndex,
                                right.FeatureIndex),
                            classification.Value.Location));
                }
            }
        }

        return issues;
    }

    private static IReadOnlyList<LineTopologyIssue>
        FindNewTopologyIssues(
            IReadOnlyList<MetricTopologyIssue> before,
            IReadOnlyList<MetricTopologyIssue> after,
            MetricProjection projection)
    {
        var beforeGroups =
            before
                .GroupBy(
                    issue =>
                        new TopologyGroupKey(
                            issue.FirstFeatureIndex,
                            issue.SecondFeatureIndex,
                            issue.Kind))
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList());

        var newIssues =
            new List<LineTopologyIssue>();

        foreach (var afterGroup in
                 after.GroupBy(
                     issue =>
                         new TopologyGroupKey(
                             issue.FirstFeatureIndex,
                             issue.SecondFeatureIndex,
                             issue.Kind)))
        {
            beforeGroups.TryGetValue(
                afterGroup.Key,
                out var beforeMatches);

            beforeMatches ??=
                new List<MetricTopologyIssue>();

            var remaining =
                afterGroup.ToList();

            foreach (var existing in
                     beforeMatches)
            {
                if (remaining.Count == 0)
                    break;

                var nearestIndex = 0;
                var nearestDistance =
                    double.MaxValue;

                for (var index = 0;
                     index <
                     remaining.Count;
                     index++)
                {
                    var distance =
                        Distance(
                            existing.Location,
                            remaining[index]
                                .Location);

                    if (distance <
                        nearestDistance)
                    {
                        nearestDistance =
                            distance;
                        nearestIndex =
                            index;
                    }
                }

                remaining.RemoveAt(
                    nearestIndex);
            }

            foreach (var issue in remaining)
            {
                newIssues.Add(
                    new LineTopologyIssue(
                        issue.Kind,
                        issue.FirstFeatureIndex,
                        issue.SecondFeatureIndex,
                        projection.Unproject(
                            issue.Location)));
            }
        }

        return newIssues;
    }

    private static void AddTopologyIssueIfNew(
        List<MetricTopologyIssue> issues,
        MetricTopologyIssue candidate)
    {
        var duplicate =
            issues.Any(existing =>
                existing.Kind ==
                    candidate.Kind &&
                existing.FirstFeatureIndex ==
                    candidate.FirstFeatureIndex &&
                existing.SecondFeatureIndex ==
                    candidate.SecondFeatureIndex &&
                Distance(
                    existing.Location,
                    candidate.Location) <=
                TopologyLocationToleranceMetres);

        if (!duplicate)
            issues.Add(candidate);
    }

    private static bool SegmentsAreAdjacent(
        TopologySegment left,
        TopologySegment right)
    {
        var difference =
            Math.Abs(
                left.SegmentIndex -
                right.SegmentIndex);

        if (difference <= 1)
            return true;

        if (!left.Closed ||
            left.FeatureIndex !=
            right.FeatureIndex)
        {
            return false;
        }

        return
            (left.SegmentIndex == 0 &&
             right.SegmentIndex ==
             left.SegmentCount - 1) ||
            (right.SegmentIndex == 0 &&
             left.SegmentIndex ==
             right.SegmentCount - 1);
    }

    private static IntersectionClassification?
        ClassifyIntersection(
            TopologySegment left,
            TopologySegment right,
            bool sameLine)
    {
        var a = left.A;
        var b = left.B;
        var c = right.A;
        var d = right.B;

        var r =
            new MetricPoint(
                b.X - a.X,
                b.Y - a.Y);

        var s =
            new MetricPoint(
                d.X - c.X,
                d.Y - c.Y);

        var rLengthSquared =
            LengthSquared(r);
        var sLengthSquared =
            LengthSquared(s);

        if (rLengthSquared <=
                GeometryEpsilonMetres *
                GeometryEpsilonMetres ||
            sLengthSquared <=
                GeometryEpsilonMetres *
                GeometryEpsilonMetres)
        {
            return null;
        }

        var denominator =
            Cross(r, s);

        var cMinusA =
            new MetricPoint(
                c.X - a.X,
                c.Y - a.Y);

        if (Math.Abs(denominator) <=
            GeometryEpsilonMetres)
        {
            var lineDistance =
                Math.Abs(
                    Cross(
                        cMinusA,
                        r)) /
                Math.Sqrt(
                    rLengthSquared);

            if (lineDistance >
                GeometryEpsilonMetres)
            {
                return null;
            }

            var t0 =
                Dot(cMinusA, r) /
                rLengthSquared;

            var dMinusA =
                new MetricPoint(
                    d.X - a.X,
                    d.Y - a.Y);

            var t1 =
                Dot(dMinusA, r) /
                rLengthSquared;

            var overlapStart =
                Math.Max(
                    0d,
                    Math.Min(
                        t0,
                        t1));

            var overlapEnd =
                Math.Min(
                    1d,
                    Math.Max(
                        t0,
                        t1));

            if (overlapEnd <
                overlapStart)
            {
                return null;
            }

            var overlapLength =
                (overlapEnd -
                 overlapStart) *
                Math.Sqrt(
                    rLengthSquared);

            var locationT =
                Math.Clamp(
                    (overlapStart +
                     overlapEnd) / 2d,
                    0d,
                    1d);

            var location =
                InterpolateMetric(
                    a,
                    b,
                    locationT);

            if (overlapLength >
                TopologyLocationToleranceMetres)
            {
                return new IntersectionClassification(
                    LineTopologyIssueKind.CollinearOverlap,
                    location);
            }

            if (sameLine)
            {
                return new IntersectionClassification(
                    LineTopologyIssueKind.SelfIntersection,
                    location);
            }

            var leftEndpoint =
                NearEndpoint(
                    location,
                    a,
                    b);
            var rightEndpoint =
                NearEndpoint(
                    location,
                    c,
                    d);

            if (leftEndpoint &&
                rightEndpoint)
            {
                return null;
            }

            return new IntersectionClassification(
                LineTopologyIssueKind.Touch,
                location);
        }

        var t =
            Cross(
                cMinusA,
                s) /
            denominator;

        var u =
            Cross(
                cMinusA,
                r) /
            denominator;

        const double parameterEpsilon =
            0.000000001d;

        if (t < -parameterEpsilon ||
            t > 1d + parameterEpsilon ||
            u < -parameterEpsilon ||
            u > 1d + parameterEpsilon)
        {
            return null;
        }

        t =
            Math.Clamp(
                t,
                0d,
                1d);
        u =
            Math.Clamp(
                u,
                0d,
                1d);

        var point =
            InterpolateMetric(
                a,
                b,
                t);

        if (sameLine)
        {
            return new IntersectionClassification(
                LineTopologyIssueKind.SelfIntersection,
                point);
        }

        var tEndpoint =
            t <= parameterEpsilon ||
            t >=
            1d - parameterEpsilon;

        var uEndpoint =
            u <= parameterEpsilon ||
            u >=
            1d - parameterEpsilon;

        if (tEndpoint &&
            uEndpoint)
        {
            return null;
        }

        return new IntersectionClassification(
            tEndpoint || uEndpoint
                ? LineTopologyIssueKind.Touch
                : LineTopologyIssueKind.ProperCrossing,
            point);
    }

    private static bool NearEndpoint(
        MetricPoint point,
        MetricPoint start,
        MetricPoint end)
    {
        return Distance(
                   point,
                   start) <=
               TopologyLocationToleranceMetres ||
               Distance(
                   point,
                   end) <=
               TopologyLocationToleranceMetres;
    }

    private static bool BoundsOverlap(
        TopologySegment left,
        TopologySegment right)
    {
        return left.MinX <= right.MaxX &&
               left.MaxX >= right.MinX &&
               left.MinY <= right.MaxY &&
               left.MaxY >= right.MinY;
    }

    private static double
        PointToSegmentDistanceSquared(
            MetricPoint point,
            MetricPoint start,
            MetricPoint end)
    {
        var dx =
            end.X - start.X;
        var dy =
            end.Y - start.Y;

        var lengthSquared =
            (dx * dx) +
            (dy * dy);

        if (lengthSquared <=
            double.Epsilon)
        {
            var px =
                point.X - start.X;
            var py =
                point.Y - start.Y;

            return
                (px * px) +
                (py * py);
        }

        var t =
            ((point.X - start.X) *
             dx +
             (point.Y - start.Y) *
             dy) /
            lengthSquared;

        t =
            Math.Clamp(
                t,
                0d,
                1d);

        var projectedX =
            start.X +
            (t * dx);
        var projectedY =
            start.Y +
            (t * dy);

        var offsetX =
            point.X -
            projectedX;
        var offsetY =
            point.Y -
            projectedY;

        return
            (offsetX * offsetX) +
            (offsetY * offsetY);
    }

    private static double PointToSegmentDistance(
        MetricPoint point,
        MetricPoint start,
        MetricPoint end)
    {
        return Math.Sqrt(
            PointToSegmentDistanceSquared(
                point,
                start,
                end));
    }

    private static MetricPoint InterpolateMetric(
        MetricPoint a,
        MetricPoint b,
        double t)
    {
        return new MetricPoint(
            a.X +
            ((b.X - a.X) * t),
            a.Y +
            ((b.Y - a.Y) * t));
    }

    private static double Cross(
        MetricPoint a,
        MetricPoint b)
    {
        return
            (a.X * b.Y) -
            (a.Y * b.X);
    }

    private static double Dot(
        MetricPoint a,
        MetricPoint b)
    {
        return
            (a.X * b.X) +
            (a.Y * b.Y);
    }

    private static double LengthSquared(
        MetricPoint point)
    {
        return
            (point.X * point.X) +
            (point.Y * point.Y);
    }

    private static double Distance(
        MetricPoint a,
        MetricPoint b)
    {
        var dx =
            b.X - a.X;
        var dy =
            b.Y - a.Y;

        return Math.Sqrt(
            (dx * dx) +
            (dy * dy));
    }

    private sealed record BaselineCache(
        long GeometryRevision,
        int FeatureCount,
        int TotalPointCount,
        MetricProjection Projection,
        IReadOnlyList<MetricLine> Lines,
        IReadOnlyList<MetricTopologyIssue> Issues);

    private readonly record struct MetricPoint(
        double X,
        double Y);

    private sealed record MetricLine(
        int FeatureIndex,
        IReadOnlyList<MetricPoint> Points,
        bool Closed);

    private sealed record MetricTopologyIssue(
        LineTopologyIssueKind Kind,
        int FirstFeatureIndex,
        int SecondFeatureIndex,
        MetricPoint Location);

    private readonly record struct TopologyGroupKey(
        int FirstFeatureIndex,
        int SecondFeatureIndex,
        LineTopologyIssueKind Kind);

    private readonly record struct IntersectionClassification(
        LineTopologyIssueKind Kind,
        MetricPoint Location);

    private readonly record struct SegmentPairKey(
        int FirstSegmentId,
        int SecondSegmentId)
    {
        public static SegmentPairKey Create(
            int left,
            int right)
        {
            return left <= right
                ? new SegmentPairKey(
                    left,
                    right)
                : new SegmentPairKey(
                    right,
                    left);
        }
    }

    private sealed class TopologySegment
    {
        public TopologySegment(
            int id,
            int featureIndex,
            int segmentIndex,
            int segmentCount,
            bool closed,
            MetricPoint a,
            MetricPoint b)
        {
            Id = id;
            FeatureIndex = featureIndex;
            SegmentIndex = segmentIndex;
            SegmentCount = segmentCount;
            Closed = closed;
            A = a;
            B = b;

            MinX =
                Math.Min(
                    a.X,
                    b.X);
            MaxX =
                Math.Max(
                    a.X,
                    b.X);
            MinY =
                Math.Min(
                    a.Y,
                    b.Y);
            MaxY =
                Math.Max(
                    a.Y,
                    b.Y);
        }

        public int Id { get; }
        public int FeatureIndex { get; }
        public int SegmentIndex { get; }
        public int SegmentCount { get; }
        public bool Closed { get; }
        public MetricPoint A { get; }
        public MetricPoint B { get; }
        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }
    }

    private sealed class TopologySegmentGrid
    {
        private readonly Dictionary<
            long,
            List<TopologySegment>> _buckets =
            new();

        private readonly double _minX;
        private readonly double _minY;
        private readonly double _cellWidth;
        private readonly double _cellHeight;
        private readonly int _columns;
        private readonly int _rows;

        public TopologySegmentGrid(
            IReadOnlyList<TopologySegment> segments,
            double targetCellMetres)
        {
            _minX =
                segments.Min(
                    item => item.MinX);
            _minY =
                segments.Min(
                    item => item.MinY);

            var maxX =
                segments.Max(
                    item => item.MaxX);
            var maxY =
                segments.Max(
                    item => item.MaxY);

            var width =
                Math.Max(
                    maxX - _minX,
                    0.001d);
            var height =
                Math.Max(
                    maxY - _minY,
                    0.001d);

            _columns =
                Math.Clamp(
                    (int)Math.Ceiling(
                        width /
                        Math.Max(
                            targetCellMetres,
                            1d)),
                    1,
                    256);

            _rows =
                Math.Clamp(
                    (int)Math.Ceiling(
                        height /
                        Math.Max(
                            targetCellMetres,
                            1d)),
                    1,
                    256);

            _cellWidth =
                width / _columns;
            _cellHeight =
                height / _rows;

            foreach (var segment in
                     segments)
            {
                var minColumn =
                    ColumnFor(
                        segment.MinX);
                var maxColumn =
                    ColumnFor(
                        segment.MaxX);
                var minRow =
                    RowFor(
                        segment.MinY);
                var maxRow =
                    RowFor(
                        segment.MaxY);

                for (var column =
                         minColumn;
                     column <=
                     maxColumn;
                     column++)
                {
                    for (var row =
                             minRow;
                         row <=
                         maxRow;
                         row++)
                    {
                        var key =
                            Key(
                                column,
                                row);

                        if (!_buckets.TryGetValue(
                                key,
                                out var bucket))
                        {
                            bucket =
                                new List<TopologySegment>();

                            _buckets[key] =
                                bucket;
                        }

                        bucket.Add(
                            segment);
                    }
                }
            }
        }

        public IEnumerable<
            List<TopologySegment>>
            Buckets =>
            _buckets.Values;

        private int ColumnFor(
            double x)
        {
            return Math.Clamp(
                (int)Math.Floor(
                    (x - _minX) /
                    Math.Max(
                        _cellWidth,
                        0.000001d)),
                0,
                _columns - 1);
        }

        private int RowFor(
            double y)
        {
            return Math.Clamp(
                (int)Math.Floor(
                    (y - _minY) /
                    Math.Max(
                        _cellHeight,
                        0.000001d)),
                0,
                _rows - 1);
        }

        private static long Key(
            int column,
            int row)
        {
            return
                ((long)column << 32) |
                (uint)row;
        }
    }

    private sealed class MetricSegmentGrid
    {
        private readonly IReadOnlyList<
            MetricSegment> _segments;

        private readonly Dictionary<
            long,
            List<int>> _buckets =
            new();

        private readonly double _minX;
        private readonly double _minY;
        private readonly double _cellWidth;
        private readonly double _cellHeight;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[] _visitStamp;
        private int _queryStamp;

        public MetricSegmentGrid(
            IReadOnlyList<MetricPoint> points)
        {
            var segments =
                new List<MetricSegment>();

            for (var index = 0;
                 index < points.Count - 1;
                 index++)
            {
                segments.Add(
                    new MetricSegment(
                        points[index],
                        points[index + 1]));
            }

            _segments = segments;
            _visitStamp =
                new int[segments.Count];

            if (segments.Count == 0)
            {
                _minX = 0d;
                _minY = 0d;
                _cellWidth = 1d;
                _cellHeight = 1d;
                _columns = 1;
                _rows = 1;
                return;
            }

            _minX =
                segments.Min(
                    item => item.MinX);
            _minY =
                segments.Min(
                    item => item.MinY);

            var maxX =
                segments.Max(
                    item => item.MaxX);
            var maxY =
                segments.Max(
                    item => item.MaxY);

            var width =
                Math.Max(
                    maxX - _minX,
                    0.001d);
            var height =
                Math.Max(
                    maxY - _minY,
                    0.001d);

            var dimension =
                Math.Clamp(
                    (int)Math.Ceiling(
                        Math.Sqrt(
                            Math.Max(
                                1d,
                                segments.Count /
                                4d))),
                    4,
                    96);

            _columns = dimension;
            _rows = dimension;
            _cellWidth =
                width / _columns;
            _cellHeight =
                height / _rows;

            for (var segmentIndex = 0;
                 segmentIndex <
                 segments.Count;
                 segmentIndex++)
            {
                var segment =
                    segments[
                        segmentIndex];

                var minColumn =
                    ColumnFor(
                        segment.MinX);
                var maxColumn =
                    ColumnFor(
                        segment.MaxX);
                var minRow =
                    RowFor(
                        segment.MinY);
                var maxRow =
                    RowFor(
                        segment.MaxY);

                for (var column =
                         minColumn;
                     column <=
                     maxColumn;
                     column++)
                {
                    for (var row =
                             minRow;
                         row <=
                         maxRow;
                         row++)
                    {
                        var key =
                            Key(
                                column,
                                row);

                        if (!_buckets.TryGetValue(
                                key,
                                out var bucket))
                        {
                            bucket =
                                new List<int>();

                            _buckets[key] =
                                bucket;
                        }

                        bucket.Add(
                            segmentIndex);
                    }
                }
            }
        }

        public double DistanceToPolyline(
            MetricPoint point)
        {
            if (_segments.Count == 0)
                return 0d;

            var column =
                ColumnFor(
                    point.X);
            var row =
                RowFor(
                    point.Y);

            var best =
                double.PositiveInfinity;

            var stamp =
                NextQueryStamp();

            var maxRadius =
                Math.Max(
                    _columns,
                    _rows);

            for (var radius = 0;
                 radius <= maxRadius &&
                 !double.IsFinite(best);
                 radius++)
            {
                var minColumn =
                    Math.Max(
                        0,
                        column - radius);
                var maxColumn =
                    Math.Min(
                        _columns - 1,
                        column + radius);
                var minRow =
                    Math.Max(
                        0,
                        row - radius);
                var maxRow =
                    Math.Min(
                        _rows - 1,
                        row + radius);

                for (var candidateColumn =
                         minColumn;
                     candidateColumn <=
                     maxColumn;
                     candidateColumn++)
                {
                    for (var candidateRow =
                             minRow;
                         candidateRow <=
                         maxRow;
                         candidateRow++)
                    {
                        if (radius > 0 &&
                            candidateColumn >
                                minColumn &&
                            candidateColumn <
                                maxColumn &&
                            candidateRow >
                                minRow &&
                            candidateRow <
                                maxRow)
                        {
                            continue;
                        }

                        EvaluateBucket(
                            candidateColumn,
                            candidateRow,
                            point,
                            stamp,
                            ref best);
                    }
                }
            }

            if (!double.IsFinite(best))
                return 0d;

            var searchMinColumn =
                ColumnFor(
                    point.X - best);
            var searchMaxColumn =
                ColumnFor(
                    point.X + best);
            var searchMinRow =
                RowFor(
                    point.Y - best);
            var searchMaxRow =
                RowFor(
                    point.Y + best);

            for (var candidateColumn =
                     searchMinColumn;
                 candidateColumn <=
                 searchMaxColumn;
                 candidateColumn++)
            {
                for (var candidateRow =
                         searchMinRow;
                     candidateRow <=
                     searchMaxRow;
                     candidateRow++)
                {
                    EvaluateBucket(
                        candidateColumn,
                        candidateRow,
                        point,
                        stamp,
                        ref best);
                }
            }

            return best;
        }

        private void EvaluateBucket(
            int column,
            int row,
            MetricPoint point,
            int stamp,
            ref double best)
        {
            if (!_buckets.TryGetValue(
                    Key(
                        column,
                        row),
                    out var bucket))
            {
                return;
            }

            foreach (var segmentIndex in
                     bucket)
            {
                if (_visitStamp[
                        segmentIndex] ==
                    stamp)
                {
                    continue;
                }

                _visitStamp[
                    segmentIndex] =
                    stamp;

                var segment =
                    _segments[
                        segmentIndex];

                best =
                    Math.Min(
                        best,
                        PointToSegmentDistance(
                            point,
                            segment.A,
                            segment.B));
            }
        }

        private int NextQueryStamp()
        {
            if (_queryStamp ==
                int.MaxValue)
            {
                Array.Clear(
                    _visitStamp,
                    0,
                    _visitStamp.Length);

                _queryStamp = 0;
            }

            _queryStamp++;
            return _queryStamp;
        }

        private int ColumnFor(
            double x)
        {
            return Math.Clamp(
                (int)Math.Floor(
                    (x - _minX) /
                    Math.Max(
                        _cellWidth,
                        0.000001d)),
                0,
                _columns - 1);
        }

        private int RowFor(
            double y)
        {
            return Math.Clamp(
                (int)Math.Floor(
                    (y - _minY) /
                    Math.Max(
                        _cellHeight,
                        0.000001d)),
                0,
                _rows - 1);
        }

        private static long Key(
            int column,
            int row)
        {
            return
                ((long)column << 32) |
                (uint)row;
        }

        private readonly record struct MetricSegment(
            MetricPoint A,
            MetricPoint B)
        {
            public double MinX =>
                Math.Min(
                    A.X,
                    B.X);

            public double MaxX =>
                Math.Max(
                    A.X,
                    B.X);

            public double MinY =>
                Math.Min(
                    A.Y,
                    B.Y);

            public double MaxY =>
                Math.Max(
                    A.Y,
                    B.Y);
        }
    }

    private readonly record struct MetricProjection(
        double ReferenceLongitude,
        double ReferenceLatitude,
        double MetresPerLongitudeDegree,
        double MetresPerLatitudeDegree)
    {
        public static MetricProjection Create(
            IEnumerable<KmlCoordinate> coordinates)
        {
            var count = 0L;
            var longitudeTotal = 0d;
            var latitudeTotal = 0d;

            foreach (var point in coordinates)
            {
                count++;
                longitudeTotal +=
                    point.Longitude;
                latitudeTotal +=
                    point.Latitude;
            }

            if (count == 0)
            {
                return new MetricProjection(
                    0d,
                    0d,
                    111319.49d,
                    111319.49d);
            }

            var referenceLongitude =
                longitudeTotal / count;

            var referenceLatitude =
                latitudeTotal / count;

            var latitudeRadians =
                referenceLatitude *
                Math.PI / 180d;

            var metresPerLatitudeDegree =
                111132.92d -
                (559.82d *
                 Math.Cos(
                     2d * latitudeRadians)) +
                (1.175d *
                 Math.Cos(
                     4d * latitudeRadians)) -
                (0.0023d *
                 Math.Cos(
                     6d * latitudeRadians));

            var metresPerLongitudeDegree =
                (111412.84d *
                 Math.Cos(
                     latitudeRadians)) -
                (93.5d *
                 Math.Cos(
                     3d * latitudeRadians)) +
                (0.118d *
                 Math.Cos(
                     5d * latitudeRadians));

            if (Math.Abs(
                    metresPerLongitudeDegree) <
                0.000001d)
            {
                metresPerLongitudeDegree =
                    0.000001d;
            }

            if (Math.Abs(
                    metresPerLatitudeDegree) <
                0.000001d)
            {
                metresPerLatitudeDegree =
                    0.000001d;
            }

            return new MetricProjection(
                referenceLongitude,
                referenceLatitude,
                metresPerLongitudeDegree,
                metresPerLatitudeDegree);
        }

        public MetricPoint Project(
            KmlCoordinate coordinate)
        {
            return new MetricPoint(
                (coordinate.Longitude -
                 ReferenceLongitude) *
                MetresPerLongitudeDegree,
                (coordinate.Latitude -
                 ReferenceLatitude) *
                MetresPerLatitudeDegree);
        }

        public KmlCoordinate Unproject(
            MetricPoint point)
        {
            return new KmlCoordinate(
                ReferenceLongitude +
                (point.X /
                 MetresPerLongitudeDegree),
                ReferenceLatitude +
                (point.Y /
                 MetresPerLatitudeDegree),
                null);
        }
    }
}

public readonly record struct LineSimplificationOptions(
    double ToleranceMetres,
    bool Smooth);

public sealed record LineSimplificationInput(
    int FeatureIndex,
    IReadOnlyList<KmlCoordinate> Coordinates,
    bool IsTarget);

public sealed record LineSimplificationReplacement(
    int FeatureIndex,
    IReadOnlyList<KmlCoordinate> Coordinates,
    bool Changed);

public enum LineTopologyIssueKind
{
    ProperCrossing,
    Touch,
    CollinearOverlap,
    SelfIntersection
}

public sealed record LineTopologyIssue(
    LineTopologyIssueKind Kind,
    int FirstFeatureIndex,
    int SecondFeatureIndex,
    KmlCoordinate Location);

public sealed record LineSimplificationPreview(
    long GeometryRevision,
    int TargetLineCount,
    int ChangedLineCount,
    int OriginalPointCount,
    int ResultPointCount,
    double MaximumMeasuredDeviationMetres,
    IReadOnlyList<LineSimplificationReplacement> Replacements,
    IReadOnlyList<LineTopologyIssue> NewIssues)
{
    public int NewCrossingCount =>
        NewIssues.Count(issue =>
            issue.Kind is
                LineTopologyIssueKind.ProperCrossing or
                LineTopologyIssueKind.SelfIntersection);

    public bool HasTopologyWarnings =>
        NewIssues.Count > 0;
}
