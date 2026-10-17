using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

/// <summary>
/// Event arguments fired when a dynamic widget provider's items change.
/// Revision semantics:
/// - Revision > 0: Monotonic versioned update; consumers ignore stale or repeated revisions.
/// - Revision == 0: Legacy or unversioned provider update; applied unconditionally.
/// Production dynamic providers should use monotonic revisions starting at 1.
/// </summary>
public sealed class WidgetItemsChangedEventArgs : EventArgs
{
    public IReadOnlyList<DynamicItemSnapshot> Items { get; }

    /// <summary>
    /// Monotonic revision sequence number. 0 indicates legacy or unversioned snapshot.
    /// </summary>
    public long Revision { get; }

    public WidgetItemsChangedEventArgs(IReadOnlyList<DynamicItemSnapshot> items, long revision = 0)
    {
        Items = items ?? Array.Empty<DynamicItemSnapshot>();
        Revision = revision;
    }
}

/// <summary>
/// Contract for runtime dynamic widget providers.
/// A dynamic provider updates ONLY its own items and must not force page/profile rebuilds.
/// </summary>
public interface IWidgetProvider : IDisposable
{
    /// <summary>
    /// Stable identifier of the provider (e.g. "audioSessions", "sensors", "mock").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Current snapshot of items emitted by this provider.
    /// </summary>
    IReadOnlyList<DynamicItemSnapshot> CurrentItems { get; }

    /// <summary>
    /// Raised when items are added, removed, updated, or reordered.
    /// </summary>
    event EventHandler<WidgetItemsChangedEventArgs>? ItemsChanged;
}
