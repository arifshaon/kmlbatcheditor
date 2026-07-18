using System.Globalization;
using System.IO;
using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

/// <summary>
/// Previews and applies safe, scoped placemark style changes.
/// Shared styles and StyleMaps are cloned and reused so placemarks outside
/// the selected scope remain unchanged.
/// </summary>
public sealed class KmlBatchEditService
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    public KmlBatchEditPreview Preview(
        IReadOnlyList<XElement> placemarks,
        KmlDocumentContext context,
        KmlBatchEditSettings settings,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? iconHrefOverride = null)
    {
        ArgumentNullException.ThrowIfNull(placemarks);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        if (!TryBuildChangeSet(settings, iconHrefOverride, out var changes, out var error))
        {
            return new KmlBatchEditPreview
            {
                CanApply = false,
                ValidationMessage = error
            };
        }

        if (placemarks.Count == 0)
        {
            return new KmlBatchEditPreview
            {
                CanApply = false,
                ValidationMessage =
                    "Calculate a selection containing at least one placemark first."
            };
        }

        var inlineCount = 0;
        var newInlineCount = 0;
        var unresolvedCount = 0;
        var sharedStyleIds = new HashSet<string>(StringComparer.Ordinal);
        var styleMapIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < placemarks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var placemark = placemarks[index];
            var processed = index + 1;

            if (processed % 250 == 0 || processed == placemarks.Count)
            {
                var percent = processed / (double)Math.Max(placemarks.Count, 1) * 100d;
                progress?.Report(new OperationProgress(
                    "Inspecting selected placemark styles...",
                    $"{processed:N0} of {placemarks.Count:N0} placemarks",
                    percent));
            }

            if (placemark.Element(KmlNs + "Style") is not null)
            {
                inlineCount++;
                continue;
            }

            var styleUrl = CleanValue(
                placemark.Element(KmlNs + "styleUrl")?.Value);

            if (styleUrl is null)
            {
                newInlineCount++;
                continue;
            }

            var styleId = ExtractLocalId(styleUrl);

            if (styleId is not null &&
                context.StylesById.ContainsKey(styleId))
            {
                sharedStyleIds.Add(styleId);
            }
            else if (styleId is not null &&
                     context.StyleMapsById.ContainsKey(styleId))
            {
                styleMapIds.Add(styleId);
            }
            else
            {
                unresolvedCount++;
                newInlineCount++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new KmlBatchEditPreview
        {
            CanApply = true,
            PlacemarkCount = placemarks.Count,
            InlineStyleCount = inlineCount,
            SharedStyleCount = sharedStyleIds.Count,
            StyleMapCount = styleMapIds.Count,
            NewInlineStyleCount = newInlineCount,
            UnresolvedStyleCount = unresolvedCount,
            ChangeSummary = BuildChangeSummary(changes)
        };
    }

    public KmlBatchEditResult Apply(
        IReadOnlyList<XElement> placemarks,
        KmlDocumentContext context,
        KmlBatchEditSettings settings,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? iconHrefOverride = null)
    {
        ArgumentNullException.ThrowIfNull(placemarks);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        if (!TryBuildChangeSet(settings, iconHrefOverride, out var changes, out var error))
            throw new InvalidOperationException(error);

        if (placemarks.Count == 0)
        {
            throw new InvalidOperationException(
                "The current selection does not contain any placemarks.");
        }

        var operation = new ApplyOperation(context, changes);
        var distinctPlacemarks = placemarks.Distinct().ToList();

        for (var index = 0; index < distinctPlacemarks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.ApplyToPlacemark(distinctPlacemarks[index]);

            var processed = index + 1;

            if (processed % 250 == 0 || processed == distinctPlacemarks.Count)
            {
                var percent = processed / (double)Math.Max(distinctPlacemarks.Count, 1) * 90d;
                progress?.Report(new OperationProgress(
                    "Applying scoped style changes...",
                    $"{processed:N0} of {distinctPlacemarks.Count:N0} placemarks",
                    percent));
            }
        }

        progress?.Report(new OperationProgress(
            "Rebuilding style indexes...",
            null,
            95));

        context.RebuildStyleIndexes();
        cancellationToken.ThrowIfCancellationRequested();

        return operation.CreateResult();
    }

    private static bool TryBuildChangeSet(
        KmlBatchEditSettings settings,
        string? iconHrefOverride,
        out StyleChangeSet changes,
        out string error)
    {
        changes = new StyleChangeSet();
        error = string.Empty;

        if (!settings.HasAnyChange)
        {
            error = "Enable at least one style property to change.";
            return false;
        }

        string? iconHref = null;
        string? iconFileName = null;
        string? iconScale = null;
        string? labelScale = null;
        string? iconColor = null;
        string? labelColor = null;

        if (settings.ChangeIconImage)
        {
            var iconPath = settings.IconFilePath?.Trim();

            if (string.IsNullOrWhiteSpace(iconPath))
            {
                error = "Choose an icon image file first.";
                return false;
            }

            if (!File.Exists(iconPath))
            {
                error = "The selected icon image could not be found.";
                return false;
            }

            var supportedExtensions = new HashSet<string>(
                new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" },
                StringComparer.OrdinalIgnoreCase);

            if (!supportedExtensions.Contains(Path.GetExtension(iconPath)))
            {
                error = "The icon must be a PNG, JPG, JPEG, GIF, or BMP image.";
                return false;
            }

            iconHref = !string.IsNullOrWhiteSpace(iconHrefOverride)
                ? iconHrefOverride.Trim()
                : new Uri(Path.GetFullPath(iconPath)).AbsoluteUri;

            iconFileName = Path.GetFileName(iconPath);
        }

        if (settings.ChangeIconScale &&
            !TryNormalizeScale(
                settings.IconScaleText,
                "Icon size",
                out iconScale,
                out error))
        {
            return false;
        }

        if (settings.ChangeLabelScale &&
            !TryNormalizeScale(
                settings.LabelScaleText,
                "Text size",
                out labelScale,
                out error))
        {
            return false;
        }

        if (settings.ChangeIconColor &&
            !TryConvertDisplayColorToKml(
                settings.IconColorText,
                "Icon color",
                out iconColor,
                out error))
        {
            return false;
        }

        if (settings.ChangeLabelColor &&
            !TryConvertDisplayColorToKml(
                settings.LabelColorText,
                "Text color",
                out labelColor,
                out error))
        {
            return false;
        }

        changes = new StyleChangeSet
        {
            IconHref = iconHref,
            IconFileName = iconFileName,
            IconScale = iconScale,
            IconColor = iconColor,
            LabelScale = labelScale,
            LabelColor = labelColor,
            IconColorDisplay = settings.ChangeIconColor
                ? NormalizeDisplayColor(settings.IconColorText)
                : null,
            LabelColorDisplay = settings.ChangeLabelColor
                ? NormalizeDisplayColor(settings.LabelColorText)
                : null
        };

        return true;
    }

    private static bool TryNormalizeScale(
        string? input,
        string propertyName,
        out string? normalized,
        out string error)
    {
        normalized = null;
        error = string.Empty;

        var text = input?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            error = $"{propertyName} is required.";
            return false;
        }

        var parsed = double.TryParse(
                         text,
                         NumberStyles.Float,
                         CultureInfo.InvariantCulture,
                         out var invariantValue)
                     ? invariantValue
                     : double.TryParse(
                         text,
                         NumberStyles.Float,
                         CultureInfo.CurrentCulture,
                         out var currentValue)
                         ? currentValue
                         : double.NaN;

        if (double.IsNaN(parsed) ||
            double.IsInfinity(parsed) ||
            parsed < 0)
        {
            error =
                $"{propertyName} must be a number greater than or equal to zero.";
            return false;
        }

        normalized = parsed.ToString("G17", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryConvertDisplayColorToKml(
        string? input,
        string propertyName,
        out string? kmlColor,
        out string error)
    {
        kmlColor = null;
        error = string.Empty;

        var value = input?.Trim().TrimStart('#');

        if (string.IsNullOrWhiteSpace(value) ||
            (value.Length != 6 && value.Length != 8) ||
            value.Any(character => !Uri.IsHexDigit(character)))
        {
            error =
                $"{propertyName} must use #RRGGBB or #AARRGGBB format.";
            return false;
        }

        value = value.ToUpperInvariant();

        var alpha = value.Length == 8 ? value[..2] : "FF";
        var rgbStart = value.Length == 8 ? 2 : 0;
        var red = value.Substring(rgbStart, 2);
        var green = value.Substring(rgbStart + 2, 2);
        var blue = value.Substring(rgbStart + 4, 2);

        // KML stores colours in AABBGGRR order.
        kmlColor = $"{alpha}{blue}{green}{red}".ToLowerInvariant();
        return true;
    }

    private static string NormalizeDisplayColor(string? input)
    {
        var value = input?.Trim().TrimStart('#').ToUpperInvariant() ?? string.Empty;
        return $"#{value}";
    }

    private static string BuildChangeSummary(StyleChangeSet changes)
    {
        var lines = new List<string>();

        if (changes.IconHref is not null)
            lines.Add($"• Icon image → {changes.IconFileName ?? changes.IconHref}");

        if (changes.IconScale is not null)
            lines.Add($"• Icon size → {changes.IconScale}");

        if (changes.IconColor is not null)
            lines.Add($"• Icon color → {changes.IconColorDisplay}");

        if (changes.LabelScale is not null)
            lines.Add($"• Text size → {changes.LabelScale}");

        if (changes.LabelColor is not null)
            lines.Add($"• Text color → {changes.LabelColorDisplay}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string? ExtractLocalId(string? styleUrl)
    {
        if (string.IsNullOrWhiteSpace(styleUrl))
            return null;

        var value = styleUrl.Trim();
        var hashPosition = value.LastIndexOf('#');

        if (hashPosition >= 0 && hashPosition < value.Length - 1)
            return value[(hashPosition + 1)..];

        if (!value.Contains("://", StringComparison.Ordinal) &&
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

    private sealed class StyleChangeSet
    {
        public string? IconHref { get; init; }

        public string? IconFileName { get; init; }

        public string? IconScale { get; init; }

        public string? IconColor { get; init; }

        public string? LabelScale { get; init; }

        public string? LabelColor { get; init; }

        public string? IconColorDisplay { get; init; }

        public string? LabelColorDisplay { get; init; }
    }

    private sealed class ApplyOperation
    {
        private readonly KmlDocumentContext _context;
        private readonly StyleChangeSet _changes;
        private readonly Dictionary<string, string> _styleCloneIds =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _styleMapCloneIds =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedIds;

        private int _placemarksChanged;
        private int _inlineStylesUpdated;
        private int _sharedStylesCloned;
        private int _styleMapsCloned;
        private int _inlineStylesCreated;
        private int _unresolvedStyleOverrides;

        public ApplyOperation(
            KmlDocumentContext context,
            StyleChangeSet changes)
        {
            _context = context;
            _changes = changes;
            _usedIds = new HashSet<string>(
                context.StylesById.Keys.Concat(context.StyleMapsById.Keys),
                StringComparer.Ordinal);
        }

        public void ApplyToPlacemark(XElement placemark)
        {
            var inlineStyle = placemark.Element(KmlNs + "Style");

            if (inlineStyle is not null)
            {
                ApplyChangesToStyle(inlineStyle, _changes);
                _inlineStylesUpdated++;
                _placemarksChanged++;
                return;
            }

            var styleUrlElement = placemark.Element(KmlNs + "styleUrl");
            var styleId = ExtractLocalId(styleUrlElement?.Value);

            if (styleId is not null &&
                _context.StylesById.ContainsKey(styleId))
            {
                var cloneId = CloneSharedStyle(styleId);
                styleUrlElement!.Value = $"#{cloneId}";
            }
            else if (styleId is not null &&
                     _context.StyleMapsById.ContainsKey(styleId))
            {
                var cloneId = CloneStyleMap(styleId);
                styleUrlElement!.Value = $"#{cloneId}";
            }
            else
            {
                var createdStyle = CreateInlineStyle(placemark);
                ApplyChangesToStyle(createdStyle, _changes);
                _inlineStylesCreated++;

                if (styleUrlElement is not null)
                    _unresolvedStyleOverrides++;
            }

            _placemarksChanged++;
        }

        public KmlBatchEditResult CreateResult()
        {
            return new KmlBatchEditResult
            {
                PlacemarksChanged = _placemarksChanged,
                InlineStylesUpdated = _inlineStylesUpdated,
                SharedStylesCloned = _sharedStylesCloned,
                StyleMapsCloned = _styleMapsCloned,
                InlineStylesCreated = _inlineStylesCreated,
                UnresolvedStyleOverrides = _unresolvedStyleOverrides
            };
        }

        private string CloneSharedStyle(string originalId)
        {
            if (_styleCloneIds.TryGetValue(originalId, out var existingCloneId))
                return existingCloneId;

            var original = _context.StylesById[originalId];
            var clone = new XElement(original);
            var newId = CreateUniqueId($"{originalId}_edited");

            clone.SetAttributeValue("id", newId);
            ApplyChangesToStyle(clone, _changes);
            original.AddAfterSelf(clone);

            _styleCloneIds.Add(originalId, newId);
            _context.StylesById[newId] = clone;
            _sharedStylesCloned++;

            return newId;
        }

        private string CloneStyleMap(string originalId)
        {
            if (_styleMapCloneIds.TryGetValue(originalId, out var existingCloneId))
                return existingCloneId;

            var original = _context.StyleMapsById[originalId];
            var clone = new XElement(original);
            var newId = CreateUniqueId($"{originalId}_edited");

            clone.SetAttributeValue("id", newId);

            // Register before following nested maps so circular references
            // cannot cause endless recursion.
            _styleMapCloneIds.Add(originalId, newId);
            _context.StyleMapsById[newId] = clone;
            original.AddAfterSelf(clone);
            _styleMapsCloned++;

            foreach (var pair in clone.Elements(KmlNs + "Pair"))
                ApplyChangesToPair(pair);

            return newId;
        }

        private void ApplyChangesToPair(XElement pair)
        {
            var inlineStyle = pair.Element(KmlNs + "Style");

            if (inlineStyle is not null)
            {
                ApplyChangesToStyle(inlineStyle, _changes);
                return;
            }

            var styleUrlElement = pair.Element(KmlNs + "styleUrl");
            var styleId = ExtractLocalId(styleUrlElement?.Value);

            if (styleId is not null &&
                _context.StylesById.ContainsKey(styleId))
            {
                styleUrlElement!.Value = $"#{CloneSharedStyle(styleId)}";
                return;
            }

            if (styleId is not null &&
                _context.StyleMapsById.ContainsKey(styleId))
            {
                styleUrlElement!.Value = $"#{CloneStyleMap(styleId)}";
                return;
            }

            var createdStyle = new XElement(KmlNs + "Style");

            if (styleUrlElement is not null)
                styleUrlElement.AddAfterSelf(createdStyle);
            else
                pair.Add(createdStyle);

            ApplyChangesToStyle(createdStyle, _changes);
        }

        private string CreateUniqueId(string baseId)
        {
            var candidate = baseId;
            var counter = 2;

            while (!_usedIds.Add(candidate))
            {
                candidate = $"{baseId}_{counter}";
                counter++;
            }

            return candidate;
        }
    }

    private static XElement CreateInlineStyle(XElement placemark)
    {
        var style = new XElement(KmlNs + "Style");
        var styleUrl = placemark.Element(KmlNs + "styleUrl");

        if (styleUrl is not null)
        {
            styleUrl.AddAfterSelf(style);
            return style;
        }

        var firstFollowingFeatureElement = placemark
            .Elements()
            .FirstOrDefault(element =>
                IsAfterStyleSelectorInFeatureOrder(element.Name.LocalName));

        if (firstFollowingFeatureElement is not null)
            firstFollowingFeatureElement.AddBeforeSelf(style);
        else
            placemark.Add(style);

        return style;
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

    private static void ApplyChangesToStyle(
        XElement style,
        StyleChangeSet changes)
    {
        if (changes.IconHref is not null ||
            changes.IconColor is not null ||
            changes.IconScale is not null)
        {
            var iconStyle = GetOrCreateSubStyle(style, "IconStyle");

            if (changes.IconHref is not null)
            {
                var icon = GetOrCreateOrderedChild(
                    iconStyle,
                    "Icon",
                    "color",
                    "colorMode",
                    "scale",
                    "heading",
                    "Icon",
                    "hotSpot");

                SetOrderedChildValue(
                    icon,
                    "href",
                    changes.IconHref,
                    "href",
                    "refreshMode",
                    "refreshInterval",
                    "viewRefreshMode",
                    "viewRefreshTime",
                    "viewBoundScale",
                    "viewFormat",
                    "httpQuery");
            }

            if (changes.IconColor is not null)
            {
                SetOrderedChildValue(
                    iconStyle,
                    "color",
                    changes.IconColor,
                    "color",
                    "colorMode",
                    "scale",
                    "heading",
                    "Icon",
                    "hotSpot");
            }

            if (changes.IconScale is not null)
            {
                SetOrderedChildValue(
                    iconStyle,
                    "scale",
                    changes.IconScale,
                    "color",
                    "colorMode",
                    "scale",
                    "heading",
                    "Icon",
                    "hotSpot");
            }
        }

        if (changes.LabelColor is not null ||
            changes.LabelScale is not null)
        {
            var labelStyle = GetOrCreateSubStyle(style, "LabelStyle");

            if (changes.LabelColor is not null)
            {
                SetOrderedChildValue(
                    labelStyle,
                    "color",
                    changes.LabelColor,
                    "color",
                    "colorMode",
                    "scale");
            }

            if (changes.LabelScale is not null)
            {
                SetOrderedChildValue(
                    labelStyle,
                    "scale",
                    changes.LabelScale,
                    "color",
                    "colorMode",
                    "scale");
            }
        }
    }

    private static XElement GetOrCreateSubStyle(
        XElement style,
        string localName)
    {
        var existing = style.Element(KmlNs + localName);

        if (existing is not null)
            return existing;

        var order = new[]
        {
            "IconStyle",
            "LabelStyle",
            "LineStyle",
            "PolyStyle",
            "BalloonStyle",
            "ListStyle"
        };

        var newElement = new XElement(KmlNs + localName);
        var targetIndex = Array.IndexOf(order, localName);

        var nextElement = style
            .Elements()
            .FirstOrDefault(element =>
            {
                var elementIndex = Array.IndexOf(order, element.Name.LocalName);
                return elementIndex > targetIndex;
            });

        if (nextElement is not null)
            nextElement.AddBeforeSelf(newElement);
        else
            style.Add(newElement);

        return newElement;
    }

    private static XElement GetOrCreateOrderedChild(
        XElement parent,
        string localName,
        params string[] order)
    {
        var existing = parent.Element(KmlNs + localName);

        if (existing is not null)
            return existing;

        var newElement = new XElement(KmlNs + localName);
        var targetIndex = Array.IndexOf(order, localName);

        var nextElement = parent
            .Elements()
            .FirstOrDefault(element =>
            {
                var elementIndex = Array.IndexOf(order, element.Name.LocalName);
                return elementIndex > targetIndex;
            });

        if (nextElement is not null)
            nextElement.AddBeforeSelf(newElement);
        else
            parent.Add(newElement);

        return newElement;
    }

    private static void SetOrderedChildValue(
        XElement parent,
        string localName,
        string value,
        params string[] order)
    {
        var existing = parent.Element(KmlNs + localName);

        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        var newElement = new XElement(KmlNs + localName, value);
        var targetIndex = Array.IndexOf(order, localName);

        var nextElement = parent
            .Elements()
            .FirstOrDefault(element =>
            {
                var elementIndex = Array.IndexOf(order, element.Name.LocalName);
                return elementIndex > targetIndex;
            });

        if (nextElement is not null)
            nextElement.AddBeforeSelf(newElement);
        else
            parent.Add(newElement);
    }
}
