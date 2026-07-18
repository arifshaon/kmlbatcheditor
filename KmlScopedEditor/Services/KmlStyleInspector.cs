using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

public sealed class KmlStyleInspector
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    public KmlResolvedStyle Inspect(
        XElement placemark,
        KmlDocumentContext context)
    {
        var styleUrl = CleanValue(
            placemark.Element(KmlNs + "styleUrl")?.Value);

        var inlineStyle = placemark.Element(KmlNs + "Style");

        XElement? referencedStyle = null;

        string styleSource = "No explicit style";
        string? normalStyleUrl = null;
        string? highlightStyleUrl = null;

        if (!string.IsNullOrWhiteSpace(styleUrl))
        {
            var styleId = ExtractLocalId(styleUrl);

            if (styleId is not null &&
                context.StylesById.TryGetValue(
                    styleId,
                    out var sharedStyle))
            {
                referencedStyle = sharedStyle;
                styleSource = "Shared Style";
            }
            else if (styleId is not null &&
                     context.StyleMapsById.TryGetValue(
                         styleId,
                         out var styleMap))
            {
                styleSource = "StyleMap";

                var normalPair = FindPair(styleMap, "normal");
                var highlightPair = FindPair(styleMap, "highlight");

                normalStyleUrl = GetPairStyleUrl(normalPair);
                highlightStyleUrl = GetPairStyleUrl(highlightPair);

                referencedStyle = ResolvePairStyle(
                    normalPair,
                    context);
            }
            else
            {
                styleSource = "Unresolved style reference";
            }
        }

        if (inlineStyle is not null)
        {
            styleSource = styleSource == "No explicit style"
                ? "Inline Style"
                : $"{styleSource} + inline overrides";
        }

        var referencedValues = ReadStyleValues(referencedStyle);
        var inlineValues = ReadStyleValues(inlineStyle);

        return new KmlResolvedStyle
        {
            StyleSource = styleSource,
            StyleUrl = styleUrl,
            NormalStyleUrl = normalStyleUrl,
            HighlightStyleUrl = highlightStyleUrl,
            HasInlineStyle = inlineStyle is not null,

            IconHref =
                inlineValues.IconHref ??
                referencedValues.IconHref,

            IconScale =
                inlineValues.IconScale ??
                referencedValues.IconScale,

            IconColor =
                inlineValues.IconColor ??
                referencedValues.IconColor,

            LabelScale =
                inlineValues.LabelScale ??
                referencedValues.LabelScale,

            LabelColor =
                inlineValues.LabelColor ??
                referencedValues.LabelColor
        };
    }

    private static XElement? FindPair(
        XElement styleMap,
        string key)
    {
        return styleMap
            .Elements(KmlNs + "Pair")
            .FirstOrDefault(pair =>
                string.Equals(
                    CleanValue(
                        pair.Element(KmlNs + "key")?.Value),
                    key,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetPairStyleUrl(XElement? pair)
    {
        return CleanValue(
            pair?.Element(KmlNs + "styleUrl")?.Value);
    }

    private static XElement? ResolvePairStyle(
        XElement? pair,
        KmlDocumentContext context)
    {
        if (pair is null)
            return null;

        // A StyleMap Pair can contain an inline Style.
        var inlineStyle = pair.Element(KmlNs + "Style");

        if (inlineStyle is not null)
            return inlineStyle;

        var pairStyleUrl = GetPairStyleUrl(pair);
        var styleId = ExtractLocalId(pairStyleUrl);

        if (styleId is null)
            return null;

        if (context.StylesById.TryGetValue(
            styleId,
            out var sharedStyle))
        {
            return sharedStyle;
        }

        // Support a StyleMap pointing to another StyleMap.
        if (context.StyleMapsById.TryGetValue(
            styleId,
            out var nestedStyleMap))
        {
            var nestedNormalPair =
                FindPair(nestedStyleMap, "normal");

            return ResolvePairStyle(
                nestedNormalPair,
                context);
        }

        return null;
    }

    private static StyleValues ReadStyleValues(
        XElement? style)
    {
        if (style is null)
            return new StyleValues();

        var iconStyle =
            style.Element(KmlNs + "IconStyle");

        var labelStyle =
            style.Element(KmlNs + "LabelStyle");

        return new StyleValues
        {
            IconHref = CleanValue(
                iconStyle?
                    .Element(KmlNs + "Icon")?
                    .Element(KmlNs + "href")?
                    .Value),

            IconScale = CleanValue(
                iconStyle?
                    .Element(KmlNs + "scale")?
                    .Value),

            IconColor = CleanValue(
                iconStyle?
                    .Element(KmlNs + "color")?
                    .Value),

            LabelScale = CleanValue(
                labelStyle?
                    .Element(KmlNs + "scale")?
                    .Value),

            LabelColor = CleanValue(
                labelStyle?
                    .Element(KmlNs + "color")?
                    .Value)
        };
    }

    private static string? ExtractLocalId(
        string? styleUrl)
    {
        if (string.IsNullOrWhiteSpace(styleUrl))
            return null;

        var value = styleUrl.Trim();
        var hashPosition = value.LastIndexOf('#');

        if (hashPosition >= 0 &&
            hashPosition < value.Length - 1)
        {
            return value[(hashPosition + 1)..];
        }

        // Also tolerate plain local IDs.
        if (!value.Contains("://") &&
            !value.Contains('/'))
        {
            return value.TrimStart('#');
        }

        return null;
    }

    private static string? CleanValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed class StyleValues
    {
        public string? IconHref { get; init; }

        public string? IconScale { get; init; }

        public string? IconColor { get; init; }

        public string? LabelScale { get; init; }

        public string? LabelColor { get; init; }
    }
}