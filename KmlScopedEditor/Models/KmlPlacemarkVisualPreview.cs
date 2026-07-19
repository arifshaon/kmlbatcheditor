using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace KmlScopedEditor.Models;

/// <summary>
/// A lightweight before/after rendering of one matched placemark. The built-in
/// view is intentionally an approximation; the same proposed appearance can
/// also be opened in Google Earth for an exact application-level check.
/// </summary>
public sealed class KmlPlacemarkVisualPreview
{
    public string PlacemarkName { get; init; } = "(unnamed placemark)";

    public KmlPlacemarkAppearancePreview Current { get; init; } = new();

    public KmlPlacemarkAppearancePreview Proposed { get; init; } = new();
}

public sealed class KmlPlacemarkAppearancePreview
{
    public string? IconHref { get; init; }

    public string? LocalIconPath { get; init; }

    public MediaImageSource? IconImage { get; init; }

    public bool HasIconImage => IconImage is not null;

    public double IconDisplaySize { get; init; } = 40d;

    public double IconImageOpacity { get; init; } = 1d;

    public double IconFallbackOpacity { get; init; } = 1d;

    public double LabelFontSize { get; init; } = 16d;

    public MediaBrush IconTintBrush { get; init; } = MediaBrushes.Transparent;

    public double IconTintOpacity { get; init; }

    public MediaBrush LabelBrush { get; init; } = MediaBrushes.White;

    public string KmlIconColor { get; init; } = "ffffffff";

    public string KmlLabelColor { get; init; } = "ffffffff";

    public string IconScale { get; init; } = "1";

    public string LabelScale { get; init; } = "1";

    public string Details { get; init; } = string.Empty;
}
