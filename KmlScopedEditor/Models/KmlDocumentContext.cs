using System.IO;
using System.Xml.Linq;

namespace KmlScopedEditor.Models;

public sealed class KmlDocumentContext : IDisposable
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private bool _disposed;

    /// <summary>
    /// The KML or KMZ file chosen by the user.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// The actual KML file loaded into the XML document. For KMZ input,
    /// this points to the extracted KML inside the temporary workspace.
    /// </summary>
    public required string LoadedKmlPath { get; init; }

    public required XDocument Document { get; init; }

    public required KmlTreeNode RootNode { get; init; }

    public bool IsKmz { get; init; }

    /// <summary>
    /// Temporary extracted KMZ directory. Null for an ordinary KML file.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Relative path of the main KML within a KMZ package, usually doc.kml.
    /// </summary>
    public string KmlEntryRelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Number of non-KML files embedded in a KMZ package.
    /// </summary>
    public int EmbeddedResourceCount { get; init; }

    public IReadOnlyList<XElement> Placemarks { get; init; }
        = Array.Empty<XElement>();

    /// <summary>
    /// Mutable indexes are retained so newly cloned styles can be resolved
    /// immediately without reopening the file.
    /// </summary>
    public Dictionary<string, XElement> StylesById { get; init; }
        = new(StringComparer.Ordinal);

    public Dictionary<string, XElement> StyleMapsById { get; init; }
        = new(StringComparer.Ordinal);

    public string PackageTypeDisplay => IsKmz ? "KMZ" : "KML";

    public void RebuildStyleIndexes()
    {
        StylesById.Clear();
        StyleMapsById.Clear();

        foreach (var style in Document
                     .Descendants(KmlNs + "Style")
                     .Where(element => element.Attribute("id") is not null))
        {
            var id = style.Attribute("id")!.Value;

            if (!StylesById.ContainsKey(id))
                StylesById.Add(id, style);
        }

        foreach (var styleMap in Document
                     .Descendants(KmlNs + "StyleMap")
                     .Where(element => element.Attribute("id") is not null))
        {
            var id = styleMap.Attribute("id")!.Value;

            if (!StyleMapsById.ContainsKey(id))
                StyleMapsById.Add(id, styleMap);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (string.IsNullOrWhiteSpace(WorkingDirectory) ||
            !Directory.Exists(WorkingDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(WorkingDirectory, recursive: true);
        }
        catch
        {
            // A temporary directory may remain if another process still has
            // a package resource open. It can be removed by Windows later.
        }
    }
}
