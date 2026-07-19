using System.Globalization;

namespace KmlScopedEditor.Services;

/// <summary>
/// Converts between user-facing RGB/opacity values and KML AABBGGRR colours.
/// Colour and opacity are intentionally handled separately so a colour-only
/// edit can preserve each style's existing alpha value.
/// </summary>
public static class KmlColorUtility
{
    public const string DefaultKmlColor = "ffffffff";

    public static string NormalizeKmlColor(string? value)
    {
        var color = value?.Trim().TrimStart('#');

        return color is { Length: 8 } && color.All(Uri.IsHexDigit)
            ? color.ToLowerInvariant()
            : DefaultKmlColor;
    }

    public static bool TryParseDisplayRgb(
        string? input,
        string propertyName,
        out string? kmlBgr,
        out string? displayRgb,
        out string error)
    {
        kmlBgr = null;
        displayRgb = null;
        error = string.Empty;

        var value = input?.Trim().TrimStart('#');

        if (string.IsNullOrWhiteSpace(value) ||
            (value.Length != 6 && value.Length != 8) ||
            value.Any(character => !Uri.IsHexDigit(character)))
        {
            error = $"{propertyName} must use #RRGGBB or #AARRGGBB format.";
            return false;
        }

        value = value.ToUpperInvariant();
        var rgb = value.Length == 8 ? value[2..] : value;
        var red = rgb[..2];
        var green = rgb.Substring(2, 2);
        var blue = rgb.Substring(4, 2);

        kmlBgr = $"{blue}{green}{red}".ToLowerInvariant();
        displayRgb = $"#{rgb}";
        return true;
    }

    public static bool TryParseOpacityPercent(
        string? input,
        string propertyName,
        out string? alphaHex,
        out string? displayValue,
        out string error)
    {
        alphaHex = null;
        displayValue = null;
        error = string.Empty;

        var value = input?.Trim().TrimEnd('%').Trim();
        var parsed = double.TryParse(
                         value,
                         NumberStyles.Float,
                         CultureInfo.InvariantCulture,
                         out var invariantValue)
                     ? invariantValue
                     : double.TryParse(
                         value,
                         NumberStyles.Float,
                         CultureInfo.CurrentCulture,
                         out var currentValue)
                         ? currentValue
                         : double.NaN;

        if (!double.IsFinite(parsed) || parsed < 0d || parsed > 100d)
        {
            error = $"{propertyName} must be a percentage from 0 to 100.";
            return false;
        }

        var alpha = (byte)Math.Round(
            parsed / 100d * byte.MaxValue,
            MidpointRounding.AwayFromZero);

        alphaHex = alpha.ToString("x2", CultureInfo.InvariantCulture);
        displayValue = $"{parsed:0.##}%";
        return true;
    }

    public static string Combine(
        string? existingKmlColor,
        string? replacementKmlBgr,
        string? replacementAlphaHex)
    {
        var existing = NormalizeKmlColor(existingKmlColor);

        var alpha = replacementAlphaHex is { Length: 2 } &&
                    replacementAlphaHex.All(Uri.IsHexDigit)
            ? replacementAlphaHex.ToLowerInvariant()
            : existing[..2];

        var bgr = replacementKmlBgr is { Length: 6 } &&
                  replacementKmlBgr.All(Uri.IsHexDigit)
            ? replacementKmlBgr.ToLowerInvariant()
            : existing[2..];

        return alpha + bgr;
    }

    public static string ToDisplayRgb(string? kmlColor)
    {
        var color = NormalizeKmlColor(kmlColor).ToUpperInvariant();
        return $"#{color.Substring(6, 2)}{color.Substring(4, 2)}{color.Substring(2, 2)}";
    }

    public static string ToDisplayArgb(string? kmlColor)
    {
        var color = NormalizeKmlColor(kmlColor).ToUpperInvariant();
        return $"#{color[..2]}{color.Substring(6, 2)}{color.Substring(4, 2)}{color.Substring(2, 2)}";
    }

    public static byte GetAlphaByte(string? kmlColor)
    {
        var color = NormalizeKmlColor(kmlColor);
        return byte.Parse(
            color[..2],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
    }

    public static string ToOpacityDisplay(string? kmlColor)
    {
        var alpha = GetAlphaByte(kmlColor);
        var percent = alpha / (double)byte.MaxValue * 100d;
        return $"{percent:0.#}%";
    }
}
