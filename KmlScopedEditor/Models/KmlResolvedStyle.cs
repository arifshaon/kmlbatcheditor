namespace KmlScopedEditor.Models;

public sealed class KmlResolvedStyle
{
    public string StyleSource { get; init; } = "No explicit style";

    public string? StyleUrl { get; init; }

    public string? NormalStyleUrl { get; init; }

    public string? HighlightStyleUrl { get; init; }

    public bool HasInlineStyle { get; init; }

    public string? IconHref { get; init; }

    public string? IconScale { get; init; }

    public string? IconColor { get; init; }

    public string? LabelScale { get; init; }

    public string? LabelColor { get; init; }

    public string StyleUrlDisplay => DisplayValue(StyleUrl);

    public string NormalStyleUrlDisplay => DisplayValue(NormalStyleUrl);

    public string HighlightStyleUrlDisplay => DisplayValue(HighlightStyleUrl);

    public string InlineStyleDisplay => HasInlineStyle ? "Yes" : "No";

    public string IconHrefDisplay => DisplayValue(IconHref);

    public string IconScaleDisplay => DisplayValue(IconScale);

    public string LabelScaleDisplay => DisplayValue(LabelScale);

    public string IconColorDisplay => FormatKmlColor(IconColor);

    public string LabelColorDisplay => FormatKmlColor(LabelColor);

    private static string DisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "Not explicitly set"
            : value;
    }

    private static string FormatKmlColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Not explicitly set";

        var color = value.Trim();

        if (color.Length != 8)
            return color;

        // KML color order is AABBGGRR.
        var alpha = color[..2];
        var blue = color.Substring(2, 2);
        var green = color.Substring(4, 2);
        var red = color.Substring(6, 2);

        return $"{color} (RGB #{red}{green}{blue}, alpha {alpha})";
    }
}