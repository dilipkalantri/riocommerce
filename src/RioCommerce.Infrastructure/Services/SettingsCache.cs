using System.Collections.Concurrent;

namespace RioCommerce.Infrastructure.Services;

// Process-wide settings cache. Singleton so values survive across scoped requests/circuits; the app runs
// as a single process (API host + Blazor Server) so one in-memory copy is authoritative. Holds the raw
// stored value (secrets stay encrypted here); decryption happens in SettingService on read.
public sealed class SettingsCache
{
    private readonly ConcurrentDictionary<string, string?> _map = new(StringComparer.Ordinal);
    private volatile bool _loaded;

    public bool Loaded => _loaded;

    public void Load(IDictionary<string, string?> all)
    {
        _map.Clear();
        foreach (var kv in all) _map[kv.Key] = kv.Value;
        _loaded = true;
    }

    public bool TryGet(string key, out string? value) => _map.TryGetValue(key, out value);
    public void Set(string key, string? value) => _map[key] = value;
    public void Clear() { _map.Clear(); _loaded = false; }
}
