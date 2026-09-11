using System.Collections.Concurrent;

namespace SokiSoko.SapBridge.Sync;

/// <summary>In-memory view of recent sync activity, shown on the setup console.</summary>
public sealed class SyncStatus
{
    private readonly ConcurrentQueue<string> _recent = new();
    private const int MaxRecent = 20;

    public DateTimeOffset? LastRunAt { get; private set; }
    public bool LastRunOk { get; private set; }
    public string LastError { get; private set; } = "";
    public int LastRunRecords { get; private set; }
    public bool Running { get; private set; }

    public IEnumerable<string> Recent => _recent;

    public void Begin() => Running = true;

    public void Complete(int records)
    {
        Running = false;
        LastRunAt = DateTimeOffset.Now;
        LastRunOk = true;
        LastError = "";
        LastRunRecords = records;
        Add($"ok — {records} records");
    }

    public void Fail(Exception ex)
    {
        Running = false;
        LastRunAt = DateTimeOffset.Now;
        LastRunOk = false;
        LastError = ex.Message;
        Add("error — " + ex.Message);
    }

    private void Add(string line)
    {
        _recent.Enqueue($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}");
        while (_recent.Count > MaxRecent && _recent.TryDequeue(out _)) { }
    }
}
