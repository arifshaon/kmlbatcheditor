using System.Collections.ObjectModel;
using KmlScopedEditor.ViewModels;
using System.Xml.Linq;

namespace KmlScopedEditor.Models;

public class KmlTreeNode : ViewModelBase
{
    private bool? _isChecked = false;
    private bool _isExpanded;
    private bool _isSelected;
    private Action? _checksChanged;

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
            if (_isChecked == value)
                return;

            if (value.HasValue)
            {
                SetCheckedDown(
                    value.Value);
            }
            else
            {
                _isChecked = null;
                OnPropertyChanged(
                    nameof(IsChecked));
            }

            Parent?.RefreshCheckStateFromChildren();
            _checksChanged?.Invoke();
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

    public void SetChecksChangedCallback(
        Action? callback)
    {
        _checksChanged = callback;

        foreach (var child in Children)
        {
            child.SetChecksChangedCallback(
                callback);
        }
    }

    public void RefreshCheckStateFromChildren()
    {
        if (Children.Count == 0)
            return;

        var first = Children[0].IsChecked;
        var allSame =
            Children.All(
                child =>
                    child.IsChecked == first);

        var newValue =
            allSame
                ? first
                : null;

        if (_isChecked != newValue)
        {
            _isChecked = newValue;
            OnPropertyChanged(
                nameof(IsChecked));
        }

        Parent?.RefreshCheckStateFromChildren();
    }

    private void SetCheckedDown(
        bool value)
    {
        if (_isChecked != value)
        {
            _isChecked = value;
            OnPropertyChanged(
                nameof(IsChecked));
        }

        foreach (var child in Children)
        {
            child.SetCheckedDown(
                value);
        }
    }
}
