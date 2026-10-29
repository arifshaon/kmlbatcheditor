using System.Globalization;
using System.Windows;
using System.Xml.Linq;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Vector = System.Windows.Vector;

namespace KmlScopedEditor.Services;

/// <summary>
/// Reads and edits KML LineString geometry. The editor deliberately uses a
/// lightweight local projection: longitude is scaled by the cosine of the
/// document's mean latitude. This is appropriate for interactive editing of
/// local/regional KML drawings such as contour maps and avoids introducing a
/// GIS dependency.
/// </summary>
public sealed class KmlLineGeometryService
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private const double CoordinateToleranceDegrees = 0.000000001d;
    private const double OpenRingTrimDegrees = 0.0000001d;

    private readonly XDocument _document;
    private readonly double? _fixedLongitudeScale;
    private readonly XName? _scopeMarkerAttribute;
    private readonly Dictionary<long, List<IndexedSegment>> _segmentGrid = new();
    private readonly Dictionary<XElement, List<IndexedSegment>> _segmentsByFeature = new();
    private Rect _indexBounds = Rect.Empty;
    private double _longitudeScale = 1d;
    private int _gridColumns;
    private int _gridRows;
    private double _gridCellWidth = 1d;
    private double _gridCellHeight = 1d;

    public KmlLineGeometryService(
        XDocument document,
        double? fixedLongitudeScale = null,
        XName? scopeMarkerAttribute = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _fixedLongitudeScale = fixedLongitudeScale;
        _scopeMarkerAttribute = scopeMarkerAttribute;
        Reload();
    }

    public double LongitudeScale => _longitudeScale;

    public IReadOnlyList<KmlLineFeature> Features { get; private set; }
        = Array.Empty<KmlLineFeature>();

    public Rect WorldBounds { get; private set; } = Rect.Empty;

    public void Reload()
    {
        var raw = new List<(XElement Line, string Name, List<KmlCoordinate> Coordinates)>();

        foreach (var lineString in _document.Descendants(KmlNs + "LineString"))
        {
            var coordinateElement = lineString.Element(KmlNs + "coordinates");
            if (coordinateElement is null)
                continue;

            var coordinates = ParseCoordinates(coordinateElement.Value);
            if (coordinates.Count < 2)
                continue;

            var placemark = lineString
                .Ancestors(KmlNs + "Placemark")
                .FirstOrDefault();

            if (_scopeMarkerAttribute is not null &&
                !string.Equals(
                    placemark?.Attribute(_scopeMarkerAttribute)?.Value,
                    "true",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var name = placemark?.Element(KmlNs + "name")?.Value?.Trim();
            raw.Add((
                lineString,
                string.IsNullOrWhiteSpace(name) ? "Line" : name,
                coordinates));
        }

        var allCoordinates = raw
            .SelectMany(item => item.Coordinates)
            .ToList();

        if (_fixedLongitudeScale.HasValue)
        {
            _longitudeScale = Math.Abs(_fixedLongitudeScale.Value) < 0.000001d
                ? 0.000001d
                : _fixedLongitudeScale.Value;
        }
        else if (allCoordinates.Count > 0)
        {
            var meanLatitude = allCoordinates.Average(point => point.Latitude);
            _longitudeScale = Math.Cos(meanLatitude * Math.PI / 180d);

            if (Math.Abs(_longitudeScale) < 0.000001d)
                _longitudeScale = 0.000001d;
        }
        else
        {
            _longitudeScale = 1d;
        }

        var features = new List<KmlLineFeature>(raw.Count);
        var bounds = Rect.Empty;

        foreach (var item in raw)
        {
            var worldPoints = item.Coordinates
                .Select(ToWorld)
                .ToList();

            foreach (var point in worldPoints)
            {
                if (bounds.IsEmpty)
                    bounds = new Rect(point, point);
                else
                    bounds.Union(point);
            }

            var featureBounds = BuildBounds(worldPoints);

            features.Add(new KmlLineFeature(
                item.Line,
                item.Name,
                item.Coordinates,
                worldPoints,
                featureBounds));
        }

        Features = features;
        WorldBounds = bounds;
        BuildSpatialIndex();
    }

    public IReadOnlyList<KmlLineFeature> FindIntersectingFeatures(
        IReadOnlyList<Point> stroke)
    {
        if (stroke.Count < 2 || Features.Count == 0)
            return Array.Empty<KmlLineFeature>();

        var matched = new HashSet<XElement>();

        for (var strokeIndex = 0;
             strokeIndex < stroke.Count - 1;
             strokeIndex++)
        {
            var c = stroke[strokeIndex];
            var d = stroke[strokeIndex + 1];
            var bounds = BuildSegmentBounds(c, d);

            foreach (var segment in QuerySegments(bounds))
            {
                if (matched.Contains(segment.Feature.LineStringElement))
                    continue;

                if (SegmentsIntersect(
                        segment.A,
                        segment.B,
                        c,
                        d))
                {
                    matched.Add(segment.Feature.LineStringElement);
                }
            }
        }

        return Features
            .Where(feature =>
                matched.Contains(feature.LineStringElement))
            .ToList();
    }

    public IReadOnlyList<KmlLineFeature> FindFeaturesInPolygon(
        IReadOnlyList<Point> polygon)
    {
        if (polygon.Count < 3 ||
            Features.Count == 0)
        {
            return Array.Empty<KmlLineFeature>();
        }

        var bounds =
            BuildBounds(polygon);

        if (bounds.IsEmpty)
            return Array.Empty<KmlLineFeature>();

        var matched =
            new HashSet<XElement>();

        foreach (var segment in
                 QuerySegments(bounds))
        {
            if (matched.Contains(
                    segment.Feature.LineStringElement))
            {
                continue;
            }

            if (PointInPolygonOrOnBoundary(
                    segment.A,
                    polygon) ||
                PointInPolygonOrOnBoundary(
                    segment.B,
                    polygon) ||
                SegmentCrossesPolygon(
                    segment.A,
                    segment.B,
                    polygon))
            {
                matched.Add(
                    segment.Feature.LineStringElement);
            }
        }

        return Features
            .Where(feature =>
                matched.Contains(
                    feature.LineStringElement))
            .ToList();
    }

    private static bool SegmentCrossesPolygon(
        Point a,
        Point b,
        IReadOnlyList<Point> polygon)
    {
        for (var index = 0;
             index < polygon.Count;
             index++)
        {
            var c = polygon[index];
            var d = polygon[
                (index + 1) %
                polygon.Count];

            if (SegmentsIntersect(
                    a,
                    b,
                    c,
                    d))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointInPolygonOrOnBoundary(
        Point point,
        IReadOnlyList<Point> polygon)
    {
        var inside = false;

        for (int index = 0,
                 previous = polygon.Count - 1;
             index < polygon.Count;
             previous = index++)
        {
            var a = polygon[previous];
            var b = polygon[index];

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

    private static bool PointOnSegment(
        Point point,
        Point a,
        Point b)
    {
        const double epsilon =
            0.0000000001d;

        var ab = b - a;
        var ap = point - a;

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
            lengthSquared + epsilon;
    }

    public KmlLineFeature? FindNearestFeature(
        Point point,
        double tolerance)
    {
        if (Features.Count == 0 || tolerance < 0d)
            return null;

        var queryBounds = new Rect(
            point.X - tolerance,
            point.Y - tolerance,
            tolerance * 2d,
            tolerance * 2d);

        KmlLineFeature? best = null;
        var bestDistance = tolerance;

        foreach (var segment in QuerySegments(queryBounds))
        {
            var distance = PointToSegmentDistance(
                point,
                segment.A,
                segment.B);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = segment.Feature;
            }
        }

        return best;
    }

    public LineSplitPreview PreviewSplitWithStroke(
        IReadOnlyList<Point> stroke,
        IReadOnlySet<XElement>? targetLines = null)
    {
        if (stroke.Count < 2 || Features.Count == 0)
        {
            return new LineSplitPreview(
                0,
                0,
                Array.Empty<Point>(),
                Array.Empty<XElement>());
        }

        var eventsByFeature = FindSplitEventsByFeature(
            stroke,
            targetLines);

        var cutPoints = new List<Point>();
        var affectedLines = new List<XElement>();

        foreach (var feature in Features)
        {
            if (!eventsByFeature.TryGetValue(
                    feature.LineStringElement,
                    out var events))
            {
                continue;
            }

            var previewEvents =
                GetEffectivePreviewEvents(
                    feature.Coordinates,
                    events);

            if (previewEvents.Count == 0)
                continue;

            affectedLines.Add(feature.LineStringElement);
            cutPoints.AddRange(
                previewEvents.Select(
                    item => item.Intersection));
        }

        return new LineSplitPreview(
            affectedLines.Count,
            cutPoints.Count,
            cutPoints,
            affectedLines);
    }

    public LineSplitResult SplitWithStroke(
        IReadOnlyList<Point> stroke,
        XName? targetMarkerAttribute = null)
    {
        if (stroke.Count < 2 || Features.Count == 0)
            return new LineSplitResult(0, 0, 0, Array.Empty<Point>());

        IReadOnlySet<XElement>? targetLines = null;

        if (targetMarkerAttribute is not null)
        {
            targetLines = Features
                .Where(feature =>
                    string.Equals(
                        feature.LineStringElement
                            .Attribute(targetMarkerAttribute)
                            ?.Value,
                        "true",
                        StringComparison.Ordinal))
                .Select(feature => feature.LineStringElement)
                .ToHashSet();
        }

        var eventsByFeature = FindSplitEventsByFeature(
            stroke,
            targetLines);

        var linesSplit = 0;
        var intersectionCount = 0;
        var outputSegments = 0;
        var splitPoints = new List<Point>();
        var updatedFeatures = Features.ToList();

        foreach (var feature in Features.ToList())
        {
            if (!eventsByFeature.TryGetValue(
                    feature.LineStringElement,
                    out var events))
            {
                continue;
            }

            var parts = BuildSplitParts(
                feature.Coordinates,
                events);

            if (parts.Count == 0)
                continue;

            RemoveFeatureFromSpatialIndex(feature);

            var replacementElements =
                ReplaceLineStringWithParts(
                    feature.LineStringElement,
                    parts);

            var featureIndex =
                updatedFeatures.FindIndex(item =>
                    ReferenceEquals(item, feature));

            if (featureIndex >= 0)
            {
                updatedFeatures.RemoveAt(featureIndex);

                var replacements = new List<KmlLineFeature>();

                for (var index = 0;
                     index < replacementElements.Count &&
                     index < parts.Count;
                     index++)
                {
                    replacements.Add(
                        CreateFeature(
                            replacementElements[index],
                            feature.Name,
                            parts[index]));
                }

                updatedFeatures.InsertRange(
                    featureIndex,
                    replacements);

                foreach (var replacement in replacements)
                    AddFeatureToSpatialIndex(replacement);
            }

            linesSplit++;
            intersectionCount += events.Count;
            outputSegments += parts.Count;
            splitPoints.AddRange(
                events.Select(item => item.Intersection));
        }

        if (linesSplit > 0)
        {
            Features = updatedFeatures;
            RecalculateWorldBounds();
        }

        return new LineSplitResult(
            linesSplit,
            intersectionCount,
            outputSegments,
            splitPoints);
    }

    public int DeleteLines(IEnumerable<XElement> lineStrings)
    {
        var lines = lineStrings
            .Where(element =>
                element.Name == KmlNs + "LineString" &&
                element.Document == _document)
            .Distinct()
            .ToList();

        var deleted = 0;

        foreach (var line in lines)
        {
            var parent = line.Parent;

            if (parent is null)
                continue;

            if (parent.Name == KmlNs + "Placemark")
            {
                parent.Remove();
                deleted++;
                continue;
            }

            line.Remove();
            deleted++;

            if (parent.Name != KmlNs + "MultiGeometry")
                continue;

            if (parent.Elements().Any(IsGeometryElement))
                continue;

            var placemark = parent.Parent;

            if (placemark?.Name == KmlNs + "Placemark")
                placemark.Remove();
            else
                parent.Remove();
        }

        if (deleted > 0)
        {
            var removedFeatures = Features
                .Where(feature =>
                    feature.LineStringElement.Document != _document)
                .ToList();

            foreach (var feature in removedFeatures)
                RemoveFeatureFromSpatialIndex(feature);

            Features = Features
                .Where(feature =>
                    feature.LineStringElement.Document == _document)
                .ToList();

            RecalculateWorldBounds();
        }

        return deleted;
    }

    private Dictionary<XElement, List<CutEvent>> FindSplitEventsByFeature(
        IReadOnlyList<Point> stroke,
        IReadOnlySet<XElement>? targetLines)
    {
        var result = new Dictionary<XElement, List<CutEvent>>();

        for (var strokeIndex = 0;
             strokeIndex < stroke.Count - 1;
             strokeIndex++)
        {
            var c = stroke[strokeIndex];
            var d = stroke[strokeIndex + 1];
            var bounds = BuildSegmentBounds(c, d);

            foreach (var segment in QuerySegments(bounds))
            {
                var element = segment.Feature.LineStringElement;

                if (targetLines is not null &&
                    !targetLines.Contains(element))
                {
                    continue;
                }

                if (!TrySegmentIntersection(
                        segment.A,
                        segment.B,
                        c,
                        d,
                        out var targetT,
                        out _,
                        out var intersection))
                {
                    continue;
                }

                var pathPosition =
                    segment.SegmentIndex + targetT;

                if (!result.TryGetValue(
                        element,
                        out var events))
                {
                    events = new List<CutEvent>();
                    result[element] = events;
                }

                if (events.Any(existing =>
                        Math.Abs(
                            existing.PathPosition -
                            pathPosition) <
                        0.0000001d))
                {
                    continue;
                }

                events.Add(
                    new CutEvent(
                        pathPosition,
                        intersection));
            }
        }

        foreach (var events in result.Values)
        {
            events.Sort((left, right) =>
                left.PathPosition.CompareTo(
                    right.PathPosition));
        }

        return result;
    }

    private static IReadOnlyList<CutEvent> GetEffectivePreviewEvents(
        IReadOnlyList<KmlCoordinate> coordinates,
        IReadOnlyList<CutEvent> events)
    {
        var segmentCount = coordinates.Count - 1;

        if (segmentCount < 1 || events.Count == 0)
            return Array.Empty<CutEvent>();

        if (IsClosed(coordinates))
        {
            var unique = new List<CutEvent>();

            foreach (var item in events
                         .OrderBy(item => item.PathPosition))
            {
                var position = item.PathPosition;

                if (position >=
                    segmentCount - 0.0000001d)
                {
                    position = 0d;
                }

                if (unique.Any(existing =>
                        Math.Abs(
                            NormalizeClosedPreviewPosition(
                                existing.PathPosition,
                                segmentCount) -
                            position) <
                        0.0000001d))
                {
                    continue;
                }

                unique.Add(item);
            }

            return unique;
        }

        return events
            .Where(item =>
                item.PathPosition > 0.0000001d &&
                item.PathPosition <
                    segmentCount - 0.0000001d)
            .ToList();
    }

    private static double NormalizeClosedPreviewPosition(
        double position,
        int segmentCount)
    {
        if (position >=
            segmentCount - 0.0000001d)
        {
            return 0d;
        }

        return Math.Max(0d, position);
    }

    private static List<List<KmlCoordinate>> BuildSplitParts(
        IReadOnlyList<KmlCoordinate> coordinates,
        IReadOnlyList<CutEvent> events)
    {
        var segmentCount = coordinates.Count - 1;
        if (segmentCount < 1)
            return new List<List<KmlCoordinate>>();

        var closed = IsClosed(coordinates);

        if (!closed)
        {
            var cuts = events
                .Select(item => item.PathPosition)
                .Where(position =>
                    position > 0.0000001d &&
                    position < segmentCount - 0.0000001d)
                .OrderBy(position => position)
                .ToList();

            cuts = DeduplicatePositions(cuts);

            if (cuts.Count == 0)
                return new List<List<KmlCoordinate>>();

            var boundaries = new List<double> { 0d };
            boundaries.AddRange(cuts);
            boundaries.Add(segmentCount);

            var parts = new List<List<KmlCoordinate>>();

            for (var index = 0; index < boundaries.Count - 1; index++)
            {
                var part = ExtractRange(
                    coordinates,
                    boundaries[index],
                    boundaries[index + 1]);

                if (HasLength(part))
                    parts.Add(part);
            }

            return parts;
        }

        var closedCuts = new List<double>();

        foreach (var item in events)
        {
            var position = item.PathPosition;

            if (position >= segmentCount - 0.0000001d)
                position = 0d;

            if (position < 0d)
                position = 0d;

            closedCuts.Add(position);
        }

        closedCuts = DeduplicatePositions(
            closedCuts.OrderBy(position => position).ToList());

        // A single crossing opens the ring at the cut point. The actual
        // opening is defined in longitude/latitude degrees, not as an arbitrary
        // fraction of a source segment. Each side is trimmed by 1e-7 degrees,
        // deliberately well above the 1e-9 degree IsClosed tolerance while
        // remaining visually negligible at illustration scale.
        if (closedCuts.Count == 1)
        {
            const double seedOffsetInSegmentUnits = 0.000000001d;
            var cut = closedCuts[0];
            var start = cut + seedOffsetInSegmentUnits;
            var end = cut - seedOffsetInSegmentUnits;

            while (start >= segmentCount)
                start -= segmentCount;

            while (end < 0d)
                end += segmentCount;

            var opened = ExtractClosedArc(
                coordinates,
                start,
                end,
                segmentCount);

            opened = TrimPolylineEndsByDegrees(
                opened,
                OpenRingTrimDegrees);

            return HasLength(opened) && !IsClosed(opened)
                ? new List<List<KmlCoordinate>> { opened }
                : new List<List<KmlCoordinate>>();
        }

        if (closedCuts.Count < 2)
            return new List<List<KmlCoordinate>>();

        var closedParts = new List<List<KmlCoordinate>>();

        for (var index = 0; index < closedCuts.Count - 1; index++)
        {
            var part = ExtractRange(
                coordinates,
                closedCuts[index],
                closedCuts[index + 1]);

            if (HasLength(part))
                closedParts.Add(part);
        }

        var wrap = ExtractRange(
            coordinates,
            closedCuts[^1],
            segmentCount);

        var beginning = ExtractRange(
            coordinates,
            0d,
            closedCuts[0]);

        if (wrap.Count > 0 && beginning.Count > 0)
        {
            foreach (var point in beginning.Skip(1))
                wrap.Add(point);
        }

        wrap = RemoveConsecutiveDuplicates(wrap);

        if (HasLength(wrap))
            closedParts.Add(wrap);

        return closedParts;
    }

    private static List<KmlCoordinate> ExtractRange(
        IReadOnlyList<KmlCoordinate> coordinates,
        double start,
        double end)
    {
        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(end));

        var points = new List<KmlCoordinate>
        {
            CoordinateAt(coordinates, start)
        };

        var firstVertex = (int)Math.Floor(start) + 1;

        for (var vertex = firstVertex;
             vertex < coordinates.Count &&
             vertex < end - 0.0000001d;
             vertex++)
        {
            points.Add(coordinates[vertex]);
        }

        points.Add(CoordinateAt(coordinates, end));
        return RemoveConsecutiveDuplicates(points);
    }

    private static List<KmlCoordinate> ExtractClosedArc(
        IReadOnlyList<KmlCoordinate> coordinates,
        double start,
        double end,
        int segmentCount)
    {
        if (start <= end)
            return ExtractRange(coordinates, start, end);

        var tail = ExtractRange(coordinates, start, segmentCount);
        var head = ExtractRange(coordinates, 0d, end);

        foreach (var point in head.Skip(1))
            tail.Add(point);

        return RemoveConsecutiveDuplicates(tail);
    }

    private static List<KmlCoordinate> TrimPolylineEndsByDegrees(
        IReadOnlyList<KmlCoordinate> points,
        double trimDegrees)
    {
        var result = points.ToList();

        if (result.Count < 3 || trimDegrees <= 0d)
            return result;

        TrimStartByDegrees(result, trimDegrees);
        TrimEndByDegrees(result, trimDegrees);

        return RemoveConsecutiveDuplicates(result);
    }

    private static void TrimStartByDegrees(
        List<KmlCoordinate> points,
        double distance)
    {
        var remaining = distance;

        while (points.Count >= 2 && remaining > 0d)
        {
            var segmentLength = CoordinateDistanceDegrees(
                points[0],
                points[1]);

            if (segmentLength <= CoordinateToleranceDegrees)
            {
                points.RemoveAt(0);
                continue;
            }

            if (segmentLength <= remaining)
            {
                remaining -= segmentLength;
                points.RemoveAt(0);
                continue;
            }

            points[0] = Interpolate(
                points[0],
                points[1],
                remaining / segmentLength);

            break;
        }
    }

    private static void TrimEndByDegrees(
        List<KmlCoordinate> points,
        double distance)
    {
        var remaining = distance;

        while (points.Count >= 2 && remaining > 0d)
        {
            var last = points.Count - 1;
            var segmentLength = CoordinateDistanceDegrees(
                points[last],
                points[last - 1]);

            if (segmentLength <= CoordinateToleranceDegrees)
            {
                points.RemoveAt(last);
                continue;
            }

            if (segmentLength <= remaining)
            {
                remaining -= segmentLength;
                points.RemoveAt(last);
                continue;
            }

            points[last] = Interpolate(
                points[last],
                points[last - 1],
                remaining / segmentLength);

            break;
        }
    }

    private static double CoordinateDistanceDegrees(
        KmlCoordinate a,
        KmlCoordinate b)
    {
        var dx = b.Longitude - a.Longitude;
        var dy = b.Latitude - a.Latitude;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static KmlCoordinate CoordinateAt(
        IReadOnlyList<KmlCoordinate> coordinates,
        double position)
    {
        var segmentCount = coordinates.Count - 1;

        if (position <= 0d)
            return coordinates[0];

        if (position >= segmentCount)
            return coordinates[^1];

        var segmentIndex = (int)Math.Floor(position);
        var t = position - segmentIndex;

        return Interpolate(
            coordinates[segmentIndex],
            coordinates[segmentIndex + 1],
            t);
    }

    private static KmlCoordinate Interpolate(
        KmlCoordinate a,
        KmlCoordinate b,
        double t)
    {
        var altitude = a.Altitude.HasValue && b.Altitude.HasValue
            ? a.Altitude.Value +
              ((b.Altitude.Value - a.Altitude.Value) * t)
            : a.Altitude ?? b.Altitude;

        return new KmlCoordinate(
            a.Longitude + ((b.Longitude - a.Longitude) * t),
            a.Latitude + ((b.Latitude - a.Latitude) * t),
            altitude);
    }

    private static IReadOnlyList<XElement> ReplaceLineStringWithParts(
        XElement original,
        IReadOnlyList<List<KmlCoordinate>> parts)
    {
        var clones = new List<XElement>(parts.Count);
        var originalId = original.Attribute("id")?.Value;

        for (var index = 0; index < parts.Count; index++)
        {
            var clone = new XElement(original);
            var coordinateElement = clone.Element(KmlNs + "coordinates");

            if (coordinateElement is null)
                continue;

            coordinateElement.Value = FormatCoordinates(parts[index]);

            var idAttribute = clone.Attribute("id");

            if (idAttribute is not null &&
                index > 0)
            {
                idAttribute.Value =
                    $"{originalId ?? "line"}-part-{index + 1}-{Guid.NewGuid():N}";
            }

            clones.Add(clone);
        }

        if (clones.Count == 0)
            return Array.Empty<XElement>();

        if (clones.Count == 1)
        {
            var coordinateElement = original.Element(KmlNs + "coordinates");
            if (coordinateElement is not null)
                coordinateElement.Value = FormatCoordinates(parts[0]);

            return new[] { original };
        }

        if (original.Parent?.Name == KmlNs + "MultiGeometry")
        {
            original.ReplaceWith(clones.Cast<object>().ToArray());
            return clones;
        }

        original.ReplaceWith(
            new XElement(
                KmlNs + "MultiGeometry",
                clones));

        return clones;
    }

    private KmlLineFeature CreateFeature(
        XElement lineString,
        string name,
        IReadOnlyList<KmlCoordinate> coordinates)
    {
        var coordinateList = coordinates.ToList();
        var worldPoints = coordinateList
            .Select(ToWorld)
            .ToList();

        return new KmlLineFeature(
            lineString,
            name,
            coordinateList,
            worldPoints,
            BuildBounds(worldPoints));
    }

    private void RecalculateWorldBounds()
    {
        var bounds = Rect.Empty;

        foreach (var feature in Features)
        {
            if (feature.WorldBounds.IsEmpty)
                continue;

            if (bounds.IsEmpty)
                bounds = feature.WorldBounds;
            else
                bounds.Union(feature.WorldBounds);
        }

        WorldBounds = bounds;
    }

    public Point Project(KmlCoordinate coordinate)
    {
        return new Point(
            coordinate.Longitude * _longitudeScale,
            coordinate.Latitude);
    }

    public KmlCoordinate Unproject(Point point)
    {
        return new KmlCoordinate(
            point.X / _longitudeScale,
            point.Y,
            null);
    }

    private Point ToWorld(KmlCoordinate coordinate)
    {
        return Project(coordinate);
    }

    private void BuildSpatialIndex()
    {
        _segmentGrid.Clear();
        _segmentsByFeature.Clear();
        _indexBounds = WorldBounds;

        if (Features.Count == 0 ||
            _indexBounds.IsEmpty)
        {
            _gridColumns = 0;
            _gridRows = 0;
            return;
        }

        var segmentCount = Features.Sum(feature =>
            Math.Max(0, feature.WorldPoints.Count - 1));

        var dimension = Math.Clamp(
            (int)Math.Ceiling(
                Math.Sqrt(
                    Math.Max(1d, segmentCount / 8d))),
            16,
            160);

        _gridColumns = dimension;
        _gridRows = dimension;

        _gridCellWidth = Math.Max(
            _indexBounds.Width / _gridColumns,
            0.000000000001d);

        _gridCellHeight = Math.Max(
            _indexBounds.Height / _gridRows,
            0.000000000001d);

        foreach (var feature in Features)
            AddFeatureToSpatialIndex(feature);
    }

    private void AddFeatureToSpatialIndex(
        KmlLineFeature feature)
    {
        if (_gridColumns <= 0 ||
            _gridRows <= 0 ||
            feature.WorldPoints.Count < 2)
        {
            return;
        }

        var segments = new List<IndexedSegment>(
            feature.WorldPoints.Count - 1);

        for (var index = 0;
             index < feature.WorldPoints.Count - 1;
             index++)
        {
            var segment = new IndexedSegment(
                feature,
                index,
                feature.WorldPoints[index],
                feature.WorldPoints[index + 1]);

            segments.Add(segment);

            foreach (var key in GridKeysForBounds(
                         segment.Bounds))
            {
                if (!_segmentGrid.TryGetValue(
                        key,
                        out var bucket))
                {
                    bucket = new List<IndexedSegment>();
                    _segmentGrid[key] = bucket;
                }

                bucket.Add(segment);
            }
        }

        _segmentsByFeature[
            feature.LineStringElement] = segments;
    }

    private void RemoveFeatureFromSpatialIndex(
        KmlLineFeature feature)
    {
        if (!_segmentsByFeature.TryGetValue(
                feature.LineStringElement,
                out var segments))
        {
            return;
        }

        var segmentsToRemove =
            segments.ToHashSet();

        var affectedKeys =
            segments
                .SelectMany(segment =>
                    GridKeysForBounds(
                        segment.Bounds))
                .Distinct()
                .ToList();

        foreach (var key in affectedKeys)
        {
            if (!_segmentGrid.TryGetValue(
                    key,
                    out var bucket))
            {
                continue;
            }

            bucket.RemoveAll(segment =>
                segmentsToRemove.Contains(segment));

            if (bucket.Count == 0)
                _segmentGrid.Remove(key);
        }

        _segmentsByFeature.Remove(
            feature.LineStringElement);
    }

    private IEnumerable<long> GridKeysForBounds(
        Rect bounds)
    {
        var minX = GridX(bounds.Left);
        var maxX = GridX(bounds.Right);
        var minY = GridY(bounds.Top);
        var maxY = GridY(bounds.Bottom);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
                yield return GridKey(x, y);
        }
    }

    private IEnumerable<IndexedSegment> QuerySegments(
        Rect bounds)
    {
        if (_gridColumns <= 0 ||
            _gridRows <= 0 ||
            bounds.IsEmpty)
        {
            yield break;
        }

        var seen = new HashSet<IndexedSegment>();

        foreach (var key in GridKeysForBounds(bounds))
        {
            if (!_segmentGrid.TryGetValue(
                    key,
                    out var bucket))
            {
                continue;
            }

            foreach (var segment in bucket)
            {
                if (!seen.Add(segment))
                    continue;

                if (BoundsOverlap(
                        segment.Bounds,
                        bounds))
                {
                    yield return segment;
                }
            }
        }
    }

    private int GridX(double x)
    {
        if (_gridColumns <= 1)
            return 0;

        return Math.Clamp(
            (int)Math.Floor(
                (x - _indexBounds.Left) /
                _gridCellWidth),
            0,
            _gridColumns - 1);
    }

    private int GridY(double y)
    {
        if (_gridRows <= 1)
            return 0;

        return Math.Clamp(
            (int)Math.Floor(
                (y - _indexBounds.Top) /
                _gridCellHeight),
            0,
            _gridRows - 1);
    }

    private static long GridKey(int x, int y)
    {
        return ((long)x << 32) |
               (uint)y;
    }

    private static Rect BuildSegmentBounds(
        Point a,
        Point b)
    {
        return new Rect(a, b);
    }

    private static Rect BuildBounds(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            return Rect.Empty;

        var bounds = new Rect(points[0], points[0]);

        for (var index = 1; index < points.Count; index++)
            bounds.Union(points[index]);

        return bounds;
    }

    private static bool BoundsOverlap(Rect left, Rect right)
    {
        if (left.IsEmpty || right.IsEmpty)
            return false;

        return left.Left <= right.Right &&
               left.Right >= right.Left &&
               left.Top <= right.Bottom &&
               left.Bottom >= right.Top;
    }

    private static List<KmlCoordinate> ParseCoordinates(string value)
    {
        var points = new List<KmlCoordinate>();

        foreach (var token in value.Split(
                     (char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = token.Split(',');
            if (fields.Length < 2)
                continue;

            if (!double.TryParse(
                    fields[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var longitude) ||
                !double.TryParse(
                    fields[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var latitude))
            {
                continue;
            }

            double? altitude = null;

            if (fields.Length >= 3 &&
                double.TryParse(
                    fields[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedAltitude))
            {
                altitude = parsedAltitude;
            }

            points.Add(new KmlCoordinate(
                longitude,
                latitude,
                altitude));
        }

        return points;
    }

    private static string FormatCoordinates(
        IEnumerable<KmlCoordinate> coordinates)
    {
        return string.Join(
            " ",
            coordinates.Select(point =>
            {
                var longitude = point.Longitude.ToString(
                    "R",
                    CultureInfo.InvariantCulture);

                var latitude = point.Latitude.ToString(
                    "R",
                    CultureInfo.InvariantCulture);

                if (!point.Altitude.HasValue)
                    return $"{longitude},{latitude}";

                var altitude = point.Altitude.Value.ToString(
                    "R",
                    CultureInfo.InvariantCulture);

                return $"{longitude},{latitude},{altitude}";
            }));
    }

    private static bool IsClosed(
        IReadOnlyList<KmlCoordinate> coordinates)
    {
        if (coordinates.Count < 3)
            return false;

        var first = coordinates[0];
        var last = coordinates[^1];

        return Math.Abs(first.Longitude - last.Longitude) < CoordinateToleranceDegrees &&
               Math.Abs(first.Latitude - last.Latitude) < CoordinateToleranceDegrees;
    }

    private static List<double> DeduplicatePositions(
        IReadOnlyList<double> positions)
    {
        var result = new List<double>();

        foreach (var position in positions)
        {
            if (result.Count == 0 ||
                Math.Abs(result[^1] - position) >= 0.0000001d)
            {
                result.Add(position);
            }
        }

        return result;
    }

    private static List<KmlCoordinate> RemoveConsecutiveDuplicates(
        IEnumerable<KmlCoordinate> points)
    {
        var result = new List<KmlCoordinate>();

        foreach (var point in points)
        {
            if (result.Count == 0 ||
                !SamePoint(result[^1], point))
            {
                result.Add(point);
            }
        }

        return result;
    }

    private static bool HasLength(IReadOnlyList<KmlCoordinate> points)
    {
        if (points.Count < 2)
            return false;

        for (var index = 0; index < points.Count - 1; index++)
        {
            if (!SamePoint(points[index], points[index + 1]))
                return true;
        }

        return false;
    }

    private static bool SamePoint(KmlCoordinate a, KmlCoordinate b)
    {
        return Math.Abs(a.Longitude - b.Longitude) < CoordinateToleranceDegrees &&
               Math.Abs(a.Latitude - b.Latitude) < CoordinateToleranceDegrees;
    }

    private static bool IsGeometryElement(XElement element)
    {
        return element.Name == KmlNs + "Point" ||
               element.Name == KmlNs + "LineString" ||
               element.Name == KmlNs + "LinearRing" ||
               element.Name == KmlNs + "Polygon" ||
               element.Name == KmlNs + "MultiGeometry" ||
               element.Name.LocalName is "Track" or "MultiTrack";
    }

    private static bool SegmentsIntersect(
        Point a,
        Point b,
        Point c,
        Point d)
    {
        return TrySegmentIntersection(
            a,
            b,
            c,
            d,
            out _,
            out _,
            out _);
    }

    private static bool TrySegmentIntersection(
        Point a,
        Point b,
        Point c,
        Point d,
        out double targetT,
        out double strokeT,
        out Point intersection)
    {
        var r = b - a;
        var s = d - c;
        var denominator = Cross(r, s);

        targetT = 0d;
        strokeT = 0d;
        intersection = default;

        // Collinear overlaps are ambiguous for a cut gesture. Ignore them;
        // crossing or near-crossing gestures are what the line editor expects.
        if (Math.Abs(denominator) < 0.000000000001d)
            return false;

        var cMinusA = c - a;
        targetT = Cross(cMinusA, s) / denominator;
        strokeT = Cross(cMinusA, r) / denominator;

        const double epsilon = 0.000000001d;

        if (targetT < -epsilon ||
            targetT > 1d + epsilon ||
            strokeT < -epsilon ||
            strokeT > 1d + epsilon)
        {
            return false;
        }

        targetT = Math.Clamp(targetT, 0d, 1d);
        strokeT = Math.Clamp(strokeT, 0d, 1d);
        intersection = a + (r * targetT);
        return true;
    }

    private static double SegmentDistance(
        Point a,
        Point b,
        Point c,
        Point d)
    {
        if (SegmentsIntersect(a, b, c, d))
            return 0d;

        return Math.Min(
            Math.Min(PointToSegmentDistance(a, c, d),
                     PointToSegmentDistance(b, c, d)),
            Math.Min(PointToSegmentDistance(c, a, b),
                     PointToSegmentDistance(d, a, b)));
    }

    private static double PointToSegmentDistance(
        Point point,
        Point a,
        Point b)
    {
        var ab = b - a;
        var lengthSquared = (ab.X * ab.X) + (ab.Y * ab.Y);

        if (lengthSquared <= double.Epsilon)
            return (point - a).Length;

        var ap = point - a;
        var t = ((ap.X * ab.X) + (ap.Y * ab.Y)) / lengthSquared;
        t = Math.Clamp(t, 0d, 1d);

        var closest = a + (ab * t);
        return (point - closest).Length;
    }

    private static double Cross(Vector a, Vector b)
    {
        return (a.X * b.Y) - (a.Y * b.X);
    }

    private sealed class IndexedSegment
    {
        public IndexedSegment(
            KmlLineFeature feature,
            int segmentIndex,
            Point a,
            Point b)
        {
            Feature = feature;
            SegmentIndex = segmentIndex;
            A = a;
            B = b;
            Bounds = BuildSegmentBounds(a, b);
        }

        public KmlLineFeature Feature { get; }
        public int SegmentIndex { get; }
        public Point A { get; }
        public Point B { get; }
        public Rect Bounds { get; }
    }

    private sealed record CutEvent(
        double PathPosition,
        Point Intersection);
}

public sealed class KmlLineFeature
{
    public KmlLineFeature(
        XElement lineStringElement,
        string name,
        IReadOnlyList<KmlCoordinate> coordinates,
        IReadOnlyList<Point> worldPoints,
        Rect worldBounds)
    {
        LineStringElement = lineStringElement;
        Name = name;
        Coordinates = coordinates;
        WorldPoints = worldPoints;
        WorldBounds = worldBounds;
    }

    public XElement LineStringElement { get; }
    public string Name { get; }
    public IReadOnlyList<KmlCoordinate> Coordinates { get; }
    public IReadOnlyList<Point> WorldPoints { get; }
    public Rect WorldBounds { get; }
}

public readonly record struct KmlCoordinate(
    double Longitude,
    double Latitude,
    double? Altitude);

public readonly record struct LineSplitPreview(
    int LinesAffected,
    int IntersectionCount,
    IReadOnlyList<Point> CutPoints,
    IReadOnlyList<XElement> AffectedLines);

public readonly record struct LineSplitResult(
    int LinesSplit,
    int IntersectionCount,
    int OutputSegments,
    IReadOnlyList<Point> CutPoints);
