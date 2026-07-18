using System.Xml.Linq;
using KmlScopedEditor.ViewModels;

namespace KmlScopedEditor.Models;

/// <summary>
/// Represents either an icon-image group or a specific icon variant.
/// A variant is defined by icon image, effective icon colour and icon size.
/// </summary>
public sealed class IconTypeOption : ViewModelBase
{
    private bool _isSelected;

    public string Key { get; init; } = string.Empty;

    public string? IconHref { get; init; }

    public string? IconScale { get; init; }

    public string? IconColor { get; init; }

    public bool IsVariant { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string DetailText { get; init; } = string.Empty;

    public IReadOnlyList<XElement> Placemarks { get; init; }
        = Array.Empty<XElement>();

    public int PlacemarkCount => Placemarks.Count;

    public string CountDisplay =>
        PlacemarkCount == 1
            ? "1 placemark"
            : $"{PlacemarkCount:N0} placemarks";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
