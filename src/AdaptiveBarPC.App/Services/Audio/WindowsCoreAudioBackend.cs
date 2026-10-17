using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AdaptiveBarPC.Interop;

namespace AdaptiveBarPC.Services.Audio;

/// <summary>
/// Native Windows Core Audio backend implementation.
/// Discovers live application audio sessions on the default render endpoint and listens for Core Audio events.
/// </summary>
public sealed class WindowsCoreAudioBackend : IAudioSessionBackend
{
    private static readonly Guid AudioSessionManager2Guid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private readonly AppLogger? _logger;
    private readonly Func<IMMDeviceEnumerator> _createEnumerator;
    private readonly object _gate = new();

    private IMMDeviceEnumerator? _enumerator;
    private NotificationClientSink? _notificationClient;
    private IMMDevice? _currentDevice;
    private IAudioSessionManager2? _sessionManager;
    private SessionNotificationSink? _sessionNotification;

    private readonly Dictionary<string, TrackedSession> _trackedSessions = new(StringComparer.OrdinalIgnoreCase);

    private string _currentEndpointId = "default";
    private string _currentEndpointName = "Default Audio Endpoint";
    private string? _pendingEndpointId;
    private long _endpointGeneration;
    private long _pendingSessionGeneration;
    private int _endpointRebindPending;
    private int _endpointNotificationPending;
    private int _notificationWorkerQueued;
    private int _isBound;
    private bool _initialized;
    private volatile bool _disposed;

    public string CurrentEndpointId
    {
        get { lock (_gate) return _currentEndpointId; }
    }

    public string CurrentEndpointName
    {
        get { lock (_gate) return _currentEndpointName; }
    }

    public event EventHandler? SessionsChanged;

    public WindowsCoreAudioBackend(AppLogger? logger = null)
        : this(logger, () => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject())
    {
    }

    // Keep fault-injection at the COM boundary; production still uses MMDeviceEnumerator.
    internal WindowsCoreAudioBackend(AppLogger? logger, Func<IMMDeviceEnumerator> createEnumerator)
    {
        _logger = logger;
        _createEnumerator = createEnumerator;
    }

    public bool Initialize()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (_initialized) return true;

            try
            {
                _enumerator = _createEnumerator();
                var notificationClient = new NotificationClientSink(this);
                Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(notificationClient));
                _notificationClient = notificationClient;
                BindCurrentEndpoint_Locked();
                _initialized = true;

