namespace KmlScopedEditor.Models;

public sealed class KmlBatchEditPreview
{
    public bool CanApply { get; init; }

    public string ValidationMessage { get; init; } = string.Empty;

    public int PlacemarkCount { get; init; }

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
            if (!CanApply)
                return ValidationMessage;

            var lines = new List<string>
            {
                $"Matched placemarks: {PlacemarkCount:N0}",
                string.Empty,
                "Changes:",
                ChangeSummary,
                string.Empty,
                "Expected style operations:",
                $"• {InlineStyleCount:N0} placemarks with inline styles will be updated",
                $"• {SharedStyleCount:N0} shared styles will be cloned",
                $"• {StyleMapCount:N0} style maps will be cloned",
                $"• {NewInlineStyleCount:N0} placemarks will receive a new inline style"
            };

            if (UnresolvedStyleCount > 0)
            {
                lines.Add(
                    $"• {UnresolvedStyleCount:N0} unresolved style references will receive safe inline overrides");
            }

            lines.Add(string.Empty);
            lines.Add("Only the enabled properties will be changed.");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
