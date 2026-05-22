using System.Diagnostics;
using System.Collections.Generic;
using EventTracker.Models;
using ThreadingTimer = System.Threading.Timer;

namespace EventTracker.Services;

public class ProcessMonitoringService
{
    private readonly DatabaseService _db;
    private ThreadingTimer? _timer;
    private readonly HashSet<int> _trackedPids = new();
    private bool _running;

    public event Action<SystemEvent>? EventOccurred;

    public ProcessMonitoringService(DatabaseService db)
    {
        _db = db;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        // Seed existing processes
        foreach (var p in Process.GetProcesses())
        {
            try { _trackedPids.Add(p.Id); }
            catch { }
        }

        _timer = new ThreadingTimer(Poll, null, 2000, 2000);
    }

    private void Poll(object? state)
    {
        if (!_running) return;
        try
        {
            var current = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    current.Add(p.Id);
                    if (!_trackedPids.Contains(p.Id))
                    {
                        string exePath = "";
                        try { exePath = p.MainModule?.FileName ?? ""; } catch { }

                        var ev = new SystemEvent
                        {
                            Timestamp = DateTime.Now,
                            Category = "Process",
                            ProcessName = p.ProcessName,
                            Action = "Started",
                            Path = exePath,
                            FileName = p.ProcessName + ".exe",
                            Details = $"{p.ProcessName}.exe started (PID: {p.Id})",
                            ExecutablePath = exePath
                        };
                        _db.QueueEvent(ev);
                        EventOccurred?.Invoke(ev);
                    }
                }
                catch { }
            }

            // Detect stopped processes — compute difference manually to avoid namespace ambiguity
            var stopped = new List<int>();
            foreach (var pid in _trackedPids)
            {
                if (!current.Contains(pid))
                    stopped.Add(pid);
            }
            // (Names are no longer available for stopped PIDs, so we just update tracking)

            _trackedPids.Clear();
            foreach (var id in current) _trackedPids.Add(id);
        }
        catch { }
    }

    public void Stop()
    {
        _running = false;
        _timer?.Dispose();
    }
}
