using System.Windows;

namespace KmlScopedEditor.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text =
            $"Version {AppInfo.Version}";
    }

    private void OnCloseClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
