namespace KmlScopedEditor.Models;

public sealed class KmlBatchEditResult
{
    public int PlacemarksChanged { get; init; }

    public int InlineStylesUpdated { get; init; }

    public int SharedStylesCloned { get; init; }

    public int StyleMapsCloned { get; init; }

    public int InlineStylesCreated { get; init; }

    public int UnresolvedStyleOverrides { get; init; }

    public string Summary
    {
        get
        {
            var lines = new List<string>
            {
                $"Changed {PlacemarksChanged:N0} placemarks.",
                $"Inline styles updated: {InlineStylesUpdated:N0}",
                $"Shared styles cloned: {SharedStylesCloned:N0}",
                $"Style maps cloned: {StyleMapsCloned:N0}",
                $"New inline styles created: {InlineStylesCreated:N0}"
            };

            if (UnresolvedStyleOverrides > 0)
            {
                lines.Add(
                    $"Unresolved references safely overridden inline: {UnresolvedStyleOverrides:N0}");
            }

            lines.Add("The changes are in memory. Use Save As to create the output file.");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
