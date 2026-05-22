using System.IO;
using System.Windows;
using System.Windows.Threading;
using EventTracker.Services;

namespace EventTracker;

public partial class App : Application
{
    private DatabaseService? _databaseService;
    private FileMonitoringService? _fileMonitoringService;
    private ProcessMonitoringService? _processMonitoringService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers — prevents silent crashes
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _databaseService = new DatabaseService();
            _databaseService.Initialize();

            _fileMonitoringService = new FileMonitoringService(_databaseService);
            _processMonitoringService = new ProcessMonitoringService(_databaseService);

            _fileMonitoringService.Start();
            _processMonitoringService.Start();

            var mainWindow = new Views.MainWindow(_databaseService, _fileMonitoringService, _processMonitoringService);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogError(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved(); // Prevent process crash from background tasks
        LogError(e.Exception);
    }

    private static void ShowFatalError(Exception ex)
    {
        var msg = $"EventTracker encountered a fatal error:\n\n{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException != null)
            msg += $"\n\nInner: {ex.InnerException.Message}";
        msg += $"\n\nStack trace:\n{ex.StackTrace}";

        try { LogError(ex); } catch { }

        MessageBox.Show(msg, "EventTracker — Fatal Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EventTracker");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(logPath, entry);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _fileMonitoringService?.Stop();
        _processMonitoringService?.Stop();
        _databaseService?.Dispose();
        base.OnExit(e);
    }
}
