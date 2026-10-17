using System.Collections.Concurrent;

namespace AdaptiveBarPC.Services;

/// <summary>
/// Registry for dynamic widget providers.
/// Supports both shared (singleton/app-lifetime) providers and transient (profile/page-owned) providers.
/// </summary>
public sealed class WidgetProviderRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, IWidgetProvider> _sharedProviders =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Func<IWidgetProvider>> _transientFactories =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Registers an application-shared provider instance.
    /// The provider is NOT disposed when a page or profile unloads.
    /// </summary>
    public void RegisterShared(IWidgetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Id))
            throw new ArgumentException("Provider ID cannot be empty.", nameof(provider));

        _sharedProviders[provider.Id] = provider;
    }

    /// <summary>
    /// Registers an application-shared provider instance with an explicit provider ID.
    /// </summary>
    public void RegisterShared(string id, IWidgetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Provider ID cannot be empty.", nameof(id));

        _sharedProviders[id] = provider;
    }

    /// <summary>
    /// Registers a transient provider factory.
    /// Each widget instance creates its own provider and owns its lifecycle (disposed with widget).
    /// </summary>
    public void RegisterTransient(string id, Func<IWidgetProvider> factory)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Provider ID cannot be empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(factory);

        _transientFactories[id] = factory;
    }

    /// <summary>
    /// Resolves a provider by ID.
    /// Returns (Provider, OwnsProvider). If OwnsProvider is true, the caller is responsible for disposing it.
    /// </summary>
    public (IWidgetProvider? Provider, bool OwnsProvider) Resolve(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId) || _disposed)
            return (null, false);

        // 1. Shared providers take precedence
        if (_sharedProviders.TryGetValue(providerId, out var shared))
        {
            return (shared, false);
        }

        // 2. Transient factory
        if (_transientFactories.TryGetValue(providerId, out var factory))
        {
            return (factory(), true);
        }

        return (null, false);
    }

    /// <summary>
    /// Unregisters a provider by ID.
    /// </summary>
    public bool Unregister(string id)
    {
        var removedShared = _sharedProviders.TryRemove(id, out _);
        var removedTransient = _transientFactories.TryRemove(id, out _);
        return removedShared || removedTransient;
    }

    /// <summary>
    /// Clears all registrations and disposes shared providers.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var provider in _sharedProviders.Values)
        {
            try { provider.Dispose(); }
            catch { /* Ignore provider disposal errors on shutdown */ }
        }

        _sharedProviders.Clear();
        _transientFactories.Clear();
    }
}
