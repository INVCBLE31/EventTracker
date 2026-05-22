using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EventTracker.Models;

public class SystemEvent
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Category { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DirectoryName { get; set; } = "";
    public string Details { get; set; } = "";
    public string ExecutablePath { get; set; } = "";

    public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
    public string DateDisplay => Timestamp.ToString("dd MMM yyyy");

    public string CategoryIcon => Category switch
    {
        "File" => "📄",
        "Folder" => "📁",
        "Process" => "⚙️",
        "USB" => "🔌",
        "Network" => "🌐",
        "Error" => "❌",
        "Warning" => "⚠️",
        _ => "•"
    };

    public string ActionColor => Action switch
    {
        "Created" => "#4ADE80",
        "Deleted" => "#F87171",
        "Modified" => "#60A5FA",
        "Renamed" => "#FBBF24",
        "Started" => "#34D399",
        "Stopped" => "#F87171",
        "Connected" => "#4ADE80",
        "Disconnected" => "#F87171",
        _ => "#94A3B8"
    };

    public string FilePath => Path;
}

// FolderNode implements INotifyPropertyChanged so the TreeView reacts to live changes
public class FolderNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isExpanded;
    private bool _isSelected;

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsLoaded { get; set; }
    public bool IsDrive { get; set; }
    public bool IsFile { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    // ObservableCollection so TreeView updates live when children are added/removed
    public ObservableCollection<FolderNode> Children { get; set; } = new();

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ActivityStat
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double Percentage { get; set; }
    public string Color { get; set; } = "#60A5FA";
}

public class ProcessStat
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public class ProcessActivityStat
{
    public string ProcessName { get; set; } = "";
    public int TotalEvents { get; set; }
    public int CreatedCount { get; set; }
    public int ModifiedCount { get; set; }
    public int DeletedCount { get; set; }
    public bool IsIgnored { get; set; }
}
