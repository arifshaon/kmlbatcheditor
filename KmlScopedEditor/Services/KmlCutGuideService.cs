using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace KmlScopedEditor.Services;

/// <summary>
/// Loads one or more KML LineString paths from a KML/KMZ file for use as
/// temporary cut guides. Guide geometry is kept separate from the edited
/// document and is never written into the output.
/// </summary>
public sealed class KmlCutGuideService
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

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
                    "Only .kml and .kmz Google Earth files are supported as cut guides.");

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

            guides.Add(new KmlCutGuide(
                string.IsNullOrWhiteSpace(name) ? "Cut line" : name,
                coordinates));
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
}

public sealed record KmlCutGuide(
    string Name,
    IReadOnlyList<KmlCoordinate> Coordinates);
