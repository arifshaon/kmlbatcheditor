using System.Collections.ObjectModel;
using KmlScopedEditor.ViewModels;
using System.Xml.Linq;

namespace KmlScopedEditor.Models;

public class KmlTreeNode : ViewModelBase
{
    private bool? _isChecked = false;
    private bool _isExpanded;
    private bool _isSelected;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public KmlNodeType NodeType { get; set; }
    public XElement? SourceElement { get; set; }

    public ObservableCollection<KmlTreeNode> Children { get; } = new();
    public KmlTreeNode? Parent { get; set; }
    public int DescendantPlacemarkCount { get; set; }

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
            {
                if (value.HasValue)
                {
                    foreach (var child in Children)
                    {
                        child.IsChecked = value.Value;
                    }
                }

                Parent?.RefreshCheckStateFromChildren();
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void RefreshCheckStateFromChildren()
    {
        if (Children.Count == 0)
            return;

        var first = Children[0].IsChecked;
        var allSame = Children.All(c => c.IsChecked == first);
        _isChecked = allSame ? first : null;
        OnPropertyChanged(nameof(IsChecked));
        Parent?.RefreshCheckStateFromChildren();
    }
}