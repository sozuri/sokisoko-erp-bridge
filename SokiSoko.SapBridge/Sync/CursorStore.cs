using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SokiSoko.SapBridge.Sync;

/// <summary>
/// Per-entity high-water marks, persisted next to the executable as cursors.json. The
/// server keeps its own copy (advanced on each accepted batch); the local copy lets the
/// agent resume after a restart without re-reading all of B1.
/// </summary>
public sealed class CursorStore
{
    private readonly string _path;
    private readonly ILogger<CursorStore> _log;
    private readonly object _lock = new();
    private Dictionary<string, string> _cursors = new();

    public CursorStore(IHostEnvironment env, ILogger<CursorStore> log)
    {
        _log = log;
        _path = Path.Combine(env.ContentRootPath, "cursors.json");
        Load();
    }

    public string Get(string entity)
    {
        lock (_lock)
        {
            return _cursors.TryGetValue(entity, out var v) ? v : "";
        }
    }

    public void Set(string entity, string cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return;
        lock (_lock)
        {
            if (!_cursors.TryGetValue(entity, out var cur) || string.CompareOrdinal(cursor, cur) > 0)
            {
                _cursors[entity] = cursor;
                Save();
            }
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                _cursors = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "could not read {Path}; starting with empty cursors", _path);
            _cursors = new();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_cursors, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "could not persist cursors to {Path}", _path);
        }
    }
}
