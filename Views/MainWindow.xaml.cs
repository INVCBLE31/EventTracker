using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EventTracker.Models;
using EventTracker.Services;
using EventTracker.ViewModels;

namespace EventTracker.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private ScrollViewer? _timelineScrollViewer;
    private TrayService? _tray;

    public MainWindow(
        DatabaseService db,
        FileMonitoringService fileSvc,
        ProcessMonitoringService procSvc,
        AIService ai,
        SettingsService settings)
    {
        InitializeComponent();
        _vm = new MainViewModel(db, fileSvc, procSvc, ai, settings);
        DataContext = _vm;

        // PasswordBox.Password cannot be bound in XAML (not a DependencyProperty).
        // Populate it once after DataContext is set and keep in sync via PasswordChanged.
        Loaded += (_, _) =>
        {
            if (ApiKeyBox != null)
                ApiKeyBox.Password = _vm.OpenAIApiKey ?? string.Empty;
        };
    }

    // Sync PasswordBox → ViewModel manually (WPF limitation)
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _vm.OpenAIApiKey = pb.Password;
    }

    public void SetTrayService(TrayService tray)
    {
        _tray = tray;
    }

    // ─── Window chrome ────────────────────────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            MaximizeButton_Click(sender, e);
        else if (WindowState == WindowState.Normal)
        {
            try { DragMove(); } catch { }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Minimize to tray instead of closing
        if (_tray != null && _vm.MinimizeToTray)
            _tray.HideToTray();
        else
            Application.Current.Shutdown();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Override state change: minimize → tray when setting is on
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _tray != null && _vm.MinimizeToTray)
        {
            _tray.HideToTray();
            WindowState = WindowState.Normal; // reset so next Show() works
        }
    }

    // ─── Event row interactions ───────────────────────────────────────────────
    private void EventRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border b)
            b.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1E, 0x33));
        else if (sender is Panel p)
            p.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1E, 0x33));
    }

    private void EventRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = Brushes.Transparent;
        else if (sender is Panel p) p.Background = Brushes.Transparent;
    }

    private void EventRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SystemEvent ev)
        {
            _vm.OpenEventLocationCommand.Execute(ev);
            e.Handled = true;
        }
    }

    // ─── Folder tree ──────────────────────────────────────────────────────────
    private void FolderNode_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderNode node)
        {
            if (!node.IsFile) _vm.LoadFolderChildren(node);
            _ = _vm.SelectFolderCommand.ExecuteAsync(node);
        }
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is FolderNode node)
        {
            if (!node.IsFile) _vm.LoadFolderChildren(node);
            e.Handled = true;
        }
    }

    // ─── Scroll lock ──────────────────────────────────────────────────────────
    private void TimelineListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox lb)
        {
            _timelineScrollViewer = GetScrollViewer(lb);
            if (_timelineScrollViewer != null)
                _timelineScrollViewer.ScrollChanged += TimelineScrollViewer_ScrollChanged;
        }
    }

    private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
        {
            bool atTop = sv.VerticalOffset < 2;
            if (!atTop && !_vm.IsScrollLocked) _vm.SetScrollLocked(true);
            else if (atTop && _vm.IsScrollLocked) _vm.SetScrollLocked(false);
        }
    }

    private void ResumeLiveButton_Click(object sender, MouseButtonEventArgs e)
    {
        _vm.SetScrollLocked(false);
        _timelineScrollViewer?.ScrollToTop();
    }

    // ─── Chat Enter key handler ───────────────────────────────────────────────
    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _vm.SendChatMessageCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ─── Scroll to bottom of chat when new message arrives ───────────────────
    private void ChatListBox_ScrollToBottom(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (sender is ListBox lb)
        {
            lb.Dispatcher.BeginInvoke(() =>
            {
                if (lb.Items.Count > 0)
                    lb.ScrollIntoView(lb.Items[^1]);
            });
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject o)
    {
        if (o is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var child = VisualTreeHelper.GetChild(o, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
