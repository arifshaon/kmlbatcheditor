using System.IO;
using KmlScopedEditor.ViewModels;

namespace KmlScopedEditor.Models;

/// <summary>
/// User-selected style changes. A property is changed only when its
/// corresponding Change... flag is enabled.
/// </summary>
public sealed class KmlBatchEditSettings : ViewModelBase
{
    private bool _changeIconImage;
    private string _iconFilePath = string.Empty;
    private string _iconFileNameDisplay = "No icon file selected";
    private bool _changeIconScale;
    private string _iconScaleText = "1";
    private bool _changeIconColor;
    private string _iconColorText = "#FFFFFF";
    private bool _changeIconOpacity;
    private string _iconOpacityText = "100";
    private bool _changeLabelScale;
    private string _labelScaleText = "1";
    private bool _changeLabelColor;
    private string _labelColorText = "#FFFFFF";
    private bool _changeLabelOpacity;
    private string _labelOpacityText = "100";

    public bool ChangeIconImage
    {
        get => _changeIconImage;
        set
        {
            if (SetProperty(ref _changeIconImage, value))
                OnPropertyChanged(nameof(HasAnyChange));
        }
    }

    public string IconFilePath
    {
        get => _iconFilePath;
        set
        {
            if (!SetProperty(ref _iconFilePath, value))
                return;

            IconFileNameDisplay = string.IsNullOrWhiteSpace(value)
                ? "No icon file selected"
                : Path.GetFileName(value);
        }
    }

    public string IconFileNameDisplay
    {
        get => _iconFileNameDisplay;
        set => SetProperty(ref _iconFileNameDisplay, value);
    }

    public bool ChangeIconScale
    {
        get => _changeIconScale;
        set
        {
            if (SetProperty(ref _changeIconScale, value))
                OnPropertyChanged(nameof(HasAnyChange));
        }
    }

    public string IconScaleText
    {
        get => _iconScaleText;
        set => SetProperty(ref _iconScaleText, value);
    }

    public bool ChangeIconColor
    {
        get => _changeIconColor;
        set
        {
            if (SetProperty(ref _changeIconColor, value))
                OnPropertyChanged(nameof(HasAnyChange));
        }
    }

    public string IconColorText
    {
        get => _iconColorText;
        set => SetProperty(ref _iconColorText, value);
    }

    public bool ChangeIconOpacity
    {
        get => _changeIconOpacity;
        set
        {
            if (SetProperty(ref _changeIconOpacity, value))
                OnPropertyChanged(nameof(HasAnyChange));
        }
    }

    public string IconOpacityText
    {
        get => _iconOpacityText;
        set => SetProperty(ref _iconOpacityText, value);
    }

    public bool ChangeLabelScale
    {
        get => _changeLabelScale;
        set
        {
            if (SetProperty(ref _changeLabelScale, value))
                OnPropertyChanged(nameof(HasAnyChange));
        }
    }

    public string LabelScaleText
    {
        get => _labelScaleText;
        set => SetProperty(ref _labelScaleText, value);
    }

    public bool ChangeLabelColor
    {
        get => _changeLabelColor;
        set
        {
            if (SetProperty(ref _changeLabelColor, value))
                OnPropertyChanged(nameof(HasAnyChange));
        }
    }

    public string LabelColorText
    {
        get => _labelColorText;
        set => SetProperty(ref _labelColorText, value);
    }

    public bool ChangeLabelOpacity
    {
        get => _changeLabelOpacity;
        set
        {
            if (SetProperty(ref _changeLabelOpacity, value))
                OnPropertyChanged(nameof(HasAnyChange));
        }
    }

    public string LabelOpacityText
    {
        get => _labelOpacityText;
        set => SetProperty(ref _labelOpacityText, value);
    }

    public bool HasAnyChange =>
        ChangeIconImage ||
        ChangeIconScale ||
        ChangeIconColor ||
        ChangeIconOpacity ||
        ChangeLabelScale ||
        ChangeLabelColor ||
        ChangeLabelOpacity;
}
