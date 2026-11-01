using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace KmlScopedEditor.Services;

/// <summary>
/// Loads one or more KML LineString/Polygon boundaries from a KML/KMZ file for
/// use as temporary split or clip guides. Guide geometry is kept separate from
/// the edited document and is never written into the output.
/// </summary>
public sealed class KmlCutGuideService
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private const double ClosedToleranceMetres = 0.5d;

    public IReadOnlyList<KmlCutGuide> Load(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A file path is required.", nameof(sourcePath));

        var extension = Path.GetExtension(sourcePath);

        XDocument document = extension.Equals(".kml", StringComparison.OrdinalIgnoreCase)
            ? XDocument.Load(sourcePath, LoadOptions.None)
            : extension.Equals(".kmz", StringComparison.OrdinalIgnoreCase)
                ? LoadKmz(sourcePath)
                : throw new NotSupportedException(
                    "Only .kml and .kmz Google Earth files are supported as split/clip guides.");

        var guides = new List<KmlCutGuide>();

        foreach (var lineString in document.Descendants(KmlNs + "LineString"))
        {
            var coordinatesElement = lineString.Element(KmlNs + "coordinates");
            if (coordinatesElement is null)
                continue;

            var coordinates = ParseCoordinates(coordinatesElement.Value);
            if (coordinates.Count < 2)
                continue;

            var placemark = lineString
                .Ancestors(KmlNs + "Placemark")
                .FirstOrDefault();

            var name = placemark?.Element(KmlNs + "name")?.Value?.Trim();
            var isClosed = IsClosedWithinTolerance(coordinates);

            guides.Add(new KmlCutGuide(
                string.IsNullOrWhiteSpace(name) ? "Cut line" : name,
                NormalizeClosedCoordinates(coordinates, isClosed),
                isClosed));
        }

        // Google Earth polygons are useful natural clip boundaries. For this
        // version only the outer ring is imported. Inner rings/holes are not
        // converted into independent loops because that would invert their
        // polygon semantics when multiple closed boundaries are treated as a
        // union.
        foreach (var polygon in document.Descendants(KmlNs + "Polygon"))
        {
            var ring = polygon
                .Element(KmlNs + "outerBoundaryIs")
                ?.Element(KmlNs + "LinearRing");

            var coordinatesElement = ring?.Element(KmlNs + "coordinates");
            if (coordinatesElement is null)
                continue;

            var coordinates = ParseCoordinates(coordinatesElement.Value);
            if (coordinates.Count < 3)
                continue;

            var placemark = polygon
                .Ancestors(KmlNs + "Placemark")
                .FirstOrDefault();

            var name = placemark?.Element(KmlNs + "name")?.Value?.Trim();

            guides.Add(new KmlCutGuide(
                string.IsNullOrWhiteSpace(name) ? "Polygon boundary" : name,
                NormalizeClosedCoordinates(coordinates, isClosed: true),
                true));
        }

        return guides;
    }

    private static XDocument LoadKmz(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);

        var kmlEntries = archive.Entries
            .Where(entry => Path.GetExtension(entry.FullName)
                .Equals(".kml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var entry = kmlEntries.FirstOrDefault(item =>
            item.FullName.Equals("doc.kml", StringComparison.OrdinalIgnoreCase));

        entry ??= kmlEntries
            .OrderBy(item => item.FullName.Count(character => character == '/'))
            .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (entry is null)
            throw new InvalidDataException("The KMZ package does not contain a KML file.");

        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
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

            points.Add(new KmlCoordinate(longitude, latitude, altitude));
        }

        return points;
    }

    private static bool IsClosedWithinTolerance(
        IReadOnlyList<KmlCoordinate> coordinates)
    {
        if (coordinates.Count < 4)
            return false;

        return DistanceMetres(
            coordinates[0],
            coordinates[^1]) <=
            ClosedToleranceMetres;
    }

    private static IReadOnlyList<KmlCoordinate> NormalizeClosedCoordinates(
        IReadOnlyList<KmlCoordinate> coordinates,
        bool isClosed)
    {
        var normalized = coordinates.ToList();

        if (!isClosed || normalized.Count < 3)
            return normalized;

        if (DistanceMetres(
                normalized[0],
                normalized[^1]) <=
            ClosedToleranceMetres)
        {
            normalized[^1] = normalized[0];
        }
        else
        {
            normalized.Add(normalized[0]);
        }

        return normalized;
    }

    private static double DistanceMetres(
        KmlCoordinate a,
        KmlCoordinate b)
    {
        var meanLatitude =
            ((a.Latitude + b.Latitude) / 2d) *
            Math.PI / 180d;

        var dx =
            (b.Longitude - a.Longitude) *
            Math.Cos(meanLatitude) *
            111320d;

        var dy =
            (b.Latitude - a.Latitude) *
            111320d;

        return Math.Sqrt(
            (dx * dx) +
            (dy * dy));
    }
}

public sealed record KmlCutGuide(
    string Name,
    IReadOnlyList<KmlCoordinate> Coordinates,
    bool IsClosed = false);
