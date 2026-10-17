using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services.Audio;

/// <summary>
/// Service responsible for per-application audio session discovery, grouping, state evaluation, and reconciliation.
/// </summary>
public sealed class WindowsAudioSessionService : IDisposable
{
    private readonly IAudioSessionBackend _backend;
    private readonly AppLogger? _logger;
    private readonly int _reconcileIntervalMs;
    private System.Threading.Timer? _timer;
    private readonly object _gate = new();

    private IReadOnlyList<AudioApplicationGroup> _currentGroups = Array.Empty<AudioApplicationGroup>();
    private IReadOnlyList<AudioSessionDescriptor> _lastSessions = Array.Empty<AudioSessionDescriptor>();
    private ForegroundAppInfo? _foreground;
    private int _backendStarted;
    private int _reconcileRequested;
    private int _reconcileWorkerQueued;
    private volatile bool _disposed;

    public event EventHandler? GroupsChanged;

    public IReadOnlyList<AudioApplicationGroup> CurrentGroups
    {
        get
        {
            lock (_gate)
                return _currentGroups;
        }
    }

    public WindowsAudioSessionService(
        IAudioSessionBackend? backend = null,
        AppLogger? logger = null,
        int reconcileIntervalMs = 1200)
    {
        _logger = logger;
        _reconcileIntervalMs = Math.Clamp(reconcileIntervalMs, 500, 5000);
        _backend = backend ?? new WindowsCoreAudioBackend(logger);

        if (_backend is WindowsCoreAudioBackend)
        {
            QueueReconcile();
            return;
        }

        if (EnsureBackendStarted())
            ReconcileBackend();
    }

    private bool EnsureBackendStarted()
    {
        if (_disposed) return false;
        if (Volatile.Read(ref _backendStarted) != 0) return true;

        bool initialized = false;
        try
        {
            initialized = _backend.Initialize();
        }
        catch (Exception ex)
        {
            _logger?.Warn($"WindowsAudioSessionService backend initialization failed: {ex.Message}");
        }

        if (!initialized)
        {
            _logger?.Warn("WindowsAudioSessionService started in unavailable/empty state.");
            return false;
        }

        lock (_gate)
        {
            if (_disposed) return false;
            if (Interlocked.CompareExchange(ref _backendStarted, 1, 0) == 0)
            {
                _backend.SessionsChanged += OnBackendSessionsChanged;
                _timer = new System.Threading.Timer(OnPollTimer, null, _reconcileIntervalMs, _reconcileIntervalMs);
            }
        }
        return true;
    }

    private void OnPollTimer(object? state)
    {
        if (_disposed) return;
        QueueReconcile();
    }

