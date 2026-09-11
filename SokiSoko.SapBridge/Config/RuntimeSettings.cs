namespace SokiSoko.SapBridge.Config;

/// <summary>
/// Live settings the worker and clients read. Updated by the setup console; changes take
/// effect on the next sync run — no service restart needed.
/// </summary>
public sealed class RuntimeSettings
{
    private readonly SettingsStore _store;
    private readonly object _lock = new();
    private BridgeSettings _current;

    public RuntimeSettings(SettingsStore store)
    {
        _store = store;
        _current = store.Load();
    }

    public BridgeSettings Current
    {
        get { lock (_lock) return _current; }
    }

    public void Update(BridgeSettings s)
    {
        lock (_lock)
        {
            _current = s;
            _store.Save(s);
        }
    }
}
