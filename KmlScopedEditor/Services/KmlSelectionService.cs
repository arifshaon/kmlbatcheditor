using System.Globalization;
using System.IO;
using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

public sealed class KmlSelectionService
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private const string NoExplicitIconKey =
        "__NO_EXPLICIT_ICON__";

    private const string DefaultIconColor = "ffffffff";
    private const string DefaultIconScale = "1";

    public IReadOnlyList<IconTypeOption> BuildIconImageInventory(
        KmlDocumentContext context,
        KmlStyleInspector styleInspector,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedIcons = ResolveIcons(
            context,
            styleInspector,
            "Analysing icon images...",
            progress,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var result = resolvedIcons
            .GroupBy(item => item.IconKey, StringComparer.Ordinal)
            .Select(group =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var first = group.First();
                var placemarks = group
                    .Select(item => item.Placemark)
                    .ToList();

                var countDisplay = FormatCount(placemarks.Count);
                var detail = string.IsNullOrWhiteSpace(first.IconHref)
                    ? countDisplay
                    : $"{countDisplay} — {first.IconHref}";

                return new IconTypeOption
                {
                    Key = group.Key,
                    IconHref = first.IconHref,
                    IsVariant = false,
                    DisplayName = GetIconDisplayName(first.IconHref),
                    DetailText = detail,
                    Placemarks = placemarks
                };
            })
            .OrderByDescending(option => option.PlacemarkCount)
            .ThenBy(option => option.DisplayName)
            .ToList();

        progress?.Report(new OperationProgress(
            "Icon image inventory ready.",
            $"{result.Count:N0} icon image groups",
            92));

        return result;
    }

    public IReadOnlyList<IconTypeOption> BuildIconVariantInventory(
        KmlDocumentContext context,
        KmlStyleInspector styleInspector,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedIcons = ResolveIcons(
            context,
            styleInspector,
            "Analysing icon variants...",
            progress,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var result = resolvedIcons
            .GroupBy(item => item.VariantKey, StringComparer.Ordinal)
            .Select(group =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var first = group.First();
                var placemarks = group
                    .Select(item => item.Placemark)
                    .ToList();

                var iconName = GetIconDisplayName(first.IconHref);
                var colorDisplay = FormatKmlColor(first.IconColor);
                var countDisplay = FormatCount(placemarks.Count);

                return new IconTypeOption
                {
                    Key = group.Key,
                    IconHref = first.IconHref,
                    IconScale = first.IconScale,
                    IconColor = first.IconColor,
                    IsVariant = true,
                    DisplayName =
                        $"{iconName} — {colorDisplay}, size {first.IconScale}",
                    DetailText = string.IsNullOrWhiteSpace(first.IconHref)
                        ? countDisplay
                        : $"{countDisplay} — {first.IconHref}",
                    Placemarks = placemarks
                };
            })
            .OrderByDescending(option => option.PlacemarkCount)
            .ThenBy(option => option.DisplayName)
            .ToList();

        progress?.Report(new OperationProgress(
            "Icon variant inventory ready.",
            $"{result.Count:N0} icon variants",
            98));

        return result;
    }

    public IReadOnlyList<XElement> GetPlacemarksForFolder(
        KmlTreeNode selectedNode,
        bool includeSubfolders,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = selectedNode.SourceElement;

        if (source is null)
            return Array.Empty<XElement>();

        if (selectedNode.NodeType == KmlNodeType.Placemark)
            return new[] { source };

        var result = new List<XElement>();
        IEnumerable<XElement> candidates;

        if (includeSubfolders)
        {
            candidates = source.Descendants(KmlNs + "Placemark");
        }
        else if (source.Name == KmlNs + "kml")
        {
            // The artificial top-level tree node points to <kml>. Its
            // immediate child is normally a Document or Folder.
            candidates = source
                .Elements()
                .Where(element =>
                    element.Name == KmlNs + "Document" ||
                    element.Name == KmlNs + "Folder")
                .SelectMany(element =>
                    element.Elements(KmlNs + "Placemark"));
        }
        else
        {
            candidates = source.Elements(KmlNs + "Placemark");
        }

        var processed = 0;

        foreach (var placemark in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(placemark);
            processed++;

            if (processed % 1000 == 0)
            {
                progress?.Report(new OperationProgress(
                    "Collecting placemarks from the selected folder...",
                    $"{processed:N0} placemarks found",
                    null));
            }
        }

        progress?.Report(new OperationProgress(
            "Folder selection calculated.",
            $"{result.Count:N0} placemarks matched",
            100));

        return result;
    }

    public IReadOnlyList<XElement> GetPlacemarksForIconTypes(
        IEnumerable<IconTypeOption> selectedIconTypes,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<XElement>();
        var seen = new HashSet<XElement>();
        var groupCount = 0;

        foreach (var option in selectedIconTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            groupCount++;

            foreach (var placemark in option.Placemarks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (seen.Add(placemark))
                    result.Add(placemark);
            }

            progress?.Report(new OperationProgress(
                "Collecting placemarks from icon groups...",
                $"{groupCount:N0} groups processed; {result.Count:N0} placemarks matched",
                null));
        }

        progress?.Report(new OperationProgress(
            "Icon selection calculated.",
            $"{result.Count:N0} placemarks matched",
            100));

        return result;
    }

    private static IReadOnlyList<ResolvedIcon> ResolveIcons(
        KmlDocumentContext context,
        KmlStyleInspector styleInspector,
        string operationMessage,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var resolved = new List<ResolvedIcon>(context.Placemarks.Count);
        var total = Math.Max(context.Placemarks.Count, 1);

        for (var index = 0; index < context.Placemarks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var placemark = context.Placemarks[index];
            var style = styleInspector.Inspect(placemark, context);
            var iconHref = CleanValue(style.IconHref);
            var iconColor = NormalizeColor(style.IconColor);
            var iconScale = NormalizeScale(style.IconScale);
            var iconKey = GetIconKey(iconHref);

            var variantKey = string.Join(
                "\u001F",
                iconKey,
                iconColor,
                iconScale);

            resolved.Add(new ResolvedIcon(
                placemark,
                iconHref,
                iconColor,
                iconScale,
                iconKey,
                variantKey));

            var processed = index + 1;

            if (processed % 250 == 0 || processed == context.Placemarks.Count)
            {
                var percentage = 88d +
                    (processed / (double)total * 8d);

                progress?.Report(new OperationProgress(
                    operationMessage,
                    $"{processed:N0} of {context.Placemarks.Count:N0} placemarks",
                    Math.Min(percentage, 96d)));
            }
        }

        return resolved;
    }

    private static string GetIconKey(string? iconHref)
    {
        return string.IsNullOrWhiteSpace(iconHref)
            ? NoExplicitIconKey
            : iconHref.Trim();
    }

    private static string NormalizeColor(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DefaultIconColor
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeScale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultIconScale;

        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed.ToString("G17", CultureInfo.InvariantCulture);
        }

        return value.Trim();
    }

    private static string GetIconDisplayName(string? iconHref)
    {
        if (string.IsNullOrWhiteSpace(iconHref))
            return "No explicitly defined icon";

        var value = iconHref.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);

            if (!string.IsNullOrWhiteSpace(fileName))
                return Uri.UnescapeDataString(fileName);
        }

        var localFileName = Path.GetFileName(value);

        return string.IsNullOrWhiteSpace(localFileName)
            ? value
            : localFileName;
    }

    private static string FormatKmlColor(string value)
    {
        if (value.Length != 8)
            return value;

        var alpha = value[..2];
        var blue = value.Substring(2, 2);
        var green = value.Substring(4, 2);
        var red = value.Substring(6, 2);

        return alpha.Equals("ff", StringComparison.OrdinalIgnoreCase)
            ? $"#{red}{green}{blue}"
            : $"#{red}{green}{blue}, alpha {alpha}";
    }

    private static string FormatCount(int count)
    {
        return count == 1
            ? "1 placemark"
            : $"{count:N0} placemarks";
    }

    private static string? CleanValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record ResolvedIcon(
        XElement Placemark,
        string? IconHref,
        string IconColor,
        string IconScale,
        string IconKey,
        string VariantKey);
}
