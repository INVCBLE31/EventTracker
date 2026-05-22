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

    public MainWindow(DatabaseService db, FileMonitoringService fileSvc, ProcessMonitoringService procSvc)
    {
        InitializeComponent();
        _vm = new MainViewModel(db, fileSvc, procSvc);
        DataContext = _vm;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            MaximizeButton_Click(sender, e);
        else if (WindowState == WindowState.Normal)
        {
            try { DragMove(); } catch { }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void EventRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border b)
            b.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1E, 0x33));
        else if (sender is Panel p)
            p.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1E, 0x33));
    }

    private void EventRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border b)
            b.Background = Brushes.Transparent;
        else if (sender is Panel p)
            p.Background = Brushes.Transparent;
    }

    // FIX: Click on any event row opens Explorer at the event's location
    private void EventRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SystemEvent ev)
        {
            _vm.OpenEventLocationCommand.Execute(ev);
            e.Handled = true;
        }
    }

    private void FolderNode_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderNode node)
        {
            // FIX: Only expand+load if it's a folder, not a file leaf
            if (!node.IsFile)
                _vm.LoadFolderChildren(node);
            _ = _vm.SelectFolderCommand.ExecuteAsync(node);
        }
    }

    // Lazy-load children when a TreeViewItem expands
    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is FolderNode node)
        {
            if (!node.IsFile)
                _vm.LoadFolderChildren(node);
            e.Handled = true;
        }
    }

    // ---- Scroll lock for timeline ----
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

        // If user scrolled (not a content-size change), check position
        if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
        {
            bool atTop = sv.VerticalOffset < 2; // near top = newest items visible
            bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 1;

            if (!atTop && !_vm.IsScrollLocked)
                _vm.SetScrollLocked(true);
            else if (atTop && _vm.IsScrollLocked)
                _vm.SetScrollLocked(false);
        }
    }

    private void ResumeLiveButton_Click(object sender, MouseButtonEventArgs e)
    {
        _vm.SetScrollLocked(false);
        _timelineScrollViewer?.ScrollToTop();
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
