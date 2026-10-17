using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services.Audio;

/// <summary>
/// Production dynamic widget provider for per-application audio sessions.
/// Converts live application audio groups into DynamicItemSnapshots for the Dynamic Widget Engine.
/// </summary>
public sealed class AudioSessionProvider : IWidgetProvider
{
    public const string DefaultProviderId = "audioSessions";

    public string Id { get; } = DefaultProviderId;

    private readonly WindowsAudioSessionService _service;
    private readonly ForegroundAppService? _foregroundService;
    private readonly AppLogger? _logger;
    private readonly object _gate = new();

    private IReadOnlyList<DynamicItemSnapshot> _currentItems = Array.Empty<DynamicItemSnapshot>();
    private long _revisionCounter;
    private bool _disposed;

    public event EventHandler<WidgetItemsChangedEventArgs>? ItemsChanged;

    public IReadOnlyList<DynamicItemSnapshot> CurrentItems
    {
        get
        {
            lock (_gate)
                return _currentItems;
        }
    }

    public long CurrentRevision => Volatile.Read(ref _revisionCounter);
    public bool IsDisposed => _disposed;

    public AudioSessionProvider(
        WindowsAudioSessionService service,
        ForegroundAppService? foregroundService = null,
        AppLogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _foregroundService = foregroundService;
        _logger = logger;

        _service.GroupsChanged += OnGroupsChanged;
        if (_foregroundService != null)
        {
            _foregroundService.ContextChanged += OnForegroundContextChanged;
        }

        // Publish initial snapshot synchronously
        PublishSnapshot(isInitial: true);
    }

    private void OnGroupsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        PublishSnapshot(isInitial: false);
    }

    private void OnForegroundContextChanged(object? sender, ForegroundAppInfo context)
    {
        if (_disposed) return;
        _service.ReconcileGroups(context);
        PublishSnapshot(isInitial: false);
    }

    public void PublishSnapshot(bool isInitial = false)
    {
        var groups = _service.CurrentGroups;
        var newSnapshot = BuildSnapshots(groups);
        WidgetItemsChangedEventArgs? changedArgs = null;

        lock (_gate)
        {
            if (_disposed) return;

            if (!isInitial && SnapshotsEqual(_currentItems, newSnapshot))
            {
                return;
            }

            _currentItems = newSnapshot;
            long revision = Interlocked.Increment(ref _revisionCounter);
            changedArgs = new WidgetItemsChangedEventArgs(newSnapshot, revision);
        }

        try { ItemsChanged?.Invoke(this, changedArgs); }
        catch (Exception ex) { _logger?.Warn($"Error invoking ItemsChanged in AudioSessionProvider: {ex.Message}"); }
    }

    internal static IReadOnlyList<DynamicItemSnapshot> BuildSnapshots(IReadOnlyList<AudioApplicationGroup> groups)
    {
        if (groups == null || groups.Count == 0)
        {
            // Empty state placeholder preserving stable mixer page navigation
            return new DynamicItemSnapshot[]
            {
                new()
                {
                    Id = "audio:empty",
                    Type = WidgetTypes.Button,
                    Label = "No app audio",
                    Width = 120,
                    OnClick = null
                }
            };
        }

        var list = new List<DynamicItemSnapshot>(groups.Count);
        foreach (var group in groups)
        {
            var capturedGroup = group;
            list.Add(new DynamicItemSnapshot
            {
                Id = capturedGroup.Id,
                Type = WidgetTypes.AudioSession,
                Label = capturedGroup.FriendlyName,
                Width = 230,
                Value = Math.Round(capturedGroup.EffectiveVolume * 100.0),
                Min = 0,
                Max = 100,
                IsChecked = capturedGroup.IsMuted,
                ValueSetter = vol => capturedGroup.SetVolume((float)(vol / 100.0)),
                OnToggle = muted => capturedGroup.SetMute(muted),
                CustomData = capturedGroup
            });
        }

        return list;
    }

    private static bool SnapshotsEqual(IReadOnlyList<DynamicItemSnapshot> a, IReadOnlyList<DynamicItemSnapshot> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            var sa = a[i];
            var sb = b[i];

            if (sa.Id != sb.Id) return false;
            if (sa.Type != sb.Type) return false;
            if (sa.Label != sb.Label) return false;
            if (sa.IsChecked != sb.IsChecked) return false;
            if (Math.Abs(sa.Value - sb.Value) > 0.01) return false;
            if (sa.CustomData is AudioApplicationGroup ga &&
                sb.CustomData is AudioApplicationGroup gb &&
                ga.MembershipFingerprint != gb.MembershipFingerprint) return false;
        }

        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            _service.GroupsChanged -= OnGroupsChanged;
            if (_foregroundService != null)
            {
                _foregroundService.ContextChanged -= OnForegroundContextChanged;
            }
        }
    }
}
