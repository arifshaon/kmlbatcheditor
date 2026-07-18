using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

/// <summary>
/// Creates a temporary one-placemark KMZ that can be opened by the user's
/// registered Google Earth application. Temporary previews are cleaned up when
/// the editor closes.
/// </summary>
public sealed class KmlGoogleEarthPreviewService : IDisposable
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private readonly List<string> _previewDirectories = new();

    public string CreatePreviewKmz(
        XElement sourcePlacemark,
        KmlPlacemarkVisualPreview preview)
    {
        ArgumentNullException.ThrowIfNull(sourcePlacemark);
        ArgumentNullException.ThrowIfNull(preview);

        var previewDirectory = Path.Combine(
            Path.GetTempPath(),
            "KmlScopedEditor",
            "Previews",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(previewDirectory);
        _previewDirectories.Add(previewDirectory);

        var placemark = new XElement(sourcePlacemark);
        placemark.Elements(KmlNs + "styleUrl").Remove();
        placemark.Elements(KmlNs + "Style").Remove();
        placemark.Elements(KmlNs + "StyleMap").Remove();

        var proposed = preview.Proposed;
        var iconHref = proposed.IconHref;

        if (!string.IsNullOrWhiteSpace(proposed.LocalIconPath) &&
            File.Exists(proposed.LocalIconPath))
        {
            var iconDirectory = Path.Combine(previewDirectory, "files", "icons");
            Directory.CreateDirectory(iconDirectory);

            var iconFileName = "preview" +
                Path.GetExtension(proposed.LocalIconPath).ToLowerInvariant();

            var destinationIcon = Path.Combine(iconDirectory, iconFileName);
            File.Copy(proposed.LocalIconPath, destinationIcon, overwrite: true);
            iconHref = $"files/icons/{iconFileName}";
        }

        var style = CreateStyle(proposed, iconHref);
        InsertInlineStyle(placemark, style);

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                KmlNs + "kml",
                new XElement(
                    KmlNs + "Document",
                    new XElement(KmlNs + "name", "KML Scoped Editor preview"),
                    new XElement(KmlNs + "open", "1"),
                    placemark)));

        var kmlPath = Path.Combine(previewDirectory, "doc.kml");
        document.Save(kmlPath, SaveOptions.DisableFormatting);

        var kmzPath = Path.Combine(
            previewDirectory,
            "KML-Scoped-Editor-Preview.kmz");

        using (var archiveStream = new FileStream(
                   kmzPath,
                   FileMode.Create,
                   FileAccess.ReadWrite,
                   FileShare.Read))
        using (var archive = new ZipArchive(
                   archiveStream,
                   ZipArchiveMode.Create,
                   leaveOpen: false))
        {
            AddFile(archive, kmlPath, "doc.kml");

            var filesDirectory = Path.Combine(previewDirectory, "files");

            if (Directory.Exists(filesDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(
                             filesDirectory,
                             "*",
                             SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(previewDirectory, file)
                        .Replace(Path.DirectorySeparatorChar, '/');

                    AddFile(archive, file, relative);
                }
            }
        }

        return kmzPath;
    }

    private static XElement CreateStyle(
        KmlPlacemarkAppearancePreview appearance,
        string? iconHref)
    {
        var iconStyle = new XElement(
            KmlNs + "IconStyle",
            new XElement(KmlNs + "color", appearance.KmlIconColor),
            new XElement(KmlNs + "scale", appearance.IconScale));

        if (!string.IsNullOrWhiteSpace(iconHref))
        {
            iconStyle.Add(
                new XElement(
                    KmlNs + "Icon",
                    new XElement(KmlNs + "href", iconHref)));
        }

        var labelStyle = new XElement(
            KmlNs + "LabelStyle",
            new XElement(KmlNs + "color", appearance.KmlLabelColor),
            new XElement(KmlNs + "scale", appearance.LabelScale));

        return new XElement(KmlNs + "Style", iconStyle, labelStyle);
    }

    private static void InsertInlineStyle(
        XElement placemark,
        XElement style)
    {
        var firstFollowingFeatureElement = placemark
            .Elements()
            .FirstOrDefault(element =>
                IsAfterStyleSelectorInFeatureOrder(element.Name.LocalName));

        if (firstFollowingFeatureElement is not null)
            firstFollowingFeatureElement.AddBeforeSelf(style);
        else
            placemark.Add(style);
    }

    private static bool IsAfterStyleSelectorInFeatureOrder(string localName)
    {
        return localName is
            "Region" or
            "ExtendedData" or
            "Point" or
            "LineString" or
            "LinearRing" or
            "Polygon" or
            "MultiGeometry" or
            "Model" or
            "Track" or
            "MultiTrack";
    }

    private static void AddFile(
        ZipArchive archive,
        string filePath,
        string entryName)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

        using var input = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        using var output = entry.Open();
        input.CopyTo(output);
    }

    public void Dispose()
    {
        foreach (var directory in _previewDirectories.Distinct())
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Google Earth may still be reading a preview. Windows will
                // remove it later through normal temporary-file cleanup.
            }
        }

        _previewDirectories.Clear();
    }
}
