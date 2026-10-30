using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

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

    private void OnGitHubLinkClick(
        object sender,
        RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
        }
        catch
        {
            System.Windows.MessageBox.Show(
                this,
                $"Unable to open link:\n{e.Uri.AbsoluteUri}",
                "Open link",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        e.Handled = true;
    }
}
