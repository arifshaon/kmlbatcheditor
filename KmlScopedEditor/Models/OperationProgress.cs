namespace KmlScopedEditor.Models;

/// <summary>
/// Progress information reported by long-running KML and KMZ operations.
/// Percent is null when the duration cannot be measured reliably.
/// </summary>
public sealed record OperationProgress(
    string Message,
    string? Detail = null,
    double? Percent = null);
