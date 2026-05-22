# EventTracker

A modern, real-time system monitoring tool for Windows — tracks all file system and process activity, stores it in a local SQLite database, and presents a clean dark-mode timeline UI.

---

## Features

- **Live Timeline** — real-time feed of every file/folder creation, deletion, modification, rename
- **Process Tracking** — detects newly started processes (polls every 2 seconds)
- **Global Search** — instant SQLite-indexed search across all events: by filename, path, process, action, folder
- **Folder Browser** — sidebar tree of all drives; click any folder to see its change history
- **Dashboard** — today's stats, activity by category, top active processes
- **Export** — one-click CSV export of any search result
- **Dark UI** — frameless window, custom titlebar, Consolas monospace timeline

---

## Requirements

- Windows 10/11
- **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0
  - Choose: `.NET 8.0 SDK (v8.x.x)` — Windows x64 Installer

---

## Build & Run

```
cd EventTracker
dotnet restore
dotnet run
```

Or publish a self-contained exe:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\EventTracker.exe`

---

## Architecture

```
EventTracker/
├── Models/
│   └── SystemEvent.cs          # Data models (SystemEvent, FolderNode, ActivityStat)
├── Services/
│   ├── DatabaseService.cs      # SQLite + async write queue + search
│   ├── FileMonitoringService.cs # FileSystemWatcher on all drives
│   └── ProcessMonitoringService.cs # Process polling (new process detection)
├── ViewModels/
│   └── MainViewModel.cs        # MVVM binding, search, navigation
├── Views/
│   ├── MainWindow.xaml         # Dark UI layout
│   └── MainWindow.xaml.cs      # Code-behind (window chrome, hover effects)
├── Themes/
│   └── DarkTheme.xaml          # All styles, colors, control templates
├── Converters/
│   └── Converters.cs           # Value converters for XAML bindings
└── App.xaml / App.xaml.cs      # Startup, service wiring
```

---

## Database

Stored at: `%LOCALAPPDATA%\EventTracker\events.db`

Table: `events`
- `id`, `timestamp`, `category`, `process_name`, `action`
- `path`, `file_name`, `directory_name`, `details`, `executable_path`

Indexed on: `timestamp`, `process_name`, `path`, `category`, `file_name`, `directory_name`, `action`

WAL mode + async batched writes for minimal CPU overhead.

---

## Search Examples

| Query | What you get |
|-------|-------------|
| `chrome` | All events involving chrome.exe |
| `deleted` | All deleted files and folders |
| `Downloads` | All changes in any Downloads folder |
| `setup.exe` | Any installer that ran or created files |
| `.png` | All PNG files created/modified/deleted |
| `Steam` | All Steam-related file activity |

---

## Performance Notes

- FileSystemWatcher uses `InternalBufferSize = 65536` to reduce dropped events
- All DB writes are queued and flushed every 500ms in batches (no per-event writes)
- SQLite in WAL mode with `synchronous=NORMAL` for fast writes
- UI list is virtualized (max 500 live events in memory)
- System/temp paths are excluded from monitoring to reduce noise

---

## Permissions

- Run as standard user: monitors your user-accessible drives
- Run as Administrator: deeper visibility into system processes and protected paths
