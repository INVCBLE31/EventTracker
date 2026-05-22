using System.Drawing;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfWindowState = System.Windows.WindowState;
using WpfWindow = System.Windows.Window;

namespace EventTracker.Services;

/// <summary>
/// System tray icon — app minimizes to tray instead of closing.
/// </summary>
public class TrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly WpfWindow _mainWindow;
    private bool _disposed;

    public TrayService(WpfWindow mainWindow)
    {
        _mainWindow = mainWindow;
        Initialize();
    }

    private void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "PC Timeline — monitoring active",
            Visible = true,
            Icon = CreateIcon()
        };

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("📊 Open PC Timeline");
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (_, _) => ShowWindow();

        var pauseItem = new ToolStripMenuItem("⏸ Pause monitoring");
        pauseItem.Click += (_, _) =>
        {
            PauseRequested?.Invoke(this, EventArgs.Empty);
            pauseItem.Text = pauseItem.Text.Contains("Pause")
                ? "▶ Resume monitoring"
                : "⏸ Pause monitoring";
        };

        var separator = new ToolStripSeparator();

        var exitItem = new ToolStripMenuItem("✕ Exit");
        exitItem.Click += (_, _) =>
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _notifyIcon!.Visible = false;
                WpfApplication.Current.Shutdown();
            });
        };

        menu.Items.Add(openItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add(separator);
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WpfWindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
        });
    }

    public void HideToTray()
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            _mainWindow.Hide();
            ShowBalloon("PC Timeline", "Monitoring in background. Double-click to open.", 2000);
        });
    }

    public void ShowBalloon(string title, string text, int ms = 3000)
        => _notifyIcon?.ShowBalloonTip(ms, title, text, ToolTipIcon.Info);

    public void UpdateStats(int todayEvents, long totalEvents)
    {
        if (_notifyIcon == null) return;
        _notifyIcon.Text = $"PC Timeline\nToday: {todayEvents} events\nTotal: {totalEvents:N0}";
    }

    public event EventHandler? PauseRequested;

    private static Icon CreateIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var bgBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        g.FillEllipse(bgBrush, 0, 0, 15, 15);
        using var ringPen = new Pen(Color.FromArgb(96, 165, 250), 1.5f);
        g.DrawEllipse(ringPen, 1, 1, 13, 13);
        using var dotBrush = new SolidBrush(Color.White);
        g.FillEllipse(dotBrush, 6, 6, 4, 4);
        using var recBrush = new SolidBrush(Color.FromArgb(248, 113, 113));
        g.FillEllipse(recBrush, 10, 1, 4, 4);
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon?.Dispose();
    }
}
