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

    // FilePath is an alias for Path to avoid WPF binding collision with FrameworkElement.Path
    public string FilePath => Path;
}

public class FolderNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public List<FolderNode> Children { get; set; } = new();
    public bool IsLoaded { get; set; }
    public bool IsDrive { get; set; }
    // FIX: Track whether this node represents a file (leaf) vs folder
    public bool IsFile { get; set; }
}

public class ActivityStat
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double Percentage { get; set; }
    public string Color { get; set; } = "#60A5FA";
}

/// <summary>
/// Replaces (string Label, int Count) tuples for WPF binding compatibility.
/// </summary>
public class ProcessStat
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>
/// Represents a process with total file activity count for the Noise Filter / Top Processes view.
/// </summary>
public class ProcessActivityStat
{
    public string ProcessName { get; set; } = "";
    public int TotalEvents { get; set; }
    public int CreatedCount { get; set; }
    public int ModifiedCount { get; set; }
    public int DeletedCount { get; set; }
    public bool IsIgnored { get; set; }
}
