using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventTracker.Models;
using EventTracker.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace EventTracker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly FileMonitoringService _fileSvc;
    private readonly ProcessMonitoringService _procSvc;
    private readonly AIService _ai;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _statsTimer;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _aiCts;

    // ── Observable properties ─────────────────────────────────────────────────
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
    [ObservableProperty] private string _processFilterQuery = "";

    // ── AI properties ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _aiExplanation = "";
    [ObservableProperty] private bool _isAiLoading = false;
    [ObservableProperty] private string _aiChatInput = "";
    [ObservableProperty] private string _openAIApiKey = "";
    [ObservableProperty] private bool _autoClassify = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _startMinimized = false;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<SystemEvent> LiveEvents { get; } = new();
    public ObservableCollection<SystemEvent> SearchResults { get; } = new();
    public ObservableCollection<FolderNode> DriveNodes { get; } = new();
    public ObservableCollection<ActivityStat> ActivityStats { get; } = new();
    public ObservableCollection<ProcessStat> TopProcesses { get; } = new();
    public ObservableCollection<ProcessActivityStat> ProcessActivityStats { get; } = new();
    public ObservableCollection<ProcessActivityStat> FilteredProcessStats { get; } = new();
    public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

    // ── Localisation helpers ──────────────────────────────────────────────────
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
    public string LblAI => CurrentLanguage == "RU" ? "AI Чат" : "AI Chat";
    public string LblSettings => CurrentLanguage == "RU" ? "Настройки" : "Settings";
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
    public string LblNoiseFilterTitle => CurrentLanguage == "RU" ? "ВСЕ ПРОЦЕССЫ СИСТЕМЫ" : "ALL SYSTEM PROCESSES";
    public string LblClearAll => CurrentLanguage == "RU" ? "Очистить всё" : "Clear All";
    public string LblIgnore => CurrentLanguage == "RU" ? "Игнор" : "Ignore";
    public string LblClear => CurrentLanguage == "RU" ? "Очистить" : "Clear";
    public string LblIgnored => CurrentLanguage == "RU" ? "В фильтре" : "Ignored";
    public string LblStatusMonitoring => CurrentLanguage == "RU" ? "Мониторинг активен" : "Monitoring active";
    public string LblExplainBtn => CurrentLanguage == "RU" ? "🤖 Объяснить" : "🤖 Explain";
    public string LblChatPlaceholder => CurrentLanguage == "RU" ? "Спросите о вашем ПК... (например: что делал Steam вчера?)" : "Ask about your PC... (e.g. what did Steam do yesterday?)";

    // ── Internal state ────────────────────────────────────────────────────────
    private readonly Queue<SystemEvent> _pendingLiveEvents = new();
    private HashSet<string> _ignoredProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderNode> _nodeIndex = new(StringComparer.OrdinalIgnoreCase);

    // ─────────────────────────────────────────────────────────────────────────
    public MainViewModel(
        DatabaseService db,
        FileMonitoringService fileSvc,
        ProcessMonitoringService procSvc,
        AIService ai,
        SettingsService settings)
    {
        _db = db;
        _fileSvc = fileSvc;
        _procSvc = procSvc;
        _ai = ai;
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Load settings into VM props
        OpenAIApiKey = _settings.Settings.OpenAIApiKey;
        AutoClassify = _settings.Settings.AutoClassify;
        MinimizeToTray = _settings.Settings.MinimizeToTray;
        StartMinimized = _settings.Settings.StartMinimized;

        _fileSvc.EventOccurred += OnEventOccurred;
        _procSvc.EventOccurred += OnEventOccurred;

        _ = InitializeAsync();
        InitializeDrives();
        StartStatsTimer();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Init
    // ─────────────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────────────
    // Event handler from monitoring services
    // ─────────────────────────────────────────────────────────────────────────
    private void OnEventOccurred(SystemEvent ev)
    {
        if (!string.IsNullOrEmpty(ev.ProcessName) &&
            _ignoredProcesses.Contains(ev.ProcessName))
            return;

        _dispatcher.BeginInvoke(() =>
        {
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

    // ─────────────────────────────────────────────────────────────────────────
    // AI Commands
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Explain the currently visible events (live timeline or search results)</summary>
    [RelayCommand]
    private async Task ExplainCurrentEventsAsync()
    {
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();

        IsAiLoading = true;
        AiExplanation = CurrentLanguage == "RU" ? "🤖 Анализирую события..." : "🤖 Analysing events...";
        CurrentView = "AI";

        try
        {
            var events = CurrentView == "Search" || IsFolderView
                ? SearchResults.ToList()
                : LiveEvents.ToList();

            var label = events.Count > 0
                ? $"{events.Last().Timestamp:HH:mm} – {events.First().Timestamp:HH:mm}"
                : null;

            AiExplanation = await _ai.ExplainEventsAsync(events, label, _aiCts.Token);
        }
        catch (OperationCanceledException) { }
        finally { IsAiLoading = false; }
    }

    /// <summary>Explain a specific time segment passed from the UI</summary>
    [RelayCommand]
    private async Task ExplainTimeSegmentAsync(string timeLabel)
    {
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();

        IsAiLoading = true;
        AiExplanation = $"🤖 Analysing {timeLabel}...";
        CurrentView = "AI";

        try
        {
            // Parse time label like "14:00–15:00" → get events in that hour
            IEnumerable<SystemEvent> events = LiveEvents;

            if (timeLabel.Contains("–") || timeLabel.Contains("-"))
            {
                var parts = timeLabel.Replace("–", "-").Split('-');
                if (parts.Length == 2
                    && TimeSpan.TryParse(parts[0].Trim(), out var from)
                    && TimeSpan.TryParse(parts[1].Trim(), out var to))
                {
                    var today = DateTime.Today;
                    var start = today + from;
                    var end = today + to;
                    events = (await _db.SearchAsync("", null, null))
                        .Where(e => e.Timestamp >= start && e.Timestamp <= end);
                }
            }

            AiExplanation = await _ai.ExplainEventsAsync(events, timeLabel, _aiCts.Token);
        }
        catch (OperationCanceledException) { }
        finally { IsAiLoading = false; }
    }

    /// <summary>Chat with PC history — free-form question</summary>
    [RelayCommand]
    private async Task SendChatMessageAsync()
    {
        var question = AiChatInput.Trim();
        if (string.IsNullOrEmpty(question)) return;

        AiChatInput = "";

        var userMsg = new ChatMessage { Role = "user", Content = question };
        _dispatcher.Invoke(() => ChatHistory.Add(userMsg));

        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        IsAiLoading = true;

        try
        {
            // Search relevant events based on question keywords
            var keywords = ExtractKeywords(question);
            var relevantEvents = await _db.SearchAsync(keywords, null, null);

            var history = ChatHistory
                .Where(m => m.Role != "system")
                .ToList();

            var answer = await _ai.ChatWithHistoryAsync(
                question,
                relevantEvents,
                history,
                _aiCts.Token);

            _dispatcher.Invoke(() =>
            {
                ChatHistory.Add(new ChatMessage { Role = "assistant", Content = answer });
            });
        }
        catch (OperationCanceledException) { }
        finally { IsAiLoading = false; }
    }

    [RelayCommand]
    private async Task ClassifyVisibleEventsAsync()
    {
        if (!_ai.IsConfigured)
        {
            AiExplanation = "⚠️ Set your OpenAI API key in Settings first.";
            CurrentView = "AI";
            return;
        }

        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        IsAiLoading = true;
        CurrentView = "AI";
        AiExplanation = "🤖 Classifying events...";

        try
        {
            var events = LiveEvents.Take(50).ToList();
            var tags = await _ai.ClassifyEventsAsync(events, _aiCts.Token);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("**Auto-classification results:**\n");
            var grouped = tags.GroupBy(x => x.Value)
                              .OrderByDescending(g => g.Count());
            foreach (var g in grouped)
            {
                var icon = g.Key switch
                {
                    "installation" => "📦",
                    "update" => "🔄",
                    "game-session" => "🎮",
                    "browser" => "🌐",
                    "deletion" => "🗑️",
                    "suspicious" => "⚠️",
                    "system" => "⚙️",
                    "backup" => "💾",
                    "development" => "💻",
                    "media" => "🎵",
                    "office" => "📄",
                    _ => "•"
                };
                sb.AppendLine($"{icon} **{g.Key}** — {g.Count()} events");
            }
            AiExplanation = sb.ToString();
        }
        catch (OperationCanceledException) { }
        finally { IsAiLoading = false; }
    }

    [RelayCommand]
    private void ShowAIView()
    {
        CurrentView = "AI";
        if (string.IsNullOrEmpty(AiExplanation))
            AiExplanation = CurrentLanguage == "RU"
                ? "👋 Привет! Я AI-ассистент PC Timeline.\n\nНажмите «🤖 Объяснить» на таймлайне чтобы проанализировать события, или напишите мне вопрос в чате ниже.\n\nПримеры:\n• «Что делал Steam вчера вечером?»\n• «Какие файлы удалил Chrome?»\n• «Что происходило сегодня в 14:00?»"
                : "👋 Hi! I'm the PC Timeline AI assistant.\n\nClick «🤖 Explain» on the timeline to analyse events, or ask me a question in the chat below.\n\nExamples:\n• «What did Steam do last night?»\n• «Which files did Chrome delete?»\n• «What happened today at 2pm?»";
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentView = "Settings";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.Settings.OpenAIApiKey = OpenAIApiKey;
        _settings.Settings.AutoClassify = AutoClassify;
        _settings.Settings.MinimizeToTray = MinimizeToTray;
        _settings.Settings.StartMinimized = StartMinimized;
        _settings.Save();

        if (!string.IsNullOrWhiteSpace(OpenAIApiKey))
            _ai.SetApiKey(OpenAIApiKey);

        StatusText = CurrentLanguage == "RU" ? "Настройки сохранены ✓" : "Settings saved ✓";
        CurrentView = "Timeline";
    }

    [RelayCommand]
    private void ClearChat()
    {
        ChatHistory.Clear();
        AiExplanation = "";
    }

    // ─── Keyword extraction for chat search ──────────────────────────────────
    private static string ExtractKeywords(string question)
    {
        // Strip common stop words and return key terms
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "что", "делал", "делала", "было", "когда", "как", "где", "the", "what",
            "did", "does", "was", "when", "how", "where", "is", "are", "and", "or"
        };

        var words = question.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Take(4);

        return string.Join(" ", words);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tree / Sidebar
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdateTreeForEvent(SystemEvent ev)
    {
        try
        {
            string path = ev.Path;
            string action = ev.Action;
            bool isDir = ev.Category == "Folder";
            string parentPath = System.IO.Path.GetDirectoryName(path) ?? "";

            switch (action)
            {
                case "Created":
                    if (_nodeIndex.TryGetValue(parentPath, out var parentNode) && parentNode.IsLoaded)
                    {
                        string itemName = System.IO.Path.GetFileName(path);
                        if (!parentNode.Children.Any(c => c.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            var newNode = isDir
                                ? new FolderNode
                                {
                                    Name = "📁 " + itemName,
                                    FullPath = path,
                                    Children = new ObservableCollection<FolderNode> { new FolderNode { Name = "..." } }
                                }
                                : new FolderNode { Name = "📄 " + itemName, FullPath = path, IsFile = true };

                            int insertIdx = FindInsertIndex(parentNode.Children, newNode);
                            parentNode.Children.Insert(insertIdx, newNode);
                            if (!newNode.IsFile) _nodeIndex[path] = newNode;
                        }
                    }
                    break;

                case "Deleted":
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
                    if (_nodeIndex.TryGetValue(parentPath, out var renParent) && renParent.IsLoaded)
                    {
                        string newName = System.IO.Path.GetFileName(path);
                        var stale = renParent.Children
                            .Where(c => !c.IsFile ? !Directory.Exists(c.FullPath) : !File.Exists(c.FullPath))
                            .ToList();
                        foreach (var s in stale) { renParent.Children.Remove(s); _nodeIndex.Remove(s.FullPath); }

                        if (!renParent.Children.Any(c => c.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            var renamedNode = isDir
                                ? new FolderNode
                                {
                                    Name = "📁 " + newName,
                                    FullPath = path,
                                    Children = new ObservableCollection<FolderNode> { new FolderNode { Name = "..." } }
                                }
                                : new FolderNode { Name = "📄 " + newName, FullPath = path, IsFile = true };
                            int insertIdx = FindInsertIndex(renParent.Children, renamedNode);
                            renParent.Children.Insert(insertIdx, renamedNode);
                            if (!renamedNode.IsFile) _nodeIndex[path] = renamedNode;
                        }
                    }
                    break;
            }
        }
        catch { }
    }

    private static int FindInsertIndex(ObservableCollection<FolderNode> children, FolderNode newNode)
    {
        if (children.Count == 1 && children[0].Name == "...") return 0;
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            if (c.Name == "...") continue;
            if (newNode.IsFile && !c.IsFile) continue;
            if (!newNode.IsFile && c.IsFile) return i;
            if (string.Compare(newNode.Name, c.Name, StringComparison.OrdinalIgnoreCase) < 0) return i;
        }
        return children.Count;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scroll lock
    // ─────────────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────────────
    // Drives
    // ─────────────────────────────────────────────────────────────────────────
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
                Children = new ObservableCollection<FolderNode> { new FolderNode { Name = "..." } }
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
            foreach (var d in Directory.GetDirectories(node.FullPath).OrderBy(x => x).Take(300))
            {
                var child = new FolderNode
                {
                    Name = "📁 " + System.IO.Path.GetFileName(d),
                    FullPath = d,
                    Children = new ObservableCollection<FolderNode> { new FolderNode { Name = "..." } }
                };
                node.Children.Add(child);
                if (!_nodeIndex.ContainsKey(d)) _nodeIndex[d] = child;
            }
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

    // ─────────────────────────────────────────────────────────────────────────
    // Navigation
    // ─────────────────────────────────────────────────────────────────────────
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
            _dispatcher.Invoke(() => { SearchResults.Clear(); foreach (var ev in events) SearchResults.Add(ev); });
        }
        else
        {
            var events = await _db.GetFolderEventsAsync(node.FullPath);
            _dispatcher.Invoke(() => { SearchResults.Clear(); foreach (var ev in events) SearchResults.Add(ev); });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Search
    // ─────────────────────────────────────────────────────────────────────────
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
            _dispatcher.Invoke(() => { CurrentView = "Timeline"; IsFolderView = false; });
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
        "Файл" => "File", "Папка" => "Folder", "Процесс" => "Process",
        "Сеть" => "Network", "Ошибка" => "Error", "Предупреждение" => "Warning",
        _ => cat
    };

    private string? MapActionToEn(string act) => act switch
    {
        "Создан" => "Created", "Удалён" => "Deleted", "Изменён" => "Modified",
        "Переименован" => "Renamed", "Запущен" => "Started",
        _ => act
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dashboard & Stats
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task ShowDashboardAsync() { CurrentView = "Dashboard"; await RefreshStatsAsync(); }

    [RelayCommand]
    private void ShowTimeline() { SearchQuery = ""; CurrentView = "Timeline"; IsFolderView = false; }

    [RelayCommand]
    private async Task ShowNoiseFilterAsync() { CurrentView = "NoiseFilter"; await RefreshNoiseFilterAsync(); }

    partial void OnProcessFilterQueryChanged(string value) => ApplyProcessFilter();

    private void ApplyProcessFilter()
    {
        var query = ProcessFilterQuery?.Trim() ?? "";
        _dispatcher.Invoke(() =>
        {
            FilteredProcessStats.Clear();
            foreach (var s in ProcessActivityStats)
                if (string.IsNullOrEmpty(query) || s.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    FilteredProcessStats.Add(s);
        });
    }

    public async Task RefreshNoiseFilterAsync()
    {
        _ignoredProcesses = await _db.GetIgnoredProcessesAsync();
        _fileSvc.SetIgnoredProcesses(_ignoredProcesses);

        var dbStats = await _db.GetProcessActivityStatsDictAsync();
        var liveProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() =>
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
                try { string name = p.ProcessName; if (!string.IsNullOrWhiteSpace(name)) liveProcessNames.Add(name); } catch { }
        });

        var merged = new List<ProcessActivityStat>(dbStats.Values.OrderByDescending(x => x.TotalEvents));
        foreach (var name in liveProcessNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!dbStats.ContainsKey(name))
                merged.Add(new ProcessActivityStat { ProcessName = name, TotalEvents = 0, IsIgnored = _ignoredProcesses.Contains(name) });
            else
                dbStats[name].IsIgnored = _ignoredProcesses.Contains(name);
        }

        _dispatcher.Invoke(() =>
        {
            ProcessActivityStats.Clear();
            foreach (var s in merged) ProcessActivityStats.Add(s);
            ApplyProcessFilter();
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
        var msg = CurrentLanguage == "RU" ? $"Удалить все события для '{processName}'?" : $"Clear all events for '{processName}'?";
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
        var msg = CurrentLanguage == "RU" ? "Удалить ВСЕ записанные события?" : "Clear ALL recorded events?";
        if (MessageBox.Show(msg, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await _db.ClearAllEventsAsync();
            _dispatcher.Invoke(() => { LiveEvents.Clear(); TotalEvents = 0; TodayEvents = 0; });
            await RefreshNoiseFilterAsync();
        }
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == "EN" ? "RU" : "EN";
        var propNames = new[]
        {
            nameof(Categories), nameof(Actions), nameof(LblTimeline), nameof(LblDashboard),
            nameof(LblExport), nameof(LblNoiseFilter), nameof(LblAI), nameof(LblSettings),
            nameof(LblLiveTimeline), nameof(LblSearchResults), nameof(LblSearching),
            nameof(LblSearchPlaceholder), nameof(LblScrollLocked), nameof(LblOpenExplorer),
            nameof(LblRecording), nameof(LblTodayPrefix), nameof(LblMatches),
            nameof(LblFileSystem), nameof(LblNoiseFilterTitle), nameof(LblClearAll),
            nameof(LblIgnore), nameof(LblClear), nameof(LblIgnored), nameof(LblStatusMonitoring),
            nameof(LblExplainBtn), nameof(LblChatPlaceholder)
        };
        foreach (var p in propNames) OnPropertyChanged(p);
        SelectedCategory = Categories[0];
        SelectedAction = Actions[0];
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
            foreach (var ps in topProcs) TopProcesses.Add(ps);
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
