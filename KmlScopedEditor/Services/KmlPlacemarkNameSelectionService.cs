using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

/// <summary>
/// Builds a case-insensitive placemark-name inventory and resolves checked
/// names back to their source placemark elements.
/// </summary>
public sealed class KmlPlacemarkNameSelectionService
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private const string UnnamedKey = "\u0000";
    private const string UnnamedDisplay = "(unnamed placemark)";

    public IReadOnlyList<PlacemarkNameOption> BuildInventory(
        KmlDocumentContext context,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var groups = new Dictionary<string, NameGroup>(
            StringComparer.OrdinalIgnoreCase);
        var total = Math.Max(context.Placemarks.Count, 1);

        for (var index = 0; index < context.Placemarks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var placemark = context.Placemarks[index];
            var rawName = placemark.Element(KmlNs + "name")?.Value?.Trim();
            var key = string.IsNullOrWhiteSpace(rawName)
                ? UnnamedKey
                : rawName;
            var displayName = string.IsNullOrWhiteSpace(rawName)
                ? UnnamedDisplay
                : rawName;

            if (!groups.TryGetValue(key, out var group))
            {
                group = new NameGroup(key, displayName);
                groups.Add(key, group);
            }

            group.Placemarks.Add(placemark);

            var processed = index + 1;
            if (processed % 1000 == 0 || processed == context.Placemarks.Count)
            {
                progress?.Report(new OperationProgress(
                    "Analysing placemark names...",
                    $"{processed:N0} of {context.Placemarks.Count:N0} placemarks",
                    processed / (double)total * 100d));
            }
        }

        return groups.Values
            .Select(group => new PlacemarkNameOption
            {
                Key = group.Key,
                DisplayName = group.DisplayName,
                Placemarks = group.Placemarks
            })
            .OrderByDescending(option => option.PlacemarkCount)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<XElement> GetPlacemarksForNames(
        IEnumerable<PlacemarkNameOption> selectedNames,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedNames);

        var result = new List<XElement>();
        var seen = new HashSet<XElement>();
        var processedGroups = 0;

        foreach (var option in selectedNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedGroups++;

            foreach (var placemark in option.Placemarks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (seen.Add(placemark))
                    result.Add(placemark);
            }

            progress?.Report(new OperationProgress(
                "Collecting placemarks from selected names...",
                $"{processedGroups:N0} names processed; {result.Count:N0} placemarks matched",
                null));
        }

        progress?.Report(new OperationProgress(
            "Placemark name selection calculated.",
            $"{result.Count:N0} placemarks matched",
            100));

        return result;
    }

    private sealed class NameGroup
    {
        public NameGroup(string key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public List<XElement> Placemarks { get; } = new();
    }
}
