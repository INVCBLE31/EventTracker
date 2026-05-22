using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventTracker.Models;
using EventTracker.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace EventTracker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly FileMonitoringService _fileSvc;
    private readonly ProcessMonitoringService _procSvc;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _statsTimer;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty] private string _currentLanguage = "EN";
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private string _selectedAction = "All";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = "Monitoring active";
    [ObservableProperty] private long _totalEvents;
    [ObservableProperty] private int _todayEvents;
    [ObservableProperty] private string _selectedFolderPath = "";
    [ObservableProperty] private bool _isFolderView;
    [ObservableProperty] private string _currentView = "Timeline";
    [ObservableProperty] private bool _isScrollLocked = false;

    public ObservableCollection<SystemEvent> LiveEvents { get; } = new();
    public ObservableCollection<SystemEvent> SearchResults { get; } = new();
    public ObservableCollection<FolderNode> DriveNodes { get; } = new();
    public ObservableCollection<ActivityStat> ActivityStats { get; } = new();
    public ObservableCollection<ProcessStat> TopProcesses { get; } = new();
    public ObservableCollection<ProcessActivityStat> ProcessActivityStats { get; } = new();

    private readonly Queue<SystemEvent> _pendingLiveEvents = new();
    private HashSet<string> _ignoredProcesses = new(StringComparer.OrdinalIgnoreCase);

    // Maps FullPath → FolderNode for O(1) live tree lookup
    private readonly Dictionary<string, FolderNode> _nodeIndex = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Categories => CurrentLanguage == "RU"
        ? new() { "Все", "Файл", "Папка", "Процесс", "USB", "Сеть", "Ошибка", "Предупреждение" }
        : new() { "All", "File", "Folder", "Process", "USB", "Network", "Error", "Warning" };

    public List<string> Actions => CurrentLanguage == "RU"
        ? new() { "Все", "Создан", "Удалён", "Изменён", "Переименован", "Запущен" }
        : new() { "All", "Created", "Deleted", "Modified", "Renamed", "Started" };

    public string LblTimeline => CurrentLanguage == "RU" ? "Таймлайн" : "Timeline";
    public string LblDashboard => CurrentLanguage == "RU" ? "Дашборд" : "Dashboard";
    public string LblExport => CurrentLanguage == "RU" ? "Экспорт" : "Export";
    public string LblNoiseFilter => CurrentLanguage == "RU" ? "Шум/Процессы" : "Noise Filter";
    public string LblLiveTimeline => CurrentLanguage == "RU" ? "ПРЯМОЙ ЭФИР" : "LIVE TIMELINE";
    public string LblSearchResults => CurrentLanguage == "RU" ? "РЕЗУЛЬТАТЫ ПОИСКА" : "SEARCH RESULTS";
    public string LblSearching => CurrentLanguage == "RU" ? "Поиск..." : "Searching...";
    public string LblSearchPlaceholder => CurrentLanguage == "RU" ? "Поиск процессов, файлов, папок, действий..." : "Search processes, files, folders, actions...";
    public string LblScrollLocked => CurrentLanguage == "RU" ? "🔒 Прокрутка заморожена. Нажмите для продолжения ↓" : "🔒 Scroll paused. Click to resume live ↓";
    public string LblOpenExplorer => CurrentLanguage == "RU" ? "Открыть в проводнике ↗" : "Open in Explorer ↗";
    public string LblRecording => CurrentLanguage == "RU" ? "● ЗАПИСЬ" : "● RECORDING";
    public string LblTodayPrefix => CurrentLanguage == "RU" ? "Сегодня: " : "Today: ";
    public string LblMatches => CurrentLanguage == "RU" ? " совпадений" : " matches";
    public string LblFileSystem => CurrentLanguage == "RU" ? "ФАЙЛОВАЯ СИСТЕМА" : "FILE SYSTEM";
    public string LblNoiseFilterTitle => CurrentLanguage == "RU" ? "ТОП ПРОЦЕССОВ ПО АКТИВНОСТИ" : "TOP PROCESSES BY ACTIVITY";
    public string LblClearAll => CurrentLanguage == "RU" ? "Очистить всё" : "Clear All";
    public string LblIgnore => CurrentLanguage == "RU" ? "Игнор" : "Ignore";
    public string LblClear => CurrentLanguage == "RU" ? "Очистить" : "Clear";
    public string LblIgnored => CurrentLanguage == "RU" ? "В фильтре" : "Ignored";
    public string LblStatusMonitoring => CurrentLanguage == "RU" ? "Мониторинг активен" : "Monitoring active";

    public MainViewModel(DatabaseService db, FileMonitoringService fileSvc, ProcessMonitoringService procSvc)
    {
        _db = db;
        _fileSvc = fileSvc;
        _procSvc = procSvc;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _fileSvc.EventOccurred += OnEventOccurred;
        _procSvc.EventOccurred += OnEventOccurred;

        _ = InitializeAsync();
        InitializeDrives();
        StartStatsTimer();
    }

    private async Task InitializeAsync()
    {
        _ignoredProcesses = await _db.GetIgnoredProcessesAsync();
        _fileSvc.SetIgnoredProcesses(_ignoredProcesses);

        var recent = await _db.GetRecentAsync(200, _ignoredProcesses);
        _dispatcher.Invoke(() =>
        {
            foreach (var ev in recent.AsEnumerable().Reverse())
                LiveEvents.Add(ev);
        });
        await RefreshStatsAsync();
    }

    private void OnEventOccurred(SystemEvent ev)
    {
        if (!string.IsNullOrEmpty(ev.ProcessName) &&
            _ignoredProcesses.Contains(ev.ProcessName))
            return;

        _dispatcher.BeginInvoke(() =>
        {
            // Update live tree sidebar
            UpdateTreeForEvent(ev);

            if (IsScrollLocked)
            {
                _pendingLiveEvents.Enqueue(ev);
                StatusText = CurrentLanguage == "RU"
                    ? $"Буфер: {_pendingLiveEvents.Count} событий"
                    : $"Buffered: {_pendingLiveEvents.Count} events";
            }
            else
            {
                LiveEvents.Insert(0, ev);
                if (LiveEvents.Count > 500)
                    LiveEvents.RemoveAt(LiveEvents.Count - 1);
                TodayEvents++;
                TotalEvents++;
                StatusText = $"Last: {ev.Action} — {ev.FileName ?? ev.ProcessName}";
            }
        });
    }

    // =====================================================================
    // LIVE TREE UPDATE — called on every file system event on the UI thread
    // =====================================================================
    private void UpdateTreeForEvent(SystemEvent ev)
    {
        try
        {
            string path = ev.Path;
            string action = ev.Action;
            bool isDir = ev.Category == "Folder";

            // Find the parent node (directory containing the changed item)
            string parentPath = System.IO.Path.GetDirectoryName(path) ?? "";

            switch (action)
            {
                case "Created":
                    // Add node to parent if parent is loaded in tree
                    if (_nodeIndex.TryGetValue(parentPath, out var parentNode) && parentNode.IsLoaded)
                    {
                        string itemName = System.IO.Path.GetFileName(path);
                        // Don't add duplicates
                        if (!parentNode.Children.Any(c => c.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            var newNode = isDir
                                ? new FolderNode
                                {
                                    Name = "📁 " + itemName,
                                    FullPath = path,
                                    Children = new System.Collections.ObjectModel.ObservableCollection<FolderNode>
                                        { new FolderNode { Name = "..." } }
                                }
                                : new FolderNode
                                {
                                    Name = "📄 " + itemName,
                                    FullPath = path,
                                    IsFile = true
                                };

                            // Insert in sorted order: folders first, then files, alphabetically
                            int insertIdx = FindInsertIndex(parentNode.Children, newNode);
                            parentNode.Children.Insert(insertIdx, newNode);
                            if (!newNode.IsFile)
                                _nodeIndex[path] = newNode;
                        }
                    }
                    break;

                case "Deleted":
                    // Remove node from parent
                    if (_nodeIndex.TryGetValue(parentPath, out var delParent) && delParent.IsLoaded)
                    {
                        var toRemove = delParent.Children.FirstOrDefault(
                            c => c.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                        if (toRemove != null)
                        {
                            delParent.Children.Remove(toRemove);
                            _nodeIndex.Remove(path);
                        }
                    }
                    break;

                case "Renamed":
                    // Rename = old path deleted, new path created
                    // ev.Path is the NEW path, ev.Details contains old→new info
                    // Handle by removing old + adding new in parent
                    if (_nodeIndex.TryGetValue(parentPath, out var renParent) && renParent.IsLoaded)
                    {
                        // Try to find existing node by checking if any child's name matches old filename from Details
                        // Since we only have the new path in ev.Path, remove any child matching old names and add new
                        string newName = System.IO.Path.GetFileName(path);

                        // Remove old (could be any node whose path no longer exists)
                        var stale = renParent.Children
                            .Where(c => !c.IsFile
                                ? !Directory.Exists(c.FullPath)
                                : !File.Exists(c.FullPath))
                            .ToList();
                        foreach (var s in stale)
                        {
                            renParent.Children.Remove(s);
                            _nodeIndex.Remove(s.FullPath);
                        }

                        // Add new if not already present
                        if (!renParent.Children.Any(c => c.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            var renamedNode = isDir
                                ? new FolderNode
                                {
                                    Name = "📁 " + newName,
                                    FullPath = path,
                                    Children = new System.Collections.ObjectModel.ObservableCollection<FolderNode>
                                        { new FolderNode { Name = "..." } }
                                }
                                : new FolderNode
                                {
                                    Name = "📄 " + newName,
                                    FullPath = path,
                                    IsFile = true
                                };
                            int insertIdx = FindInsertIndex(renParent.Children, renamedNode);
                            renParent.Children.Insert(insertIdx, renamedNode);
                            if (!renamedNode.IsFile)
                                _nodeIndex[path] = renamedNode;
                        }
                    }
                    break;
            }
        }
        catch { /* Never crash the UI thread */ }
    }

    // Insert folders before files, both alphabetically
    private static int FindInsertIndex(
        System.Collections.ObjectModel.ObservableCollection<FolderNode> children,
        FolderNode newNode)
    {
        // Skip placeholder "..." node
        if (children.Count == 1 && children[0].Name == "...")
            return 0;

        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            if (c.Name == "...") continue;

            // Folders before files
            if (newNode.IsFile && !c.IsFile) continue;
            if (!newNode.IsFile && c.IsFile) return i;

            // Alphabetical within same type
            if (string.Compare(newNode.Name, c.Name, StringComparison.OrdinalIgnoreCase) < 0)
                return i;
        }
        return children.Count;
    }

    [RelayCommand]
    private void ResumeScroll()
    {
        IsScrollLocked = false;
        while (_pendingLiveEvents.Count > 0)
        {
            var ev = _pendingLiveEvents.Dequeue();
            LiveEvents.Insert(0, ev);
            TodayEvents++;
            TotalEvents++;
        }
        if (LiveEvents.Count > 500)
            while (LiveEvents.Count > 500)
                LiveEvents.RemoveAt(LiveEvents.Count - 1);
        StatusText = LblStatusMonitoring;
    }

    public void SetScrollLocked(bool locked)
    {
        IsScrollLocked = locked;
        if (!locked) ResumeScrollCommand.Execute(null);
    }

    private void InitializeDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;

            string driveLetter = drive.Name.TrimEnd('\\');
            string label = "";
            try { label = drive.VolumeLabel; } catch { }

            string driveTypeIcon = drive.DriveType switch
            {
                DriveType.Fixed => "💾",
                DriveType.Removable => "💿",
                DriveType.Network => "🌐",
                DriveType.CDRom => "📀",
                _ => "🖥"
            };

            string displayName = string.IsNullOrWhiteSpace(label)
                ? $"{driveTypeIcon} {driveLetter}"
                : $"{driveTypeIcon} {driveLetter} — {label}";

            var node = new FolderNode
            {
                Name = displayName,
                FullPath = drive.RootDirectory.FullName,
                IsDrive = true,
                Children = new System.Collections.ObjectModel.ObservableCollection<FolderNode>
                    { new FolderNode { Name = "..." } }
            };
            DriveNodes.Add(node);
            _nodeIndex[drive.RootDirectory.FullName] = node;
        }
    }

    public void LoadFolderChildren(FolderNode node)
    {
        if (node.IsLoaded) return;
        node.IsLoaded = true;
        node.Children.Clear();

        try
        {
            // Folders first
            foreach (var d in Directory.GetDirectories(node.FullPath).OrderBy(x => x).Take(300))
            {
                var child = new FolderNode
                {
                    Name = "📁 " + System.IO.Path.GetFileName(d),
                    FullPath = d,
                    Children = new System.Collections.ObjectModel.ObservableCollection<FolderNode>
                        { new FolderNode { Name = "..." } }
                };
                node.Children.Add(child);
                // Register in index for live updates
                if (!_nodeIndex.ContainsKey(d))
                    _nodeIndex[d] = child;
            }

            // Files after folders
            foreach (var f in Directory.GetFiles(node.FullPath).OrderBy(x => x).Take(200))
            {
                node.Children.Add(new FolderNode
                {
                    Name = "📄 " + System.IO.Path.GetFileName(f),
                    FullPath = f,
                    IsFile = true
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task SelectFolderAsync(FolderNode node)
    {
        SelectedFolderPath = node.FullPath;
        IsFolderView = true;
        CurrentView = "Folder";
        SearchResults.Clear();

        if (node.IsFile)
        {
            var events = await _db.GetFileEventsAsync(node.FullPath);
            _dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var ev in events) SearchResults.Add(ev);
            });
        }
        else
        {
            var events = await _db.GetFolderEventsAsync(node.FullPath);
            _dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var ev in events) SearchResults.Add(ev);
            });
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;
        _ = SearchDebounced(value, cts);
    }

    partial void OnSelectedCategoryChanged(string value) => _ = PerformSearchAsync();
    partial void OnSelectedActionChanged(string value) => _ = PerformSearchAsync();

    private async Task SearchDebounced(string query, CancellationTokenSource cts)
    {
        try { await Task.Delay(300, cts.Token); }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested) return;
        await PerformSearchAsync();
    }

    private async Task PerformSearchAsync()
    {
        var query = SearchQuery;
        var category = (SelectedCategory == "All" || SelectedCategory == "Все") ? null : MapCategoryToEn(SelectedCategory);
        var action = (SelectedAction == "All" || SelectedAction == "Все") ? null : MapActionToEn(SelectedAction);

        if (string.IsNullOrWhiteSpace(query) && category == null && action == null)
        {
            _dispatcher.Invoke(() =>
            {
                CurrentView = "Timeline";
                IsFolderView = false;
            });
            return;
        }

        IsSearching = true;
        CurrentView = "Search";

        try
        {
            var results = await _db.SearchAsync(query, category, action);
            _dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var ev in results) SearchResults.Add(ev);
                StatusText = CurrentLanguage == "RU"
                    ? $"Найдено {results.Count} результатов"
                    : $"Found {results.Count} results";
            });
        }
        finally { IsSearching = false; }
    }

    private string? MapCategoryToEn(string cat) => cat switch
    {
        "Файл" => "File",
        "Папка" => "Folder",
        "Процесс" => "Process",
        "Сеть" => "Network",
        "Ошибка" => "Error",
        "Предупреждение" => "Warning",
        _ => cat
    };

    private string? MapActionToEn(string act) => act switch
    {
        "Создан" => "Created",
        "Удалён" => "Deleted",
        "Изменён" => "Modified",
        "Переименован" => "Renamed",
        "Запущен" => "Started",
        _ => act
    };

    [RelayCommand]
    private async Task ShowDashboardAsync()
    {
        CurrentView = "Dashboard";
        await RefreshStatsAsync();
    }

    [RelayCommand]
    private void ShowTimeline()
    {
        SearchQuery = "";
        CurrentView = "Timeline";
        IsFolderView = false;
    }

    [RelayCommand]
    private async Task ShowNoiseFilterAsync()
    {
        CurrentView = "NoiseFilter";
        await RefreshNoiseFilterAsync();
    }

    public async Task RefreshNoiseFilterAsync()
    {
        _ignoredProcesses = await _db.GetIgnoredProcessesAsync();
        _fileSvc.SetIgnoredProcesses(_ignoredProcesses);

        var stats = await _db.GetProcessActivityStatsAsync(50);
        _dispatcher.Invoke(() =>
        {
            ProcessActivityStats.Clear();
            foreach (var s in stats)
                ProcessActivityStats.Add(s);
        });
    }

    [RelayCommand]
    private async Task ToggleIgnoreProcessAsync(string processName)
    {
        if (_ignoredProcesses.Contains(processName))
        {
            await _db.RemoveIgnoredProcessAsync(processName);
            _ignoredProcesses.Remove(processName);
        }
        else
        {
            await _db.AddIgnoredProcessAsync(processName);
            _ignoredProcesses.Add(processName);
        }
        _fileSvc.SetIgnoredProcesses(_ignoredProcesses);
        await RefreshNoiseFilterAsync();
    }

    [RelayCommand]
    private async Task ClearProcessEventsAsync(string processName)
    {
        var msg = CurrentLanguage == "RU"
            ? $"Удалить все события для '{processName}'?"
            : $"Clear all events for '{processName}'?";
        if (MessageBox.Show(msg, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await _db.ClearProcessEventsAsync(processName);
            var toRemove = LiveEvents.Where(e => e.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var e in toRemove) LiveEvents.Remove(e);
            await RefreshNoiseFilterAsync();
        }
    }

    [RelayCommand]
    private async Task ClearAllEventsAsync()
    {
        var msg = CurrentLanguage == "RU"
            ? "Удалить ВСЕ записанные события?"
            : "Clear ALL recorded events?";
        if (MessageBox.Show(msg, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await _db.ClearAllEventsAsync();
            _dispatcher.Invoke(() =>
            {
                LiveEvents.Clear();
                TotalEvents = 0;
                TodayEvents = 0;
            });
            await RefreshNoiseFilterAsync();
        }
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == "EN" ? "RU" : "EN";
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Actions));
        OnPropertyChanged(nameof(LblTimeline));
        OnPropertyChanged(nameof(LblDashboard));
        OnPropertyChanged(nameof(LblExport));
        OnPropertyChanged(nameof(LblNoiseFilter));
        OnPropertyChanged(nameof(LblLiveTimeline));
        OnPropertyChanged(nameof(LblSearchResults));
        OnPropertyChanged(nameof(LblSearching));
        OnPropertyChanged(nameof(LblSearchPlaceholder));
        OnPropertyChanged(nameof(LblScrollLocked));
        OnPropertyChanged(nameof(LblOpenExplorer));
        OnPropertyChanged(nameof(LblRecording));
        OnPropertyChanged(nameof(LblTodayPrefix));
        OnPropertyChanged(nameof(LblMatches));
        OnPropertyChanged(nameof(LblFileSystem));
        OnPropertyChanged(nameof(LblNoiseFilterTitle));
        OnPropertyChanged(nameof(LblClearAll));
        OnPropertyChanged(nameof(LblIgnore));
        OnPropertyChanged(nameof(LblClear));
        OnPropertyChanged(nameof(LblIgnored));

        _selectedCategory = Categories[0];
        _selectedAction = Actions[0];
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(SelectedAction));
    }

    private async Task RefreshStatsAsync()
    {
        var todayStats = await _db.GetTodayStatsAsync();
        var topProcs = await _db.GetTopProcessesAsync(8);
        var total = await _db.GetTotalCountAsync();

        int today = todayStats.Values.Sum();
        int maxVal = todayStats.Values.DefaultIfEmpty(1).Max();

        _dispatcher.Invoke(() =>
        {
            TotalEvents = total;
            TodayEvents = today;

            ActivityStats.Clear();
            var colors = new[] { "#60A5FA", "#4ADE80", "#FBBF24", "#F87171", "#A78BFA", "#34D399", "#FB923C" };
            int i = 0;
            foreach (var kvp in todayStats.OrderByDescending(x => x.Value))
            {
                ActivityStats.Add(new ActivityStat
                {
                    Label = kvp.Key,
                    Count = kvp.Value,
                    Percentage = maxVal > 0 ? (double)kvp.Value / maxVal * 100 : 0,
                    Color = colors[i % colors.Length]
                });
                i++;
            }

            TopProcesses.Clear();
            foreach (var ps in topProcs)
                TopProcesses.Add(ps);
        });
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"EventTracker_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            await _db.ExportToCsvAsync(dlg.FileName, SearchQuery);
            var msg = CurrentLanguage == "RU" ? $"Экспортировано: {dlg.FileName}" : $"Exported to {dlg.FileName}";
            MessageBox.Show(msg, CurrentLanguage == "RU" ? "Экспорт завершён" : "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    public void OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
            else
            {
                var parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{parent}\"");
            }
        }
        catch { }
    }

    [RelayCommand]
    public void OpenEventLocation(SystemEvent ev)
    {
        if (ev == null) return;
        OpenFolder(ev.Path);
    }

    private void StartStatsTimer()
    {
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _statsTimer.Tick += async (s, e) => await RefreshStatsAsync();
        _statsTimer.Start();
    }
}
