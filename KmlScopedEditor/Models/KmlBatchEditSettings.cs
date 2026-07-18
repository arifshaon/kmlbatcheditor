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
    private bool _changeIconScale;
    private string _iconScaleText = "1";
    private bool _changeIconColor;
    private string _iconColorText = "#FFFFFF";
    private bool _changeLabelScale;
    private string _labelScaleText = "1";
    private bool _changeLabelColor;
    private string _labelColorText = "#FFFFFF";

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
            if (SetProperty(ref _iconFilePath, value))
                OnPropertyChanged(nameof(IconFileNameDisplay));
        }
    }

    public string IconFileNameDisplay =>
        string.IsNullOrWhiteSpace(IconFilePath)
            ? "No icon file selected"
            : Path.GetFileName(IconFilePath);

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

    /// <summary>
    /// User-facing RGB or ARGB value, for example #FF0000 or #80FF0000.
    /// </summary>
    public string IconColorText
    {
        get => _iconColorText;
        set => SetProperty(ref _iconColorText, value);
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

    /// <summary>
    /// User-facing RGB or ARGB value, for example #FFFFFF or #80FFFFFF.
    /// </summary>
    public string LabelColorText
    {
        get => _labelColorText;
        set => SetProperty(ref _labelColorText, value);
    }

    public bool HasAnyChange =>
        ChangeIconImage ||
        ChangeIconScale ||
        ChangeIconColor ||
        ChangeLabelScale ||
        ChangeLabelColor;
}
