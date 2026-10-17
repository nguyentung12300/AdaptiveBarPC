using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public sealed record MediaArtworkState(string MediaKey, ImageSource? Artwork);

internal sealed class MediaArtworkCache(int capacity)
{
    private readonly int _capacity = Math.Max(1, capacity);
    private readonly Dictionary<string, ImageSource> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public int Count => _entries.Count;

    public bool TryGet(string key, out ImageSource? artwork)
    {
        if (_entries.TryGetValue(key, out var value))
        {
            artwork = value;
            return true;
        }

        artwork = null;
        return false;
    }

    public void Set(string key, ImageSource artwork)
    {
        if (_entries.ContainsKey(key))
        {
            _entries[key] = artwork;
            return;
        }

        while (_entries.Count >= _capacity && _order.Count > 0)
            _entries.Remove(_order.Dequeue());

        _entries[key] = artwork;
        _order.Enqueue(key);
    }

    public void Clear()
    {
        _entries.Clear();
        _order.Clear();
    }
}

public sealed class MediaSessionService : IDisposable
{
    internal const int ArtworkDecodePixelWidth = 96;
    internal const int ArtworkCacheCapacity = 8;
    internal const int ArtworkSettleReadCount = 2;
    internal static readonly TimeSpan ArtworkSettleDelay = TimeSpan.FromMilliseconds(80);
    private const ulong MaxArtworkBytes = 8 * 1024 * 1024;

    private readonly AppLogger _logger;
    private readonly object _lock = new();
    private readonly MediaArtworkCache _artworkCache = new(ArtworkCacheCapacity);

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private MediaSessionState _currentState = MediaSessionState.Empty;
    private DateTimeOffset _currentStateTimestamp = DateTimeOffset.UtcNow;
    private ImageSource? _currentArtwork;
    private string _currentArtworkKey = string.Empty;
    private CancellationTokenSource? _artworkLoadCts;
    private long _artworkGeneration;
    private string _sourceAppId = string.Empty;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _albumTitle = string.Empty;
    private string _mediaKey = string.Empty;

    private bool _isInitialized;
    private bool _disposed;
    private long _stateRevision;
    private long _appliedRevision;

    public event EventHandler<MediaSessionState>? StateChanged;
    public event EventHandler<MediaArtworkState>? ArtworkChanged;

