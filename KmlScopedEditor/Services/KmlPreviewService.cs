using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using KmlScopedEditor.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace KmlScopedEditor.Services;

/// <summary>
/// Builds the in-application before/after placemark preview. This preview
/// approximates Google Earth by applying icon and label scale and colour to a
/// map-like card. The external Google Earth preview remains the final check.
/// </summary>
public sealed class KmlPreviewService
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private readonly KmlStyleInspector _styleInspector;
    private readonly KmlIconAssetService _iconAssetService;

    public KmlPreviewService(
        KmlStyleInspector styleInspector,
        KmlIconAssetService iconAssetService)
    {
        _styleInspector = styleInspector;
        _iconAssetService = iconAssetService;
    }

    public KmlPlacemarkVisualPreview Build(
        XElement placemark,
        KmlDocumentContext context,
        KmlBatchEditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(placemark);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        var resolved = _styleInspector.Inspect(placemark, context);
        var currentLocalPath = _iconAssetService.ResolveLocalIconPath(
            context,
            resolved.IconHref);

        var proposedHref = resolved.IconHref;
        var proposedLocalPath = currentLocalPath;

        if (settings.ChangeIconImage)
        {
            _iconAssetService.ValidateIconFile(settings.IconFilePath);
            proposedLocalPath = Path.GetFullPath(settings.IconFilePath);
            proposedHref = new Uri(proposedLocalPath).AbsoluteUri;
        }

        var currentIconScale = NormalizeScale(resolved.IconScale, 1d);
        var currentLabelScale = NormalizeScale(resolved.LabelScale, 1d);
        var currentIconColor = NormalizeKmlColor(resolved.IconColor);
        var currentLabelColor = NormalizeKmlColor(resolved.LabelColor);

        var proposedIconScale = settings.ChangeIconScale
            ? NormalizeScale(settings.IconScaleText, currentIconScale)
            : currentIconScale;

        var proposedLabelScale = settings.ChangeLabelScale
            ? NormalizeScale(settings.LabelScaleText, currentLabelScale)
            : currentLabelScale;

        var proposedIconColor = BuildProposedColor(
            currentIconColor,
            settings.ChangeIconColor,
            settings.IconColorText,
            settings.ChangeIconOpacity,
            settings.IconOpacityText);

        var proposedLabelColor = BuildProposedColor(
            currentLabelColor,
            settings.ChangeLabelColor,
            settings.LabelColorText,
            settings.ChangeLabelOpacity,
            settings.LabelOpacityText);

        var name = placemark.Element(KmlNs + "name")?.Value?.Trim();

        return new KmlPlacemarkVisualPreview
        {
            PlacemarkName = string.IsNullOrWhiteSpace(name)
                ? "(unnamed placemark)"
                : name,
            Current = BuildAppearance(
                resolved.IconHref,
                currentLocalPath,
                currentIconScale,
                currentLabelScale,
                currentIconColor,
                currentLabelColor),
            Proposed = BuildAppearance(
                proposedHref,
                proposedLocalPath,
                proposedIconScale,
                proposedLabelScale,
                proposedIconColor,
                proposedLabelColor)
        };
    }

    private static KmlPlacemarkAppearancePreview BuildAppearance(
        string? iconHref,
        string? localIconPath,
        double iconScale,
        double labelScale,
        string iconColor,
        string labelColor)
    {
        var iconImage = LoadImage(localIconPath);
        var iconBrush = CreateBrush(iconColor);
        var labelBrush = CreateBrush(labelColor);

        var iconName = !string.IsNullOrWhiteSpace(localIconPath)
            ? Path.GetFileName(localIconPath)
            : GetHrefDisplayName(iconHref);

        var iconScaleText = iconScale.ToString("0.###", CultureInfo.InvariantCulture);
        var labelScaleText = labelScale.ToString("0.###", CultureInfo.InvariantCulture);

        var details = string.Join(
            Environment.NewLine,
            $"Icon: {iconName}",
            $"Icon size: {iconScaleText}",
            $"Icon colour: {KmlColorUtility.ToDisplayRgb(iconColor)}",
            $"Icon opacity: {KmlColorUtility.ToOpacityDisplay(iconColor)}",
            $"Text size: {labelScaleText}",
            $"Text colour: {KmlColorUtility.ToDisplayRgb(labelColor)}",
            $"Text opacity: {KmlColorUtility.ToOpacityDisplay(labelColor)}");

        return new KmlPlacemarkAppearancePreview
        {
            IconHref = iconHref,
            LocalIconPath = localIconPath,
            IconImage = iconImage,
            IconDisplaySize = Math.Clamp(40d * iconScale, 12d, 150d),
            LabelFontSize = Math.Clamp(16d * labelScale, 8d, 48d),
            IconTintBrush = iconBrush,
            IconTintOpacity = IsOpaqueWhite(iconColor) ? 0d : 0.28d,
            LabelBrush = labelBrush,
            KmlIconColor = iconColor,
            KmlLabelColor = labelColor,
            IconScale = iconScaleText,
            LabelScale = labelScaleText,
            Details = details
        };
    }

    private static MediaImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static double NormalizeScale(string? value, double fallback)
    {
        var text = value?.Trim();

        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var invariant) &&
            double.IsFinite(invariant) &&
            invariant >= 0)
        {
            return invariant;
        }

        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var current) &&
            double.IsFinite(current) &&
            current >= 0)
        {
            return current;
        }

        return fallback;
    }

    private static string BuildProposedColor(
        string currentKmlColor,
        bool changeColor,
        string? displayColor,
        bool changeOpacity,
        string? opacityText)
    {
        string? bgr = null;
        string? alpha = null;

        if (changeColor)
        {
            KmlColorUtility.TryParseDisplayRgb(
                displayColor,
                "Colour",
                out bgr,
                out _,
                out _);
        }

        if (changeOpacity)
        {
            KmlColorUtility.TryParseOpacityPercent(
                opacityText,
                "Opacity",
                out alpha,
                out _,
                out _);
        }

        return KmlColorUtility.Combine(currentKmlColor, bgr, alpha);
    }

    private static string NormalizeKmlColor(string? value)
    {
        return KmlColorUtility.NormalizeKmlColor(value);
    }

    private static MediaBrush CreateBrush(string kmlColor)
    {
        var color = NormalizeKmlColor(kmlColor);

        var alpha = byte.Parse(color[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = byte.Parse(color.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = byte.Parse(color.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var red = byte.Parse(color.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        var brush = new MediaSolidColorBrush(MediaColor.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static bool IsOpaqueWhite(string kmlColor)
    {
        return string.Equals(
            NormalizeKmlColor(kmlColor),
            "ffffffff",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHrefDisplayName(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "Google Earth default";

        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? href : name;
        }

        var fileName = Path.GetFileName(href.Replace('/', Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(fileName) ? href : fileName;
    }
}
