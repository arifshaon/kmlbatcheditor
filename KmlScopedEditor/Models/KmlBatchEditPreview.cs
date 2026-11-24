namespace KmlScopedEditor.Models;

public sealed class KmlBatchEditPreview
{
    public bool CanApply { get; init; }

    public string ValidationMessage { get; init; } = string.Empty;

    public int PlacemarkCount { get; init; }

    public int EditablePlacemarkCount { get; init; }

    public int SkippedPlacemarkCount { get; init; }

    public int ExternalStyleReferenceCount { get; init; }

    public int AmbiguousStyleReferenceCount { get; init; }

    public int CyclicStyleReferenceCount { get; init; }

    public int MissingStyleReferenceCount { get; init; }

    public int RecoveredDuplicateReferenceCount { get; init; }

    public int DuplicateIdCount { get; init; }

    public IReadOnlyList<string> SkippedPlacemarkNames { get; init; } =
        Array.Empty<string>();

    public int InlineStyleCount { get; init; }

    public int SharedStyleCount { get; init; }

    public int StyleMapCount { get; init; }

    public int NewInlineStyleCount { get; init; }

    public int UnresolvedStyleCount { get; init; }

    public string ChangeSummary { get; init; } = string.Empty;

    public string Summary
    {
        get
        {
            var lines = new List<string>
            {
                $"Matched placemarks: {PlacemarkCount:N0}",
                $"Safely editable: {EditablePlacemarkCount:N0}",
                $"Skipped (left unchanged): {SkippedPlacemarkCount:N0}"
            };

            if (DuplicateIdCount > 0)
            {
                lines.Add($"⚠ {DuplicateIdCount:N0} duplicate KML ID(s) detected. " +
                    "KML requires unique IDs; ambiguous style references are skipped.");
            }

            if (RecoveredDuplicateReferenceCount > 0)
            {
                lines.Add($"⚠ {RecoveredDuplicateReferenceCount:N0} reference(s) " +
                    "recovered using a unique style in the nearest enclosing Document.");
            }

            if (SkippedPlacemarkCount > 0)
            {
                lines.Add("⚠ Skipped style references: " +
                    $"{ExternalStyleReferenceCount:N0} external, " +
                    $"{AmbiguousStyleReferenceCount:N0} ambiguous, " +
                    $"{CyclicStyleReferenceCount:N0} cyclic, " +
                    $"{MissingStyleReferenceCount:N0} missing/invalid.");
                if (SkippedPlacemarkNames.Count > 0)
                {
                    lines.Add("Examples of skipped placemarks: " +
                        string.Join(", ", SkippedPlacemarkNames) +
                        (SkippedPlacemarkCount > SkippedPlacemarkNames.Count ? ", …" : ""));
                }
            }

            if (!CanApply)
            {
                lines.Add(string.Empty);
                lines.Add(ValidationMessage);
                return string.Join(Environment.NewLine, lines);
            }

            lines.AddRange(new[]
            {
                string.Empty,
                "Changes:",
                ChangeSummary,
                string.Empty,
                "Expected style operations:",
                $"• {InlineStyleCount:N0} placemarks with inline styles will be updated",
                $"• {SharedStyleCount:N0} shared styles will be cloned",
                $"• {StyleMapCount:N0} style maps will be cloned",
                $"• {NewInlineStyleCount:N0} placemarks will receive a new inline style",
                string.Empty,
                "Only enabled properties on safely editable placemarks will change."
            });

            return string.Join(Environment.NewLine, lines);
        }
    }
}