                _logger?.Info($"WindowsCoreAudioBackend initialized. Default endpoint: {_currentEndpointName} ({_currentEndpointId})");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"WindowsCoreAudioBackend failed to initialize: {ex.Message}");
                CleanupEndpoint_Locked();
                CleanupEnumerator_Locked();
                return false;
            }
        }
    }

    private void BindCurrentEndpoint_Locked()
    {
        Interlocked.Increment(ref _endpointGeneration);
        CleanupEndpoint_Locked();

        if (_enumerator == null) return;

        Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(
            EDataFlow.eRender, ERole.eMultimedia, out _currentDevice));
        var device = _currentDevice ?? throw new InvalidOperationException("Default audio endpoint was not returned.");

        // Query Endpoint ID
        if (device.GetId(out var endpointId) >= 0 && !string.IsNullOrWhiteSpace(endpointId))
        {
            _currentEndpointId = endpointId;
        }

        // Query Endpoint Friendly Name
        _currentEndpointName = QueryDeviceFriendlyName(device) ?? "Default Audio Endpoint";

        // Activate Audio Session Manager 2
        object? managerObj = null;
        try
        {
            Marshal.ThrowExceptionForHR(device.Activate(AudioSessionManager2Guid, CLSCTX.ALL, IntPtr.Zero, out managerObj));
            _sessionManager = managerObj as IAudioSessionManager2
                ?? throw new InvalidCastException("Activated object does not support IAudioSessionManager2.");
            managerObj = null; // Transfer the activation reference; the interface cast is only an alias.
            var sessionNotification = new SessionNotificationSink(this, Volatile.Read(ref _endpointGeneration));
            Marshal.ThrowExceptionForHR(_sessionManager.RegisterSessionNotification(sessionNotification));
            _sessionNotification = sessionNotification;
            Volatile.Write(ref _isBound, 1);
            Volatile.Write(ref _endpointRebindPending, 0);
            Interlocked.Exchange(ref _pendingEndpointId, null);
        }
        finally
        {
            ReleaseCom(managerObj);
        }
    }

    private static string? QueryDeviceFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        PropVariant pv = default; // VT_EMPTY is safe to clear, including failed/partial native writes.
        try
        {
            if (device.OpenPropertyStore(0, out store) >= 0 && store != null)
            {
                var key = PropertyKey.PKEY_Device_FriendlyName;
                if (store.GetValue(ref key, out pv) >= 0)
                    return pv.GetString();
            }
        }
        catch { }
        finally
        {
            try { Marshal.ThrowExceptionForHR(PropVariantInterop.PropVariantClear(ref pv)); }
            finally { ReleaseCom(store); }
        }
        return null;
    }

    public IReadOnlyList<AudioSessionDescriptor> GetSessions()
    {
        lock (_gate)
        {
            if (_disposed || !_initialized)
            {
                return Array.Empty<AudioSessionDescriptor>();
            }

            if (_sessionManager == null || Volatile.Read(ref _endpointRebindPending) != 0)
            {
                try
                {
                    var requestedEndpoint = Volatile.Read(ref _pendingEndpointId);
                    if (_sessionManager == null || string.IsNullOrWhiteSpace(requestedEndpoint) ||
                        !string.Equals(requestedEndpoint, _currentEndpointId, StringComparison.OrdinalIgnoreCase))
                    {
                        BindCurrentEndpoint_Locked();
                    }
                    else
                    {
                        Volatile.Write(ref _endpointRebindPending, 0);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Audio endpoint bind retry failed: {ex.Message}");
                    CleanupEndpoint_Locked();
                    Volatile.Write(ref _endpointRebindPending, 1);
                    return Array.Empty<AudioSessionDescriptor>();
                }
            }

            if (_sessionManager == null)
                return Array.Empty<AudioSessionDescriptor>();

            var discoveredDescriptors = new List<AudioSessionDescriptor>();
            var survivingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IAudioSessionEnumerator? sessionEnum = null;
            bool enumerationCompleted = false;
            try
            {
                Marshal.ThrowExceptionForHR(_sessionManager.GetSessionEnumerator(out sessionEnum));
                if (sessionEnum == null)
                {
                    return Array.Empty<AudioSessionDescriptor>();
                }

                Marshal.ThrowExceptionForHR(sessionEnum.GetCount(out var count));
                if (count < 0) throw new InvalidOperationException("Audio session count was negative.");

                int currentPid = Environment.ProcessId;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl? sessionControl = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(sessionEnum.GetSession(i, out sessionControl));
                        if (sessionControl == null)
                            continue;

                        if (sessionControl is not IAudioSessionControl2 sessionControl2)
                        {
                            continue;
                        }

                        // Filter out Windows system sounds sessions
                        int systemSoundsHr = sessionControl2.IsSystemSoundsSession();
                        Marshal.ThrowExceptionForHR(systemSoundsHr);
                        if (systemSoundsHr == 0) // S_OK = system sounds; S_FALSE = ordinary session.
                        {
                            continue;
                        }

                        // Query Process ID
                        // AUDCLNT_S_NO_SINGLE_PROCESS is successful and supplies the creator PID.
                        Marshal.ThrowExceptionForHR(sessionControl2.GetProcessId(out var pid));
                        if (pid <= 4 || pid > int.MaxValue || pid == currentPid)
                        {
                            continue;
                        }

                        // Query Session State
                        Marshal.ThrowExceptionForHR(sessionControl2.GetState(out var state));
                        if (state == AudioSessionState.AudioSessionStateExpired)
                        {
                            continue;
                        }

                        // Query Session Identifier
                        string sessionId = "";
                        string instanceId = "";
                        try { if (sessionControl2.GetSessionIdentifier(out sessionId) < 0) sessionId = ""; } catch { }
                        try { if (sessionControl2.GetSessionInstanceIdentifier(out instanceId) < 0) instanceId = ""; } catch { }

                        var trackingKey = !string.IsNullOrWhiteSpace(instanceId)
                            ? instanceId
                            : (!string.IsNullOrWhiteSpace(sessionId) ? sessionId : $"pid:{pid}:idx:{i}");

                        survivingKeys.Add(trackingKey);

                        if (!_trackedSessions.TryGetValue(trackingKey, out var tracked))
                        {
                            tracked = new TrackedSession(
                                this,
                                Volatile.Read(ref _endpointGeneration),
                                trackingKey,
                                (int)pid,
                                sessionControl,
                                sessionControl2);
                            _trackedSessions[trackingKey] = tracked;
                            sessionControl = null; // TrackedSession owns this acquisition from now on.
                        }

                        var descriptor = tracked.CreateDescriptor(_currentEndpointId, _currentEndpointName);
                        if (descriptor != null)
                        {
                            discoveredDescriptors.Add(descriptor);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"Error inspecting audio session at index {i}: {ex.Message}");
                    }
                    finally
                    {
                        // Balance only this GetSession acquisition, never the tracked RCW's other references.
                        ReleaseCom(sessionControl);
                    }
                }
                enumerationCompleted = true;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Error during audio session enumeration: {ex.Message}");
            }
            finally
            {
                ReleaseCom(sessionEnum);
            }

            // Clean up sessions that no longer exist
            var deadKeys = enumerationCompleted
                ? _trackedSessions.Keys.Where(k => !survivingKeys.Contains(k)).ToList()
                : new List<string>();
            foreach (var deadKey in deadKeys)
            {
                if (_trackedSessions.Remove(deadKey, out var deadTracked))
                {
                    deadTracked.Dispose();
                }
            }

            return discoveredDescriptors;
        }
    }

    private void OnDefaultEndpointChanged(string endpointId)
    {
        if (_disposed) return;

        if (Volatile.Read(ref _isBound) != 0 &&
            string.Equals(endpointId, Volatile.Read(ref _currentEndpointId), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Interlocked.Exchange(ref _pendingEndpointId, endpointId);
        Volatile.Write(ref _endpointRebindPending, 1);
        Volatile.Write(ref _endpointNotificationPending, 1);
        QueueSessionsChangedWorker();
    }

    internal void NotifySessionsChanged(long generation)
    {
        if (_disposed) return;

        if (generation != Volatile.Read(ref _endpointGeneration))
            return;

        long observed;
        do
        {
            observed = Volatile.Read(ref _pendingSessionGeneration);
            if (observed >= generation) break;
        }
        while (Interlocked.CompareExchange(ref _pendingSessionGeneration, generation, observed) != observed);

        QueueSessionsChangedWorker();
    }

    private void QueueSessionsChangedWorker()
    {
        if (_disposed) return;
        if (Interlocked.CompareExchange(ref _notificationWorkerQueued, 1, 0) != 0) return;

        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            ((WindowsCoreAudioBackend)state!).DrainSessionsChangedWorker();
        }, this, preferLocal: false);
    }

    private void DrainSessionsChangedWorker()
    {
        while (!_disposed)
        {
            var endpointPending = Interlocked.Exchange(ref _endpointNotificationPending, 0) != 0;
            var sessionGeneration = Interlocked.Exchange(ref _pendingSessionGeneration, 0);
            var shouldNotify = endpointPending ||
                (sessionGeneration != 0 && sessionGeneration == Volatile.Read(ref _endpointGeneration));

            if (shouldNotify)
            {
                try { SessionsChanged?.Invoke(this, EventArgs.Empty); }
                catch { }
            }

            Volatile.Write(ref _notificationWorkerQueued, 0);

            if (_disposed ||
                (Volatile.Read(ref _endpointNotificationPending) == 0 && Volatile.Read(ref _pendingSessionGeneration) == 0) ||
                Interlocked.CompareExchange(ref _notificationWorkerQueued, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private void CleanupEndpoint_Locked()
    {
        Volatile.Write(ref _isBound, 0);
        foreach (var tracked in _trackedSessions.Values)
        {
            tracked.Dispose();
        }
        _trackedSessions.Clear();

        if (_sessionManager != null)
        {
            if (_sessionNotification != null)
            {
                try { Marshal.ThrowExceptionForHR(_sessionManager.UnregisterSessionNotification(_sessionNotification)); }
                catch (Exception ex) { _logger?.Warn($"Failed to unregister session notifications: {ex.Message}"); }
                _sessionNotification = null;
            }
            ReleaseCom(_sessionManager);
            _sessionManager = null;
        }

        if (_currentDevice != null)
        {
            ReleaseCom(_currentDevice);
            _currentDevice = null;
        }
        _currentEndpointId = "default";
        _currentEndpointName = "Default Audio Endpoint";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            CleanupEndpoint_Locked();
            CleanupEnumerator_Locked();
        }
    }

    private void CleanupEnumerator_Locked()
    {
        if (_enumerator != null)
        {
            if (_notificationClient != null)
            {
                try { Marshal.ThrowExceptionForHR(_enumerator.UnregisterEndpointNotificationCallback(_notificationClient)); }
                catch (Exception ex) { _logger?.Warn($"Failed to unregister endpoint notifications: {ex.Message}"); }
                _notificationClient = null;
            }
            ReleaseCom(_enumerator);
            _enumerator = null;
        }
        _initialized = false;
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            // One release per acquired RCW reference; interface casts do not add ownership.
            try { Marshal.ReleaseComObject(obj); }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Nested Helper Classes
    // ------------------------------------------------------------------

    private sealed class TrackedSession : IDisposable
    {
        private readonly WindowsCoreAudioBackend _backend;
        private readonly long _generation;
        private readonly string _key;
        private readonly int _processId;
        private readonly IAudioSessionControl _control;
        private readonly IAudioSessionControl2 _control2;
        private readonly ISimpleAudioVolume? _volume;
        private readonly AudioSessionEventsSink _eventsSink;

        private string? _executablePath;
        private string _processName = "";
        private string _friendlyName = "";
        private bool _metadataResolved;
        private bool _eventsRegistered;
        private bool _disposed;

        public TrackedSession(
            WindowsCoreAudioBackend backend,
            long generation,
            string key,
            int processId,
            IAudioSessionControl control,
            IAudioSessionControl2 control2)
        {
            _backend = backend;
            _generation = generation;
            _key = key;
            _processId = processId;
            _control = control;
            _control2 = control2;
            _volume = control as ISimpleAudioVolume;

            _eventsSink = new AudioSessionEventsSink(this);
            try
            {
                Marshal.ThrowExceptionForHR(_control.RegisterAudioSessionNotification(_eventsSink));
                _eventsRegistered = true;
            }
            catch (Exception ex) { _backend._logger?.Warn($"Failed to register audio session notifications: {ex.Message}"); }
        }

        private void EnsureMetadata()
        {
            if (_metadataResolved) return;
            _metadataResolved = true;

            try
            {
                _executablePath = AppMetadataService.TryGetExecutablePath(_processId);

                try
                {
                    using var proc = Process.GetProcessById(_processId);
                    _processName = proc.ProcessName;
                }
                catch
                {
                    if (!string.IsNullOrWhiteSpace(_executablePath))
                        _processName = Path.GetFileNameWithoutExtension(_executablePath);
                }

                var (friendly, _) = AppMetadataService.Resolve(_processName, _executablePath);
                _friendlyName = friendly;
            }
            catch
            {
                _friendlyName = !string.IsNullOrWhiteSpace(_processName) ? _processName : $"App ({_processId})";
            }
        }

        public AudioSessionDescriptor? CreateDescriptor(string endpointId, string endpointFriendlyName)
        {
            if (_disposed) return null;

            EnsureMetadata();

            // Descriptor reads run under the backend gate, like writes and release.
            Marshal.ThrowExceptionForHR(_control.GetState(out var state));

            if (state == AudioSessionState.AudioSessionStateExpired || _volume == null)
                return null;

            Marshal.ThrowExceptionForHR(_volume.GetMasterVolume(out var vol));
            Marshal.ThrowExceptionForHR(_volume.GetMute(out var isMuted));

            string sessionId = "";
            try { if (_control2.GetSessionIdentifier(out sessionId) < 0) sessionId = ""; } catch { }

            return new AudioSessionDescriptor
            {
                EndpointId = endpointId,
                EndpointFriendlyName = endpointFriendlyName,
                SessionIdentifier = sessionId,
                SessionInstanceIdentifier = _key,
                ProcessId = _processId,
                ExecutablePath = _executablePath,
                ProcessName = _processName,
                FriendlyAppName = _friendlyName,
                State = state,
                Volume = vol,
                IsMuted = isMuted,
                SetVolumeAction = SetVolume,
                SetMuteAction = SetMute
            };
        }

        public void SetVolume(float volume)
        {
            lock (_backend._gate)
            {
                if (_disposed || _volume == null) return;
                try
                {
                    Marshal.ThrowExceptionForHR(_volume.SetMasterVolume(Math.Clamp(volume, 0.0f, 1.0f), Guid.Empty));
                }
                catch (Exception ex) { _backend._logger?.Warn($"Failed to set audio session volume: {ex.Message}"); }
            }
        }

        public void SetMute(bool mute)
        {
            lock (_backend._gate)
            {
                if (_disposed || _volume == null) return;
                try
                {
                    Marshal.ThrowExceptionForHR(_volume.SetMute(mute, Guid.Empty));
                }
                catch (Exception ex) { _backend._logger?.Warn($"Failed to set audio session mute: {ex.Message}"); }
            }
        }

        internal void OnVolumeOrStateChanged()
        {
            if (_disposed) return;
            _backend.NotifySessionsChanged(_generation);
        }

        public void Dispose()
        {
            lock (_backend._gate)
            {
                if (_disposed) return;
                _disposed = true;

                if (_eventsRegistered)
                {
                    try { Marshal.ThrowExceptionForHR(_control.UnregisterAudioSessionNotification(_eventsSink)); }
                    catch (Exception ex) { _backend._logger?.Warn($"Failed to unregister audio session notifications: {ex.Message}"); }
                    _eventsRegistered = false;
                }
                // _control2 and _volume are aliases of the one transferred GetSession reference.
                ReleaseCom(_control);
            }
        }
    }

    private sealed class AudioSessionEventsSink : IAudioSessionEvents
    {
        private readonly TrackedSession _session;

        public AudioSessionEventsSink(TrackedSession session)
        {
            _session = session;
        }

        public int OnDisplayNameChanged(string newDisplayName, Guid eventContext) => 0;
        public int OnIconPathChanged(string newIconPath, Guid eventContext) => 0;

        public int OnSimpleVolumeChanged(float newVolume, bool newMute, Guid eventContext)
        {
            _session.OnVolumeOrStateChanged();
            return 0;
        }

        public int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, Guid eventContext) => 0;
        public int OnGroupingParamChanged(Guid newGroupingParam, Guid eventContext) => 0;

        public int OnStateChanged(AudioSessionState newState)
        {
            _session.OnVolumeOrStateChanged();
            return 0;
        }

        public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            _session.OnVolumeOrStateChanged();
            return 0;
        }
    }

    private sealed class SessionNotificationSink : IAudioSessionNotification
    {
        private readonly WindowsCoreAudioBackend _backend;
        private readonly long _generation;

        public SessionNotificationSink(WindowsCoreAudioBackend backend, long generation)
        {
            _backend = backend;
            _generation = generation;
        }

        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            _backend.NotifySessionsChanged(_generation);
            return 0;
        }
    }

    private sealed class NotificationClientSink : IMMNotificationClient
    {
        private readonly WindowsCoreAudioBackend _backend;

        public NotificationClientSink(WindowsCoreAudioBackend backend)
        {
            _backend = backend;
        }

        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;
        public int OnDeviceAdded(string pwstrDeviceId) => 0;
        public int OnDeviceRemoved(string pwstrDeviceId) => 0;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
        {
            if (flow == EDataFlow.eRender && (role == ERole.eMultimedia || role == ERole.eConsole))
            {
                _backend.OnDefaultEndpointChanged(pwstrDefaultDeviceId);
            }
            return 0;
        }

        public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => 0;
    }
}