    private void OnBackendSessionsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        QueueReconcile();
    }

    public void ReconcileGroups(ForegroundAppInfo? foreground)
    {
        if (foreground != null)
        {
            IReadOnlyList<AudioApplicationGroup>? reordered = null;
            lock (_gate)
            {
                if (_disposed) return;
                _foreground = foreground;
                var groups = BuildApplicationGroups(_lastSessions, _foreground);
                if (!GroupsEqual(_currentGroups, groups))
                {
                    _currentGroups = groups;
                    reordered = groups;
                }
            }
            if (reordered != null) RaiseGroupsChanged();
            return;
        }

        ReconcileBackend();
    }

    private void QueueReconcile()
    {
        if (_disposed) return;
        Volatile.Write(ref _reconcileRequested, 1);
        if (Interlocked.CompareExchange(ref _reconcileWorkerQueued, 1, 0) != 0) return;

        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var self = (WindowsAudioSessionService)state!;
            while (!self._disposed)
            {
                Interlocked.Exchange(ref self._reconcileRequested, 0);
                if (self.EnsureBackendStarted())
                    self.ReconcileBackend();

                if (Volatile.Read(ref self._reconcileRequested) != 0)
                    continue;

                Volatile.Write(ref self._reconcileWorkerQueued, 0);
                if (Volatile.Read(ref self._reconcileRequested) == 0 ||
                    Interlocked.CompareExchange(ref self._reconcileWorkerQueued, 1, 0) != 0)
                    return;
            }
        }, this, preferLocal: false);
    }

    private void ReconcileBackend()
    {
        IReadOnlyList<AudioSessionDescriptor> rawSessions;
        try
        {
            rawSessions = _backend.GetSessions();
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Error retrieving audio sessions from backend: {ex.Message}");
            rawSessions = Array.Empty<AudioSessionDescriptor>();
        }

        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            _lastSessions = rawSessions;
            var newGroups = BuildApplicationGroups(rawSessions, _foreground);
            changed = !GroupsEqual(_currentGroups, newGroups);
            if (changed) _currentGroups = newGroups;
        }
        if (changed) RaiseGroupsChanged();
    }

    private void RaiseGroupsChanged()
    {
        try { GroupsChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { _logger?.Warn($"Error notifying GroupsChanged: {ex.Message}"); }
    }

    /// <summary>
    /// Pure function grouping raw session descriptors into application-level groups and ordering them.
    /// Internal for direct unit testing.
    /// </summary>
    internal static IReadOnlyList<AudioApplicationGroup> BuildApplicationGroups(
        IReadOnlyList<AudioSessionDescriptor> sessions,
        ForegroundAppInfo? foreground = null)
    {
        if (sessions == null || sessions.Count == 0)
        {
            return Array.Empty<AudioApplicationGroup>();
        }

        int currentPid = Environment.ProcessId;

        // 1. Filter sessions
        var validSessions = new List<AudioSessionDescriptor>();
        foreach (var s in sessions)
        {
            if (s == null) continue;
            if (s.IsSystemSound) continue;
            if (s.ProcessId <= 4 || s.ProcessId == currentPid) continue;
            if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

            // Inactive session rule: verify process is still alive
            if (s.State == AudioSessionState.AudioSessionStateInactive)
            {
                if (!IsProcessAlive(s.ProcessId))
                {
                    continue; // Discard dead inactive process
                }
            }

            validSessions.Add(s);
        }

        if (validSessions.Count == 0)
        {
            return Array.Empty<AudioApplicationGroup>();
        }

        // 2. Group by stable application identity + endpoint
        // Group key: endpointId:appKey
        var groupedDict = new Dictionary<string, List<AudioSessionDescriptor>>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in validSessions)
        {
            var appIdentity = DeriveApplicationIdentity(session);
            var groupKey = $"{session.EndpointId}:{StableKey(appIdentity)}";

            if (!groupedDict.TryGetValue(groupKey, out var list))
            {
                list = new List<AudioSessionDescriptor>();
                groupedDict[groupKey] = list;
            }
            list.Add(session);
        }

        // 3. Construct AudioApplicationGroup for each bucket
        var resultGroups = new List<AudioApplicationGroup>(groupedDict.Count);
        foreach (var (groupKey, sessionList) in groupedDict)
        {
            var firstSession = sessionList[0];
            var appKey = DeriveApplicationKey(firstSession);
            var endpointKey = $"{SanitizeKey(firstSession.EndpointId)}-{StableKey(firstSession.EndpointId)[..6]}";
            var stableId = $"audio:{endpointKey}:{StableKey(DeriveApplicationIdentity(firstSession))}";

            // Pick best friendly name: FileDescription / ProductName / FriendlyAppName, else stem, else process name
            var friendlyName = sessionList
                .Select(s => s.FriendlyAppName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? (!string.IsNullOrWhiteSpace(firstSession.ProcessName)
                    ? firstSession.ProcessName
                    : appKey);

            var exePath = sessionList
                .Select(s => s.ExecutablePath)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

            // Aggregation: Average volume, All muted
            float avgVol = (float)sessionList.Average(s => s.Volume);
            bool allMuted = sessionList.All(s => s.IsMuted);

            // State: Active if any is Active, else Inactive
            var state = sessionList.Any(s => s.State == AudioSessionState.AudioSessionStateActive)
                ? AudioSessionState.AudioSessionStateActive
                : AudioSessionState.AudioSessionStateInactive;

            // Foreground match
            bool isForeground = false;
            if (foreground != null)
            {
                isForeground = (!string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(foreground.ExecutablePath) &&
                        string.Equals(exePath, foreground.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(foreground.ProcessName) &&
                        sessionList.Any(s => string.Equals(
                            NormalizeProcessName(s.ProcessName),
                            NormalizeProcessName(foreground.ProcessName),
                            StringComparison.OrdinalIgnoreCase)))
                    || (!string.IsNullOrWhiteSpace(friendlyName) && !string.IsNullOrWhiteSpace(foreground.FriendlyAppName) &&
                        string.Equals(friendlyName, foreground.FriendlyAppName, StringComparison.OrdinalIgnoreCase));
            }

            resultGroups.Add(new AudioApplicationGroup
            {
                Id = stableId,
                ApplicationKey = appKey,
                FriendlyName = friendlyName,
                ExecutablePath = exePath,
                EndpointId = firstSession.EndpointId,
                MembershipFingerprint = string.Join("|", sessionList
                    .Select(SessionIdentity)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                Sessions = sessionList.ToArray(),
                EffectiveVolume = avgVol,
                IsMuted = allMuted,
                IsForeground = isForeground,
                State = state
            });
        }

        // 4. Deterministic ordering:
        // 1. Foreground application first
        // 2. Active audio applications before Inactive
        // 3. Alphabetical by FriendlyName
        // 4. By Id
        var ordered = resultGroups
            .OrderByDescending(g => g.IsForeground)
            .ThenByDescending(g => g.State == AudioSessionState.AudioSessionStateActive)
            .ThenBy(g => g.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ordered;
    }

    internal static string DeriveApplicationKey(AudioSessionDescriptor session)
    {
        if (!string.IsNullOrWhiteSpace(session.ExecutablePath))
        {
            var filename = Path.GetFileName(session.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(filename))
                return NormalizeProcessName(filename).ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(session.ProcessName))
            return NormalizeProcessName(session.ProcessName).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(session.SessionInstanceIdentifier))
            return SanitizeKey(session.SessionInstanceIdentifier);
        if (!string.IsNullOrWhiteSpace(session.SessionIdentifier))
            return SanitizeKey(session.SessionIdentifier);
        return $"pid-{session.ProcessId}";
    }

    private static string DeriveApplicationIdentity(AudioSessionDescriptor session)
    {
        if (!string.IsNullOrWhiteSpace(session.ExecutablePath))
        {
            return Path.GetFullPath(session.ExecutablePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(session.ProcessName))
            return $"name:{NormalizeProcessName(session.ProcessName).ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(session.SessionInstanceIdentifier))
            return $"instance:{session.SessionInstanceIdentifier.Trim()}";
        if (!string.IsNullOrWhiteSpace(session.SessionIdentifier))
            return $"session:{session.SessionIdentifier.Trim()}";
        return $"pid:{session.ProcessId}";
    }

    private static string SessionIdentity(AudioSessionDescriptor session) =>
        !string.IsNullOrWhiteSpace(session.SessionInstanceIdentifier) ? session.SessionInstanceIdentifier :
        !string.IsNullOrWhiteSpace(session.SessionIdentifier) ? session.SessionIdentifier :
        $"pid:{session.ProcessId}";

    private static string StableKey(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    internal static string NormalizeProcessName(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    internal static string SanitizeKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "default";

        // For long GUIDs or endpoint IDs, produce a clean stable identifier
        var clean = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                clean.Append(c);
            else if (c == '{' || c == '}' || c == '.' || c == ':')
                clean.Append('-');
        }

        var result = clean.ToString().Trim('-').ToLowerInvariant();
        if (result.Length > 40)
        {
            // Hash long endpoint strings deterministically for clean IDs
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)))[..12].ToLowerInvariant();
            return hash;
        }

        return string.IsNullOrEmpty(result) ? "endpoint" : result;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool GroupsEqual(IReadOnlyList<AudioApplicationGroup> a, IReadOnlyList<AudioApplicationGroup> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            var ga = a[i];
            var gb = b[i];

            if (ga.Id != gb.Id) return false;
            if (ga.FriendlyName != gb.FriendlyName) return false;
            if (ga.IsMuted != gb.IsMuted) return false;
            if (ga.IsForeground != gb.IsForeground) return false;
            if (ga.State != gb.State) return false;
            if (ga.MembershipFingerprint != gb.MembershipFingerprint) return false;
            if (Math.Abs(ga.EffectiveVolume - gb.EffectiveVolume) > 0.005f) return false;
        }

        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            var timer = _timer;
            _timer = null;
            timer?.Dispose();
            _backend.SessionsChanged -= OnBackendSessionsChanged;
            _backend.Dispose();
        }
    }
}
