using Microsoft.Data.Sqlite;
using EventTracker.Models;
using System.IO;
using System.Linq;
using ThreadingTimer = System.Threading.Timer;

namespace EventTracker.Services;

public class DatabaseService : IDisposable
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Queue<SystemEvent> _writeQueue = new();
    private readonly ThreadingTimer _flushTimer;
    private bool _disposed;

    public DatabaseService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EventTracker");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "events.db");
        _flushTimer = new ThreadingTimer(FlushQueue, null, 500, 500);
    }

    public void Initialize()
    {
        using var conn = CreateConnection();
        conn.Open();

        ExecuteNonQuery(conn, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(conn, "PRAGMA synchronous=NORMAL;");
        ExecuteNonQuery(conn, "PRAGMA cache_size=10000;");
        ExecuteNonQuery(conn, "PRAGMA temp_store=memory;");

        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                category TEXT NOT NULL DEFAULT '',
                process_name TEXT NOT NULL DEFAULT '',
                action TEXT NOT NULL DEFAULT '',
                path TEXT NOT NULL DEFAULT '',
                file_name TEXT NOT NULL DEFAULT '',
                directory_name TEXT NOT NULL DEFAULT '',
                details TEXT NOT NULL DEFAULT '',
                executable_path TEXT NOT NULL DEFAULT ''
            );");

        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_timestamp ON events(timestamp);");
        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_process_name ON events(process_name COLLATE NOCASE);");
        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_path ON events(path COLLATE NOCASE);");
        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_category ON events(category);");
        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_file_name ON events(file_name COLLATE NOCASE);");
        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_directory_name ON events(directory_name COLLATE NOCASE);");
        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_action ON events(action);");

        // Table for ignored (noise-filtered) processes
        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS ignored_processes (
                process_name TEXT PRIMARY KEY COLLATE NOCASE
            );");
    }

    private static void ExecuteNonQuery(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection()
        => new($"Data Source={_dbPath};");

    public void QueueEvent(SystemEvent ev)
    {
        lock (_writeQueue) { _writeQueue.Enqueue(ev); }
    }

    private void FlushQueue(object? state)
    {
        List<SystemEvent> batch;
        lock (_writeQueue)
        {
            if (_writeQueue.Count == 0) return;
            batch = new List<SystemEvent>(_writeQueue);
            _writeQueue.Clear();
        }
        _ = WriteBatchAsync(batch);
    }

    private async Task WriteBatchAsync(List<SystemEvent> events)
    {
        if (events.Count == 0) return;
        await _writeLock.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO events (timestamp, category, process_name, action, path, file_name, directory_name, details, executable_path)
                VALUES ($ts, $cat, $proc, $action, $path, $fname, $dname, $details, $exe)";

            var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);
            var pCat = cmd.Parameters.Add("$cat", SqliteType.Text);
            var pProc = cmd.Parameters.Add("$proc", SqliteType.Text);
            var pAction = cmd.Parameters.Add("$action", SqliteType.Text);
            var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
            var pFname = cmd.Parameters.Add("$fname", SqliteType.Text);
            var pDname = cmd.Parameters.Add("$dname", SqliteType.Text);
            var pDetails = cmd.Parameters.Add("$details", SqliteType.Text);
            var pExe = cmd.Parameters.Add("$exe", SqliteType.Text);

            foreach (var ev in events)
            {
                pTs.Value = ev.Timestamp.ToString("o");
                pCat.Value = ev.Category;
                pProc.Value = ev.ProcessName;
                pAction.Value = ev.Action;
                pPath.Value = ev.Path;
                pFname.Value = ev.FileName;
                pDname.Value = ev.DirectoryName;
                pDetails.Value = ev.Details;
                pExe.Value = ev.ExecutablePath;
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch { /* log silently */ }
        finally { _writeLock.Release(); }
    }

    public async Task<HashSet<string>> GetIgnoredProcessesAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT process_name FROM ignored_processes";
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task AddIgnoredProcessAsync(string processName)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO ignored_processes (process_name) VALUES ($p)";
        cmd.Parameters.AddWithValue("$p", processName);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveIgnoredProcessAsync(string processName)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ignored_processes WHERE process_name = $p COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$p", processName);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ClearProcessEventsAsync(string processName)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM events WHERE process_name = $p COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$p", processName);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ClearAllEventsAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM events";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<string, ProcessActivityStat>> GetProcessActivityStatsDictAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                e.process_name,
                COUNT(*) as total,
                SUM(CASE WHEN e.action = 'Created' THEN 1 ELSE 0 END) as created,
                SUM(CASE WHEN e.action = 'Modified' THEN 1 ELSE 0 END) as modified,
                SUM(CASE WHEN e.action = 'Deleted' THEN 1 ELSE 0 END) as deleted,
                CASE WHEN ip.process_name IS NOT NULL THEN 1 ELSE 0 END as is_ignored
            FROM events e
            LEFT JOIN ignored_processes ip ON ip.process_name = e.process_name COLLATE NOCASE
            WHERE e.process_name != ''
            GROUP BY e.process_name
            ORDER BY total DESC";

        var result = new Dictionary<string, ProcessActivityStat>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            result[name] = new ProcessActivityStat
            {
                ProcessName = name,
                TotalEvents = reader.GetInt32(1),
                CreatedCount = reader.GetInt32(2),
                ModifiedCount = reader.GetInt32(3),
                DeletedCount = reader.GetInt32(4),
                IsIgnored = reader.GetInt32(5) == 1
            };
        }
        return result;
    }

    // Keep old signature for compatibility — wraps the dict version
    public async Task<List<ProcessActivityStat>> GetProcessActivityStatsAsync(int topN = 9999)
    {
        var dict = await GetProcessActivityStatsDictAsync();
        return dict.Values.OrderByDescending(x => x.TotalEvents).Take(topN).ToList();
    }

    public async Task<List<SystemEvent>> SearchAsync(string query, string? category = null,
        string? action = null, int limit = 500, int offset = 0)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        var conditions = new List<string>();

        // Always exclude ignored processes
        conditions.Add("process_name NOT IN (SELECT process_name FROM ignored_processes)");

        if (!string.IsNullOrWhiteSpace(query))
        {
            conditions.Add(@"(
                file_name LIKE $q COLLATE NOCASE OR
                path LIKE $q COLLATE NOCASE OR
                process_name LIKE $q COLLATE NOCASE OR
                directory_name LIKE $q COLLATE NOCASE OR
                action LIKE $q COLLATE NOCASE OR
                details LIKE $q COLLATE NOCASE
            )");
            cmd.Parameters.AddWithValue("$q", $"%{query}%");
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            conditions.Add("category = $cat");
            cmd.Parameters.AddWithValue("$cat", category);
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            conditions.Add("action = $action");
            cmd.Parameters.AddWithValue("$action", action);
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        cmd.CommandText = $@"
            SELECT id, timestamp, category, process_name, action, path, file_name, directory_name, details, executable_path
            FROM events
            {where}
            ORDER BY timestamp DESC
            LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        return await ReadEventsAsync(cmd);
    }

    public async Task<List<SystemEvent>> GetFolderEventsAsync(string folderPath, int limit = 300)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, timestamp, category, process_name, action, path, file_name, directory_name, details, executable_path
            FROM events
            WHERE (path LIKE $path COLLATE NOCASE OR directory_name LIKE $exact COLLATE NOCASE)
              AND process_name NOT IN (SELECT process_name FROM ignored_processes)
            ORDER BY timestamp DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$path", $"{folderPath}%");
        cmd.Parameters.AddWithValue("$exact", folderPath);
        cmd.Parameters.AddWithValue("$limit", limit);
        return await ReadEventsAsync(cmd);
    }

    public async Task<List<SystemEvent>> GetRecentAsync(int limit = 200, HashSet<string>? ignoredProcesses = null)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, timestamp, category, process_name, action, path, file_name, directory_name, details, executable_path
            FROM events
            WHERE process_name NOT IN (SELECT process_name FROM ignored_processes)
            ORDER BY timestamp DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        return await ReadEventsAsync(cmd);
    }

    public async Task<Dictionary<string, int>> GetTodayStatsAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        cmd.CommandText = @"
            SELECT category, COUNT(*) as cnt
            FROM events
            WHERE timestamp >= $today
            GROUP BY category
            ORDER BY cnt DESC";
        cmd.Parameters.AddWithValue("$today", today);

        var result = new Dictionary<string, int>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    public async Task<List<ProcessStat>> GetTopProcessesAsync(int topN = 10)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT process_name, COUNT(*) as cnt
            FROM events
            WHERE process_name != ''
            AND timestamp >= $today
            GROUP BY process_name
            ORDER BY cnt DESC
            LIMIT $n";
        cmd.Parameters.AddWithValue("$today", DateTime.Today.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$n", topN);

        var result = new List<ProcessStat>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new ProcessStat { Label = reader.GetString(0), Count = reader.GetInt32(1) });
        return result;
    }

    public async Task<long> GetTotalCountAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events";
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public async Task ExportToCsvAsync(string filePath, string? query = null)
    {
        var events = await SearchAsync(query ?? "", limit: 50000);
        using var writer = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        await writer.WriteLineAsync("ID,Timestamp,Category,Process,Action,Path,FileName,Directory,Details");
        foreach (var ev in events)
            await writer.WriteLineAsync($"{ev.Id},{ev.Timestamp:o},{Csv(ev.Category)},{Csv(ev.ProcessName)},{Csv(ev.Action)},{Csv(ev.Path)},{Csv(ev.FileName)},{Csv(ev.DirectoryName)},{Csv(ev.Details)}");
    }

    private static string Csv(string s) => $"\"{s.Replace("\"", "\"\"")}\"";

    private static async Task<List<SystemEvent>> ReadEventsAsync(SqliteCommand cmd)
    {
        var list = new List<SystemEvent>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SystemEvent
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                Category = reader.GetString(2),
                ProcessName = reader.GetString(3),
                Action = reader.GetString(4),
                Path = reader.GetString(5),
                FileName = reader.GetString(6),
                DirectoryName = reader.GetString(7),
                Details = reader.GetString(8),
                ExecutablePath = reader.GetString(9)
            });
        }
        return list;
    }

    // FIX: Get events for a specific file (exact path match)
    public async Task<List<SystemEvent>> GetFileEventsAsync(string filePath, int limit = 300)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, timestamp, category, process_name, action, path, file_name, directory_name, details, executable_path
            FROM events
            WHERE path = $path COLLATE NOCASE
              AND process_name NOT IN (SELECT process_name FROM ignored_processes)
            ORDER BY timestamp DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$limit", limit);
        return await ReadEventsAsync(cmd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer.Dispose();
        FlushQueue(null);
    }
}
