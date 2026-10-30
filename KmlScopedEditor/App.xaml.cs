using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KmlScopedEditor.Views;

namespace KmlScopedEditor;

public partial class App : Application
{
    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        var splash =
            new SplashWindow();

        splash.Show();

        await Dispatcher.InvokeAsync(
            () => { },
            DispatcherPriority.Render);

        var minimumDisplay =
            Task.Delay(1100);

        var mainWindow =
            new MainWindow();

        await minimumDisplay;

        MainWindow =
            mainWindow;

        mainWindow.Show();
        mainWindow.Activate();

        splash.Close();

        ShutdownMode =
            System.Windows.ShutdownMode
                .OnMainWindowClose;
    }
}
