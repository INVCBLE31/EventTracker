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
    private AIService? _aiService;
    private SettingsService? _settingsService;
    private TrayService? _trayService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _settingsService = new SettingsService();
            _aiService = new AIService();

            if (!string.IsNullOrWhiteSpace(_settingsService.Settings.OpenAIApiKey))
                _aiService.SetApiKey(_settingsService.Settings.OpenAIApiKey);

            _databaseService = new DatabaseService();
            _databaseService.Initialize();

            _fileMonitoringService = new FileMonitoringService(_databaseService);
            _processMonitoringService = new ProcessMonitoringService(_databaseService);

            _fileMonitoringService.Start();
            _processMonitoringService.Start();

            var mainWindow = new Views.MainWindow(
                _databaseService,
                _fileMonitoringService,
                _processMonitoringService,
                _aiService,
                _settingsService);

            MainWindow = mainWindow;

            // Init tray BEFORE showing window
            _trayService = new TrayService(mainWindow);
            _trayService.PauseRequested += OnTrayPauseRequested;

            // Pass tray to window
            mainWindow.SetTrayService(_trayService);

            if (_settingsService.Settings.StartMinimized)
                _trayService.HideToTray();
            else
                mainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
            Shutdown(1);
        }
    }

    private void OnTrayPauseRequested(object? sender, EventArgs e)
    {
        // Toggle monitoring pause — could be wired to VM
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) LogError(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        LogError(e.Exception);
    }

    private static void ShowFatalError(Exception ex)
    {
        var msg = $"EventTracker encountered a fatal error:\n\n{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException != null)
            msg += $"\n\nInner: {ex.InnerException.Message}";
        msg += $"\n\nStack trace:\n{ex.StackTrace}";
        try { LogError(ex); } catch { }
        MessageBox.Show(msg, "EventTracker — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        _trayService?.Dispose();
        _fileMonitoringService?.Stop();
        _processMonitoringService?.Stop();
        _databaseService?.Dispose();
        base.OnExit(e);
    }
}
