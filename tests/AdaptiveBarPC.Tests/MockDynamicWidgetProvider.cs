using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

/// <summary>
/// Test-only mock dynamic widget provider.
/// Demonstrates runtime item addition, removal, in-place updates, reordering, and monotonic revisions.
/// </summary>
public sealed class MockDynamicWidgetProvider : IWidgetProvider
{
    public const string DefaultId = "mock";

    public string Id { get; }

    private readonly List<DynamicItemSnapshot> _items = new();
    private readonly object _gate = new();
    private bool _disposed;
    private int _subscriptionCount;
    private long _revisionCounter;

    public int DisposeCount { get; private set; }
    public int SubscriptionCount => _subscriptionCount;
    public bool IsDisposed => _disposed;

    private EventHandler<WidgetItemsChangedEventArgs>? _itemsChanged;

    public event EventHandler<WidgetItemsChangedEventArgs>? ItemsChanged
    {
        add
        {
            lock (_gate)
            {
                _itemsChanged += value;
                _subscriptionCount++;
            }
        }
        remove
        {
            lock (_gate)
            {
                _itemsChanged -= value;
                _subscriptionCount = Math.Max(0, _subscriptionCount - 1);
            }
        }
    }

    public MockDynamicWidgetProvider(string id = DefaultId)
    {
        Id = id;
        SetSequenceInitial();
    }

    public IReadOnlyList<DynamicItemSnapshot> CurrentItems
    {
        get
        {
            lock (_gate)
                return _items.ToList();
        }
    }

    public void SetItems(IEnumerable<DynamicItemSnapshot> items, long? revision = null)
    {
        IReadOnlyList<DynamicItemSnapshot> snapshot;
        long rev;
        lock (_gate)
        {
            if (_disposed) return;
            _items.Clear();
            _items.AddRange(items);
            snapshot = _items.ToList();
            rev = revision ?? ++_revisionCounter;
        }

        _itemsChanged?.Invoke(this, new WidgetItemsChangedEventArgs(snapshot, rev));
    }

    /// <summary>
    /// Initial sequence: A, B, C
    /// </summary>
    public void SetSequenceInitial()
    {
        SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A", Width = 84 },
            new DynamicItemSnapshot { Id = "B", Label = "B", Width = 84 },
            new DynamicItemSnapshot { Id = "C", Label = "C", Width = 84 },
        });
    }

    /// <summary>
    /// Step 1 sequence: A, C, D (B removed, D added, C kept)
    /// </summary>
    public void SetSequenceStep1()
    {
        SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A", Width = 84 },
            new DynamicItemSnapshot { Id = "C", Label = "C", Width = 84 },
            new DynamicItemSnapshot { Id = "D", Label = "D", Width = 84 },
        });
    }

    /// <summary>
    /// Step 2 sequence: A(updated), C, D (A updated in place)
    /// </summary>
    public void SetSequenceStep2()
    {
        SetItems(new[]
        {
            new DynamicItemSnapshot { Id = "A", Label = "A(updated)", Width = 84 },
            new DynamicItemSnapshot { Id = "C", Label = "C", Width = 84 },
            new DynamicItemSnapshot { Id = "D", Label = "D", Width = 84 },
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeCount++;
            _items.Clear();
        }
    }
}
