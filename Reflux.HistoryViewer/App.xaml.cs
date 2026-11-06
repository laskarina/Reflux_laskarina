using System.Windows;
using System.Windows.Threading;
using Reflux.HistoryViewer.Services;

namespace Reflux.HistoryViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        var settings = ViewerSettings.Load();
        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();
    }

    static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "Reflux History Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
