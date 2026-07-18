namespace KmlScopedEditor.Models;

public sealed class SelectionModeOption
{
    public PlacemarkSelectionMode Value { get; init; }

    public string Label { get; init; } = string.Empty;
}