    public MediaSessionService(AppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MediaSessionState CurrentState
    {
        get
        {
            lock (_lock)
            {
                var state = _currentState;
                if (!state.IsPlaying || state.Duration <= TimeSpan.Zero)
                    return state;

                var position = state.Position + (DateTimeOffset.UtcNow - _currentStateTimestamp);
                return state with { Position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, state.Duration.Ticks)) };
            }
        }
    }

    public MediaArtworkState CurrentArtworkState
    {
        get
        {
            lock (_lock)
                return new MediaArtworkState(_currentArtworkKey, _currentArtwork);
        }
    }

    public async Task InitializeAsync()
    {
        lock (_lock)
        {
            if (_disposed || _isInitialized)
                return;
        }

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            lock (_lock)
            {
                if (_disposed)
                    return;

                _sessionManager = manager;
                if (_sessionManager is not null)
                {
                    _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                }
                _isInitialized = true;
            }

            if (_sessionManager is not null)
            {
                await RefreshSessionAndStateAsync();
            }
            else
            {
                _logger.Info("GlobalSystemMediaTransportControlsSessionManager returned null on this system.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize MediaSessionService.", ex);
        }
    }

    public async Task<bool> PlayPauseAsync()
    {
        if (_disposed) return false;

        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            if (_disposed) return false;
            session = _currentSession;
        }

        if (session is null)
            return false;

        try
        {
            return await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to execute PlayPauseAsync command.", ex);
            return false;
        }
    }

    public async Task<bool> NextAsync()
    {
        if (_disposed) return false;

        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            if (_disposed) return false;
            session = _currentSession;
        }

        if (session is null)
            return false;

        try
        {
            return await session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to execute NextAsync command.", ex);
            return false;
        }
    }

    public async Task<bool> PreviousAsync()
    {
        if (_disposed) return false;

        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            if (_disposed) return false;
            session = _currentSession;
        }

        if (session is null)
            return false;

        try
        {
            return await session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to execute PreviousAsync command.", ex);
            return false;
        }
    }

    public async Task<bool> SeekAsync(TimeSpan position)
    {
        if (_disposed) return false;

        GlobalSystemMediaTransportControlsSession? session;
        TimeSpan duration;
        lock (_lock)
        {
            if (_disposed) return false;
            session = _currentSession;
            duration = _currentState.Duration;
        }

        if (session is null)
            return false;

        var ticks = Math.Max(0, position.Ticks);
        if (duration > TimeSpan.Zero)
            ticks = Math.Min(ticks, duration.Ticks);

        try
        {
            return await session.TryChangePlaybackPositionAsync(ticks);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to execute SeekAsync command.", ex);
            return false;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (_disposed) return;
        _ = RefreshSessionAndStateAsync();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        if (_disposed) return;
        _ = UpdateStateFromSessionAsync(sender, refreshMetadata: true);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        if (_disposed) return;
        _ = UpdateStateFromSessionAsync(sender, refreshMetadata: false);
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        if (_disposed) return;
        _ = UpdateStateFromSessionAsync(sender, refreshMetadata: false);
    }

    private async Task RefreshSessionAndStateAsync()
    {
        if (_disposed) return;

        GlobalSystemMediaTransportControlsSession? session = null;
        bool sessionChanged = false;
        try
        {
            lock (_lock)
            {
                if (_disposed || _sessionManager is null) return;

                session = _sessionManager.GetCurrentSession();
                if (!ReferenceEquals(session, _currentSession))
                {
                    DetachCurrentSessionLocked();
                    sessionChanged = true;
                    if (session is not null)
                    {
                        AttachSessionLocked(session);
                    }
                }
                else
                {
                    session = _currentSession;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get current media session.", ex);
        }

        if (sessionChanged)
            PublishSessionArtworkResetIfPending();

        if (session is null)
        {
            long revision = Interlocked.Increment(ref _stateRevision);
            PublishState(MediaSessionState.Empty, revision);
            return;
        }

        await UpdateStateFromSessionAsync(session, refreshMetadata: true);
    }

    private async Task UpdateStateFromSessionAsync(
        GlobalSystemMediaTransportControlsSession session,
        bool refreshMetadata)
    {
        if (_disposed) return;

        long revision = Interlocked.Increment(ref _stateRevision);

        try
        {
            GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProperties = null;
            if (refreshMetadata)
            {
                try
                {
                    mediaProperties = await session.TryGetMediaPropertiesAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to get media properties from session.", ex);
                }
            }

            if (_disposed) return;

            string sourceAppId;
            string title;
            string artist;
            string albumTitle;
            string mediaKey;
            bool artworkCacheable = false;
            bool metadataChanged = false;
            long artworkGeneration = 0;

            lock (_lock)
            {
                if (_disposed || !ReferenceEquals(session, _currentSession))
                    return;

                if (refreshMetadata)
                {
                    sourceAppId = GetSourceAppId(session);
                    title = mediaProperties?.Title ?? string.Empty;
                    artist = mediaProperties?.Artist ?? string.Empty;
                    albumTitle = mediaProperties?.AlbumTitle ?? string.Empty;
                    string albumArtist = mediaProperties?.AlbumArtist ?? string.Empty;
                    string subtitle = mediaProperties?.Subtitle ?? string.Empty;
                    int trackNumber = mediaProperties?.TrackNumber ?? 0;
                    mediaKey = BuildMediaKey(sourceAppId, title, artist, albumTitle, albumArtist, subtitle, trackNumber);
                    artworkCacheable = IsArtworkCacheIdentityStrong(
                        sourceAppId,
                        title,
                        artist,
                        albumTitle,
                        albumArtist,
                        subtitle,
                        trackNumber);
                    metadataChanged = ArtworkNeedsRefresh(_mediaKey, mediaKey);

                    if (metadataChanged)
                    {
                        CancelArtworkLoadLocked();
                        artworkGeneration = ++_artworkGeneration;
                    }

                    _sourceAppId = sourceAppId;
                    _title = title;
                    _artist = artist;
                    _albumTitle = albumTitle;
                    _mediaKey = mediaKey;
                    if (metadataChanged)
                    {
                        _currentArtwork = null;
                        _currentArtworkKey = mediaKey;
                    }
                }
                else
                {
                    sourceAppId = _sourceAppId;
                    title = _title;
                    artist = _artist;
                    albumTitle = _albumTitle;
                    mediaKey = _mediaKey;
                }
            }

            bool isPlaying = false;
            bool canPlay = false;
            bool canPause = false;
            bool canSkipNext = false;
            bool canSkipPrevious = false;

            try
            {
                var playbackInfo = session.GetPlaybackInfo();
                if (playbackInfo is not null)
                {
                    isPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    var controls = playbackInfo.Controls;
                    if (controls is not null)
                    {
                        canPlay = controls.IsPlayEnabled;
                        canPause = controls.IsPauseEnabled;
                        canSkipNext = controls.IsNextEnabled;
                        canSkipPrevious = controls.IsPreviousEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get playback info from session.", ex);
            }

            TimeSpan position = TimeSpan.Zero;
            TimeSpan duration = TimeSpan.Zero;

            try
            {
                var timeline = session.GetTimelineProperties();
                if (timeline is not null)
                {
                    position = timeline.Position;
                    if (timeline.EndTime > timeline.StartTime)
                    {
                        duration = timeline.EndTime - timeline.StartTime;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get timeline properties from session.", ex);
            }

            var newState = new MediaSessionState(
                IsAvailable: true,
                SourceAppId: sourceAppId,
                Title: title,
                Artist: artist,
                IsPlaying: isPlaying,
                CanPlay: canPlay,
                CanPause: canPause,
                CanSkipNext: canSkipNext,
                CanSkipPrevious: canSkipPrevious,
                Position: position,
                Duration: duration,
                AlbumTitle: albumTitle,
                MediaKey: mediaKey);

            PublishState(newState, revision);

            if (refreshMetadata && metadataChanged)
            {
                PublishArtworkResetIfCurrent(session, mediaKey, artworkGeneration);
                await RefreshArtworkAsync(
                    session,
                    mediaKey,
                    artworkGeneration,
                    artworkCacheable);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Unexpected error while updating media session state.", ex);
            long fallbackRevision = Interlocked.Increment(ref _stateRevision);
            PublishState(MediaSessionState.Empty, fallbackRevision);
        }
    }

    private async Task RefreshArtworkAsync(
        GlobalSystemMediaTransportControlsSession session,
        string mediaKey,
        long generation,
        bool cacheable)
    {
        ImageSource? cachedArtwork = null;
        CancellationToken token = default;
        bool useCachedArtwork = false;

        lock (_lock)
        {
            if (_disposed || !IsArtworkResultCurrent(generation, _artworkGeneration, mediaKey, _mediaKey) ||
                !ReferenceEquals(session, _currentSession))
            {
                return;
            }

            if (cacheable && _artworkCache.TryGet(mediaKey, out cachedArtwork))
            {
                useCachedArtwork = true;
            }
            else
            {
                _artworkLoadCts = new CancellationTokenSource();
                token = _artworkLoadCts.Token;
            }
        }

        if (useCachedArtwork)
        {
            PublishArtworkIfCurrent(session, mediaKey, generation, cachedArtwork, cacheResult: false);
            return;
        }

        GlobalSystemMediaTransportControlsSessionMediaProperties? settledProperties;
        try
        {
            settledProperties = await GetSettledMediaPropertiesAsync(session, mediaKey, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (settledProperties is null)
            return;

        ImageSource? artwork;
        try
        {
            artwork = await LoadArtworkAsync(settledProperties.Thumbnail, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        PublishArtworkIfCurrent(
            session,
            mediaKey,
            generation,
            artwork,
            cacheResult: cacheable && artwork is not null);
    }

    private async Task<GlobalSystemMediaTransportControlsSessionMediaProperties?> GetSettledMediaPropertiesAsync(
        GlobalSystemMediaTransportControlsSession session,
        string expectedMediaKey,
        CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSessionMediaProperties? latest = null;

        for (int attempt = 0; attempt < ArtworkSettleReadCount; attempt++)
        {
            await Task.Delay(ArtworkSettleDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var operation = session.TryGetMediaPropertiesAsync();
                using var registration = cancellationToken.Register(operation.Cancel);
                latest = await operation;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to re-read media properties for artwork.", ex);
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (latest is null)
                return null;

            var candidateKey = BuildMediaKey(
                GetSourceAppId(session),
                latest.Title ?? string.Empty,
                latest.Artist ?? string.Empty,
                latest.AlbumTitle ?? string.Empty,
                latest.AlbumArtist ?? string.Empty,
                latest.Subtitle ?? string.Empty,
                latest.TrackNumber);

            if (!string.Equals(candidateKey, expectedMediaKey, StringComparison.Ordinal))
                return null;
        }

        return latest;
    }

    private void PublishArtworkIfCurrent(
        GlobalSystemMediaTransportControlsSession session,
        string mediaKey,
        long generation,
        ImageSource? artwork,
        bool cacheResult)
    {
        EventHandler<MediaArtworkState>? handler;
        MediaArtworkState? state;

        lock (_lock)
        {
            if (_disposed || !IsArtworkResultCurrent(generation, _artworkGeneration, mediaKey, _mediaKey) ||
                !ReferenceEquals(session, _currentSession))
            {
                return;
            }

            if (cacheResult && artwork is not null)
                _artworkCache.Set(mediaKey, artwork);

            if (string.Equals(_currentArtworkKey, mediaKey, StringComparison.Ordinal) &&
                ReferenceEquals(_currentArtwork, artwork))
            {
                return;
            }

            _currentArtworkKey = mediaKey;
            _currentArtwork = artwork;
            state = new MediaArtworkState(mediaKey, artwork);
            handler = ArtworkChanged;
        }

        InvokeArtworkChanged(handler, state);
    }

    private void PublishArtworkResetIfCurrent(
        GlobalSystemMediaTransportControlsSession session,
        string mediaKey,
        long generation)
    {
        EventHandler<MediaArtworkState>? handler;

        lock (_lock)
        {
            if (_disposed || !IsArtworkResultCurrent(generation, _artworkGeneration, mediaKey, _mediaKey) ||
                !ReferenceEquals(session, _currentSession))
            {
                return;
            }

            _currentArtworkKey = mediaKey;
            _currentArtwork = null;
            handler = ArtworkChanged;
        }

        InvokeArtworkChanged(handler, new MediaArtworkState(mediaKey, null));
    }

    private void PublishSessionArtworkResetIfPending()
    {
        EventHandler<MediaArtworkState>? handler;

        lock (_lock)
        {
            if (_disposed || _mediaKey.Length != 0 || _currentArtworkKey.Length != 0)
                return;

            handler = ArtworkChanged;
        }

        InvokeArtworkChanged(handler, new MediaArtworkState(string.Empty, null));
    }

    private void InvokeArtworkChanged(EventHandler<MediaArtworkState>? handler, MediaArtworkState state)
    {
        try
        {
            handler?.Invoke(this, state);
        }
        catch (Exception ex)
        {
            _logger.Error("Error invoking ArtworkChanged event handler.", ex);
        }
    }

    private void CancelArtworkLoadLocked()
    {
        if (_artworkLoadCts is null)
            return;

        try
        {
            _artworkLoadCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _artworkLoadCts.Dispose();
        _artworkLoadCts = null;
    }

    private string GetSourceAppId(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.SourceAppUserModelId ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get SourceAppUserModelId from session.", ex);
            return string.Empty;
        }
    }

    internal static string BuildMediaKey(
        string sourceAppId,
        string title,
        string artist,
        string albumTitle,
        string albumArtist = "",
        string subtitle = "",
        int trackNumber = 0) =>
        string.Join('\u001f', sourceAppId, title, artist, albumTitle, albumArtist, subtitle, trackNumber);

    internal static bool IsArtworkCacheIdentityStrong(
        string sourceAppId,
        string title,
        string artist,
        string albumTitle,
        string albumArtist = "",
        string subtitle = "",
        int trackNumber = 0) =>
        !string.IsNullOrWhiteSpace(sourceAppId) &&
        !string.IsNullOrWhiteSpace(title) &&
        (!string.IsNullOrWhiteSpace(artist) ||
         !string.IsNullOrWhiteSpace(albumTitle) ||
         !string.IsNullOrWhiteSpace(albumArtist) ||
         !string.IsNullOrWhiteSpace(subtitle) ||
         trackNumber > 0);

    internal static bool ArtworkNeedsRefresh(string currentMediaKey, string nextMediaKey) =>
        !string.Equals(currentMediaKey, nextMediaKey, StringComparison.Ordinal);

    internal static bool IsArtworkResultCurrent(
        long generation,
        long currentGeneration,
        string mediaKey,
        string currentMediaKey) =>
        generation == currentGeneration && string.Equals(mediaKey, currentMediaKey, StringComparison.Ordinal);

    internal static async Task<ImageSource?> LoadArtworkAsync(
        IRandomAccessStreamReference? thumbnail,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null)
            return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var openOperation = thumbnail.OpenReadAsync();
            using var openCancellation = cancellationToken.Register(openOperation.Cancel);
            using var stream = await openOperation;

            if (stream.Size == 0 || stream.Size > MaxArtworkBytes || stream.Size > uint.MaxValue)
                return null;

            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            var loadOperation = reader.LoadAsync((uint)stream.Size);
            using var loadCancellation = cancellationToken.Register(loadOperation.Cancel);
            var loaded = await loadOperation;
            cancellationToken.ThrowIfCancellationRequested();

            if (loaded == 0)
                return null;

            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            return await Task.Run(() => DecodeArtwork(bytes), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static ImageSource? DecodeArtwork(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ArtworkDecodePixelWidth;
            image.StreamSource = stream;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private void PublishState(MediaSessionState state, long revision)
    {
        EventHandler<MediaSessionState>? handler;
        lock (_lock)
        {
            if (_disposed || revision < _appliedRevision)
                return;

            _appliedRevision = revision;
            if (_currentState == state)
                return;

            _currentState = state;
            _currentStateTimestamp = DateTimeOffset.UtcNow;
            handler = StateChanged;
        }

        try
        {
            handler?.Invoke(this, state);
        }
        catch (Exception ex)
        {
            _logger.Error("Error invoking StateChanged event handler.", ex);
        }
    }

    private void AttachSessionLocked(GlobalSystemMediaTransportControlsSession session)
    {
        _currentSession = session;
        try
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
        catch (Exception ex)
        {
            _logger.Error("Error subscribing to session events.", ex);
        }
    }

    private void DetachCurrentSessionLocked()
    {
        if (_currentSession is not null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            catch (Exception ex)
            {
                _logger.Error("Error unsubscribing from session events.", ex);
            }
            _currentSession = null;
        }

        CancelArtworkLoadLocked();
        _artworkGeneration++;
        _currentArtwork = null;
        _currentArtworkKey = string.Empty;
        _sourceAppId = string.Empty;
        _title = string.Empty;
        _artist = string.Empty;
        _albumTitle = string.Empty;
        _mediaKey = string.Empty;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_sessionManager is not null)
            {
                try
                {
                    _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                }
                catch (Exception ex)
                {
                    _logger.Error("Error unsubscribing from CurrentSessionChanged.", ex);
                }
                _sessionManager = null;
            }

            DetachCurrentSessionLocked();
            _currentState = MediaSessionState.Empty;
            _artworkCache.Clear();
        }
    }
}
