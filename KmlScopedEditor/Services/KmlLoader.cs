using System.IO;
using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

public class KmlLoader
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    public KmlDocumentContext Load(
        string kmlPath,
        string? sourcePath = null,
        bool isKmz = false,
        string? workingDirectory = null,
        string? kmlEntryRelativePath = null,
        int embeddedResourceCount = 0,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Report(progress, "Reading the KML document...", Path.GetFileName(kmlPath), 30);

        var doc = XDocument.Load(
            kmlPath,
            LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

        cancellationToken.ThrowIfCancellationRequested();

        var root = doc.Root
            ?? throw new InvalidOperationException(
                "Invalid KML: missing root element.");

        if (root.Name.LocalName != "kml")
        {
            throw new InvalidOperationException(
                "Invalid KML: the root element is not <kml>.");
        }

        var originalSourcePath = sourcePath ?? kmlPath;

        Report(progress, "Building the folder and placemark tree...", null, 45);

        var rootNode = new KmlTreeNode
        {
            Name = Path.GetFileNameWithoutExtension(originalSourcePath),
            NodeType = KmlNodeType.Document,
            SourceElement = root,
            IsExpanded = true,
            IsChecked = false
        };

        var parsedNodeCount = 0;

        foreach (var child in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = ParseElement(
                child,
                cancellationToken,
                progress,
                ref parsedNodeCount);

            if (node is not null)
            {
                node.Parent = rootNode;
                rootNode.Children.Add(node);
            }
        }

        Report(progress, "Counting placemarks in folders...", null, 58);
        UpdatePlacemarkCounts(rootNode, cancellationToken);

        Report(progress, "Indexing placemarks...", null, 66);
        var placemarks = new List<XElement>();
        var placemarkProcessed = 0;

        foreach (var placemark in doc.Descendants(KmlNs + "Placemark"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            placemarks.Add(placemark);
            placemarkProcessed++;

            if (placemarkProcessed % 1000 == 0)
            {
                Report(
                    progress,
                    "Indexing placemarks...",
                    $"{placemarkProcessed:N0} placemarks indexed",
                    70);
            }
        }

        Report(progress, "Indexing shared styles...", null, 76);
        var stylesById = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var styleProcessed = 0;

        foreach (var style in doc
                     .Descendants(KmlNs + "Style")
                     .Where(element => element.Attribute("id") is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = style.Attribute("id")!.Value;
            stylesById.TryAdd(id, style);
            styleProcessed++;

            if (styleProcessed % 1000 == 0)
            {
                Report(
                    progress,
                    "Indexing shared styles...",
                    $"{styleProcessed:N0} shared styles indexed",
                    80);
            }
        }

        Report(progress, "Indexing style maps...", null, 84);
        var styleMapsById = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var mapProcessed = 0;

        foreach (var styleMap in doc
                     .Descendants(KmlNs + "StyleMap")
                     .Where(element => element.Attribute("id") is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = styleMap.Attribute("id")!.Value;
            styleMapsById.TryAdd(id, styleMap);
            mapProcessed++;

            if (mapProcessed % 1000 == 0)
            {
                Report(
                    progress,
                    "Indexing style maps...",
                    $"{mapProcessed:N0} style maps indexed",
                    88);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new KmlDocumentContext
        {
            SourcePath = originalSourcePath,
            LoadedKmlPath = kmlPath,
            IsKmz = isKmz,
            WorkingDirectory = workingDirectory,
            KmlEntryRelativePath = kmlEntryRelativePath ??
                Path.GetFileName(kmlPath),
            EmbeddedResourceCount = embeddedResourceCount,
            Document = doc,
            RootNode = rootNode,
            Placemarks = placemarks,
            StylesById = stylesById,
            StyleMapsById = styleMapsById
        };
    }

    private KmlTreeNode? ParseElement(
        XElement element,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress,
        ref int parsedNodeCount)
    {
        cancellationToken.ThrowIfCancellationRequested();

        parsedNodeCount++;

        if (parsedNodeCount % 1000 == 0)
        {
            Report(
                progress,
                "Building the folder and placemark tree...",
                $"{parsedNodeCount:N0} tree items prepared",
                52);
        }

        var local = element.Name.LocalName;

        return local switch
        {
            "Document" => ParseContainer(
                element,
                KmlNodeType.Document,
                cancellationToken,
                progress,
                ref parsedNodeCount),
            "Folder" => ParseContainer(
                element,
                KmlNodeType.Folder,
                cancellationToken,
                progress,
                ref parsedNodeCount),
            "Placemark" => ParsePlacemark(element),
            "GroundOverlay" => ParseSimple(element, KmlNodeType.GroundOverlay),
            "NetworkLink" => ParseSimple(element, KmlNodeType.NetworkLink),
            _ => null
        };
    }

    private KmlTreeNode ParseContainer(
        XElement element,
        KmlNodeType nodeType,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress,
        ref int parsedNodeCount)
    {
        var node = new KmlTreeNode
        {
            Name = GetName(element, nodeType.ToString()),
            NodeType = nodeType,
            SourceElement = element,
            IsChecked = false
        };

        foreach (var child in element.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var childNode = ParseElement(
                child,
                cancellationToken,
                progress,
                ref parsedNodeCount);

            if (childNode is not null)
            {
                childNode.Parent = node;
                node.Children.Add(childNode);
            }
        }

        return node;
    }

    private KmlTreeNode ParsePlacemark(XElement element)
    {
        var description = element.Element(KmlNs + "description")?.Value;
        var cleanSubtitle = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Replace("\r", " ").Replace("\n", " ").Trim();

        if (!string.IsNullOrWhiteSpace(cleanSubtitle) &&
            cleanSubtitle.Length > 120)
        {
            cleanSubtitle = cleanSubtitle[..120] + "...";
        }

        return new KmlTreeNode
        {
            Name = GetName(element, "Placemark"),
            Subtitle = cleanSubtitle,
            NodeType = KmlNodeType.Placemark,
            SourceElement = element,
            IsChecked = false
        };
    }

    private KmlTreeNode ParseSimple(XElement element, KmlNodeType nodeType)
    {
        return new KmlTreeNode
        {
            Name = GetName(element, nodeType.ToString()),
            NodeType = nodeType,
            SourceElement = element,
            IsChecked = false
        };
    }

    private static string GetName(XElement element, string fallback)
    {
        var name = element.Element(KmlNs + "name")?.Value;
        return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
    }

    private static int UpdatePlacemarkCounts(
        KmlTreeNode node,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var count = node.NodeType == KmlNodeType.Placemark ? 1 : 0;

        foreach (var child in node.Children)
        {
            count += UpdatePlacemarkCounts(child, cancellationToken);
        }

        node.DescendantPlacemarkCount = count;
        return count;
    }

    private static void Report(
        IProgress<OperationProgress>? progress,
        string message,
        string? detail,
        double? percent)
    {
        progress?.Report(new OperationProgress(message, detail, percent));
    }
}
