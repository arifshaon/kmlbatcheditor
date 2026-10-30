using System.Windows;

namespace KmlScopedEditor.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        VersionText.Text =
            $"Version {AppInfo.Version}";
    }
}
