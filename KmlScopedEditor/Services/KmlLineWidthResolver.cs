using System.Globalization;
using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

/// <summary>
/// Read-only interpretation of the effective normal-state KML LineStyle width
/// for Line Editor display. Uses exactly the same shared resolver as Style
/// Diagnostics and the batch editor, including duplicate-ID safeguards.
/// </summary>
public sealed class KmlLineWidthResolver
{
    private static readonly XNamespace KmlNs = "http://www.opengis.net/kml/2.2";
    private const double DefaultEditorWidth = 1.1d;
    private readonly KmlDocumentContext _context;
    private readonly KmlStyleInspector _inspector = new();
    private readonly Dictionary<XElement, double> _placemarkWidths = new();

    public KmlLineWidthResolver(XDocument document)
    {
        _context = new KmlDocumentContext
        {
            SourcePath = "(Line Editor)",
            LoadedKmlPath = "(Line Editor)",
            Document = document,
            RootNode = new KmlTreeNode
            {
                Name = "Line Editor",
                NodeType = KmlNodeType.Document,
                SourceElement = document.Root
            }
        };
    }

    public double ResolveWidth(XElement lineString)
    {
        var placemark = lineString.Ancestors(KmlNs + "Placemark").FirstOrDefault();
        if (placemark is null)
            return DefaultEditorWidth;

        if (_placemarkWidths.TryGetValue(placemark, out var width))
            return width;

        var value = _inspector.Inspect(placemark, _context).LineWidth;
        width = double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) &&
                double.IsFinite(parsed) && parsed > 0
            ? Math.Clamp(parsed, 1d, 6d)
            : DefaultEditorWidth;

        _placemarkWidths.Add(placemark, width);
        return width;
    }
}
