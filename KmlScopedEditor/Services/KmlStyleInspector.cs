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
            var resolver = context.StyleResolver;
            var resolved = resolver.Resolve(placemark, styleUrl);

            if (resolved.Status == StyleReferenceStatus.Style)
            {
                referencedStyle = resolved.Selector;
                styleSource = resolved.RecoveredDuplicate
                    ? "Shared Style (duplicate ID recovered by Document)"
                    : "Shared Style";
            }
            else if (resolved.Status == StyleReferenceStatus.StyleMap)
            {
                var map = resolved.Selector!;
                var valid = resolver.ValidateStyleMap(map);
                if (valid.IsResolved)
                {
                    styleSource = resolved.RecoveredDuplicate
                        ? "StyleMap (duplicate ID recovered by Document)"
                        : "StyleMap";

                    var normalPair = FindPair(map, "normal");
                    var highlightPair = FindPair(map, "highlight");
                    normalStyleUrl = GetPairStyleUrl(normalPair);
                    highlightStyleUrl = GetPairStyleUrl(highlightPair);
                    referencedStyle = ResolvePairStyle(
                        normalPair, resolver, new HashSet<XElement> { map });
                }
                else
                {
                    styleSource = $"Unresolved StyleMap ({valid.Status})";
                }
            }
            else
            {
                styleSource = resolved.Status == StyleReferenceStatus.External
                    ? "External style reference (not resolved locally)"
                    : $"Unresolved style reference ({resolved.Status})";
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
                referencedValues.LabelColor,

            LineColor =
                inlineValues.LineColor ??
                referencedValues.LineColor,

            LineWidth =
                inlineValues.LineWidth ??
                referencedValues.LineWidth
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
        KmlStyleReferenceResolver resolver,
        HashSet<XElement> visitedMaps)
    {
        if (pair is null)
            return null;

        var inlineStyle = pair.Element(KmlNs + "Style");
        if (inlineStyle is not null)
            return inlineStyle;

        var result = resolver.Resolve(pair, GetPairStyleUrl(pair));
        if (result.Status == StyleReferenceStatus.Style)
            return result.Selector;

        if (result.Status != StyleReferenceStatus.StyleMap ||
            !visitedMaps.Add(result.Selector!))
            return null;

        var nestedPair = FindPair(result.Selector!, "normal");
        return ResolvePairStyle(nestedPair, resolver, visitedMaps);
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

        var lineStyle =
            style.Element(KmlNs + "LineStyle");

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
                    .Value),

            LineColor = CleanValue(
                lineStyle?
                    .Element(KmlNs + "color")?
                    .Value),

            LineWidth = CleanValue(
                lineStyle?
                    .Element(KmlNs + "width")?
                    .Value)
        };
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

        public string? LineColor { get; init; }

        public string? LineWidth { get; init; }
    }
}