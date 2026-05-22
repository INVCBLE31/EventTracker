using System.IO;
using EventTracker.Models;

namespace EventTracker.Services;

public class FileMonitoringService
{
    private readonly DatabaseService _db;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _ignoredPaths;
    private bool _running;

    // Noise throttle: skip if same path got same event < 2s ago
    private readonly Dictionary<string, DateTime> _lastEventTime = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _throttleWindow = TimeSpan.FromSeconds(2);
    private readonly object _throttleLock = new();

    // Set of process names to ignore (noise filter) — updated from ViewModel
    // FIX: This is now properly synced from DB via SetIgnoredProcesses()
    public HashSet<string> IgnoredProcesses { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public event Action<SystemEvent>? EventOccurred;

    public FileMonitoringService(DatabaseService db)
    {
        _db = db;
        _ignoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\EventTracker",
            Path.GetTempPath()
        };
    }

    // FIX: Called by ViewModel after loading ignored processes from DB
    public void SetIgnoredProcesses(HashSet<string> processes)
    {
        IgnoredProcesses = new HashSet<string>(processes, StringComparer.OrdinalIgnoreCase);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            if (drive.DriveType == DriveType.CDRom) continue;
            try { CreateWatcher(drive.RootDirectory.FullName); }
            catch { /* skip inaccessible */ }
        }
    }

    private void CreateWatcher(string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size
                         | NotifyFilters.CreationTime,
            InternalBufferSize = 65536,
            EnableRaisingEvents = true
        };

        watcher.Created += (s, e) => HandleEvent(e.FullPath, "Created");
        watcher.Deleted += (s, e) => HandleEvent(e.FullPath, "Deleted");
        watcher.Changed += (s, e) => HandleEvent(e.FullPath, "Modified");
        watcher.Renamed += (s, e) => HandleRename(e.OldFullPath, e.FullPath);
        watcher.Error += (s, e) => HandleError(e.GetException());

        _watchers.Add(watcher);
    }

    private bool IsThrottled(string key)
    {
        lock (_throttleLock)
        {
            if (_lastEventTime.TryGetValue(key, out var last) &&
                DateTime.Now - last < _throttleWindow)
                return true;
            _lastEventTime[key] = DateTime.Now;
            return false;
        }
    }

    private void HandleEvent(string fullPath, string action)
    {
        if (!_running) return;
        if (ShouldIgnore(fullPath)) return;

        // Throttle duplicate events on the same path
        var throttleKey = $"{action}:{fullPath}";
        if (IsThrottled(throttleKey)) return;

        bool isDir = Directory.Exists(fullPath);
        var ev = new SystemEvent
        {
            Timestamp = DateTime.Now,
            Category = isDir ? "Folder" : "File",
            Action = action,
            Path = fullPath,
            FileName = isDir ? "" : System.IO.Path.GetFileName(fullPath),
            DirectoryName = System.IO.Path.GetDirectoryName(fullPath) ?? "",
            Details = $"{action}: {fullPath}"
        };

        _db.QueueEvent(ev);
        EventOccurred?.Invoke(ev);
    }

    private void HandleRename(string oldPath, string newPath)
    {
        if (!_running) return;
        if (ShouldIgnore(newPath)) return;

        bool isDir = Directory.Exists(newPath);
        var ev = new SystemEvent
        {
            Timestamp = DateTime.Now,
            Category = isDir ? "Folder" : "File",
            Action = "Renamed",
            Path = newPath,
            FileName = isDir ? "" : System.IO.Path.GetFileName(newPath),
            DirectoryName = System.IO.Path.GetDirectoryName(newPath) ?? "",
            Details = $"Renamed: {System.IO.Path.GetFileName(oldPath)} → {System.IO.Path.GetFileName(newPath)}"
        };

        _db.QueueEvent(ev);
        EventOccurred?.Invoke(ev);
    }

    private void HandleError(Exception ex)
    {
        var ev = new SystemEvent
        {
            Timestamp = DateTime.Now,
            Category = "Error",
            Action = "Error",
            Details = $"Watcher error: {ex.Message}"
        };
        _db.QueueEvent(ev);
        EventOccurred?.Invoke(ev);
    }

    private bool ShouldIgnore(string path)
    {
        foreach (var ignored in _ignoredPaths)
            if (path.StartsWith(ignored, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public void Stop()
    {
        _running = false;
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
