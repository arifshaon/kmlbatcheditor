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

    private readonly XDocument _document;
    private double _longitudeScale = 1d;

    public KmlLineGeometryService(XDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Reload();
    }

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

            var name = placemark?.Element(KmlNs + "name")?.Value?.Trim();
            raw.Add((
                lineString,
                string.IsNullOrWhiteSpace(name) ? "Line" : name,
                coordinates));
        }

        var allCoordinates = raw
            .SelectMany(item => item.Coordinates)
            .ToList();

        if (allCoordinates.Count > 0)
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

            features.Add(new KmlLineFeature(
                item.Line,
                item.Name,
                item.Coordinates,
                worldPoints));
        }

        Features = features;
        WorldBounds = bounds;
    }

    public IReadOnlyList<KmlLineFeature> FindIntersectingFeatures(
        IReadOnlyList<Point> stroke,
        double tolerance)
    {
        if (stroke.Count < 2 || Features.Count == 0)
            return Array.Empty<KmlLineFeature>();

        tolerance = Math.Max(0d, tolerance);
        var matches = new List<KmlLineFeature>();

        foreach (var feature in Features)
        {
            if (FeatureIntersectsStroke(feature, stroke, tolerance))
                matches.Add(feature);
        }

        return matches;
    }

    public LineCutResult CutWithStroke(IReadOnlyList<Point> stroke)
    {
        if (stroke.Count < 2 || Features.Count == 0)
            return new LineCutResult(0, 0, 0);

        var linesSplit = 0;
        var intersectionCount = 0;
        var outputSegments = 0;

        // Work from the current feature snapshot. Replacements do not disturb
        // references to other LineStrings in the same XDocument.
        foreach (var feature in Features.ToList())
        {
            var events = FindCutEvents(feature, stroke);
            if (events.Count == 0)
                continue;

            var parts = BuildSplitParts(feature.Coordinates, events);
            if (parts.Count < 2)
                continue;

            ReplaceLineStringWithParts(feature.LineStringElement, parts);

            linesSplit++;
            intersectionCount += events.Count;
            outputSegments += parts.Count;
        }

        if (linesSplit > 0)
            Reload();

        return new LineCutResult(
            linesSplit,
            intersectionCount,
            outputSegments);
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
            Reload();

        return deleted;
    }

    private bool FeatureIntersectsStroke(
        KmlLineFeature feature,
        IReadOnlyList<Point> stroke,
        double tolerance)
    {
        var line = feature.WorldPoints;

        for (var lineIndex = 0; lineIndex < line.Count - 1; lineIndex++)
        {
            var a = line[lineIndex];
            var b = line[lineIndex + 1];

            for (var strokeIndex = 0;
                 strokeIndex < stroke.Count - 1;
                 strokeIndex++)
            {
                var c = stroke[strokeIndex];
                var d = stroke[strokeIndex + 1];

                if (SegmentsIntersect(a, b, c, d) ||
                    SegmentDistance(a, b, c, d) <= tolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private List<CutEvent> FindCutEvents(
        KmlLineFeature feature,
        IReadOnlyList<Point> stroke)
    {
        var events = new List<CutEvent>();
        var line = feature.WorldPoints;

        for (var lineIndex = 0; lineIndex < line.Count - 1; lineIndex++)
        {
            var a = line[lineIndex];
            var b = line[lineIndex + 1];

            for (var strokeIndex = 0;
                 strokeIndex < stroke.Count - 1;
                 strokeIndex++)
            {
                var c = stroke[strokeIndex];
                var d = stroke[strokeIndex + 1];

                if (!TrySegmentIntersection(
                        a,
                        b,
                        c,
                        d,
                        out var targetT,
                        out _,
                        out _))
                {
                    continue;
                }

                var pathPosition = lineIndex + targetT;

                if (events.Any(existing =>
                        Math.Abs(existing.PathPosition - pathPosition) <
                        0.0000001d))
                {
                    continue;
                }

                events.Add(new CutEvent(pathPosition));
            }
        }

        return events
            .OrderBy(item => item.PathPosition)
            .ToList();
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

        // A single point does not meaningfully divide a closed contour into
        // independently removable pieces. Normal cross-cuts produce two or
        // more intersections.
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

    private static void ReplaceLineStringWithParts(
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

        if (clones.Count < 2)
            return;

        if (original.Parent?.Name == KmlNs + "MultiGeometry")
        {
            original.ReplaceWith(clones.Cast<object>().ToArray());
            return;
        }

        original.ReplaceWith(
            new XElement(
                KmlNs + "MultiGeometry",
                clones));
    }

    private Point ToWorld(KmlCoordinate coordinate)
    {
        return new Point(
            coordinate.Longitude * _longitudeScale,
            coordinate.Latitude);
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

        return Math.Abs(first.Longitude - last.Longitude) < 0.000000001d &&
               Math.Abs(first.Latitude - last.Latitude) < 0.000000001d;
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
        return Math.Abs(a.Longitude - b.Longitude) < 0.000000001d &&
               Math.Abs(a.Latitude - b.Latitude) < 0.000000001d;
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

    private sealed record CutEvent(double PathPosition);
}

public sealed class KmlLineFeature
{
    public KmlLineFeature(
        XElement lineStringElement,
        string name,
        IReadOnlyList<KmlCoordinate> coordinates,
        IReadOnlyList<Point> worldPoints)
    {
        LineStringElement = lineStringElement;
        Name = name;
        Coordinates = coordinates;
        WorldPoints = worldPoints;
    }

    public XElement LineStringElement { get; }
    public string Name { get; }
    public IReadOnlyList<KmlCoordinate> Coordinates { get; }
    public IReadOnlyList<Point> WorldPoints { get; }
}

public readonly record struct KmlCoordinate(
    double Longitude,
    double Latitude,
    double? Altitude);

public readonly record struct LineCutResult(
    int LinesSplit,
    int IntersectionCount,
    int OutputSegments);
