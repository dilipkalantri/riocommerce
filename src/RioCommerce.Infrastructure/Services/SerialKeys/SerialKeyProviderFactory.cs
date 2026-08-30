using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services.SerialKeys;

/// <summary>
/// Resolves the right <see cref="ISerialKeyProvider"/> for a record. All providers registered
/// in DI via <c>AddScoped&lt;ISerialKeyProvider&gt;</c> flow in via the IEnumerable ctor injection;
/// adding a new vendor is one line in <c>Program.cs</c> and zero changes here.
/// </summary>
public sealed class SerialKeyProviderFactory : ISerialKeyProviderFactory
{
    private readonly Dictionary<string, ISerialKeyProvider> _byKey;
    private readonly List<ISerialKeyProvider> _all;

    public SerialKeyProviderFactory(IEnumerable<ISerialKeyProvider> providers)
    {
        _all = providers.ToList();
        _byKey = _all.ToDictionary(p => p.Key, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public ISerialKeyProvider Get(string providerKey)
        => _byKey.TryGetValue(providerKey, out var p)
            ? p
            : throw new InvalidOperationException($"No serial-key provider registered for '{providerKey}'.");

    public bool TryGet(string providerKey, out ISerialKeyProvider provider)
        => _byKey.TryGetValue(providerKey, out provider!);

    public IReadOnlyList<ISerialKeyProvider> All() => _all;
}
