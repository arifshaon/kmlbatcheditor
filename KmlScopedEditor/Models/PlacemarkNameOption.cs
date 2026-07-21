using System.Xml.Linq;
using KmlScopedEditor.ViewModels;

namespace KmlScopedEditor.Models;

/// <summary>
/// Represents all placemarks sharing the same name. Names are grouped using
/// case-insensitive comparison while preserving the first spelling for display.
/// </summary>
public sealed class PlacemarkNameOption : ViewModelBase
{
    private bool _isSelected;

    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

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
