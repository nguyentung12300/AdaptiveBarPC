using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.ViewModels;

public sealed class MediaItemViewModel : BarItemViewModel, INotifyPropertyChanged, IDisposable
{
    public const double MinimumWidth = 360;
    private readonly MediaSessionService? _mediaSessionService;
    private readonly Dispatcher? _dispatcher;
    private readonly DispatcherTimer? _progressTimer;

    private readonly DelegateCommand _playPauseCommand;
    private readonly DelegateCommand _previousCommand;
    private readonly DelegateCommand _nextCommand;

    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _mediaKey = string.Empty;
    private ImageSource? _artwork;
    private bool _isAvailable;
    private bool _isPlaying;
    private bool _canPrevious;
    private bool _canPlayPause;
    private bool _canNext;

    private TimeSpan _basePosition = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private DateTimeOffset _baseTimestamp = DateTimeOffset.MinValue;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private bool _isSeeking;

    private bool _disposed;

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        private set => SetField(ref _artist, value);
    }

    public string MediaKey
    {
        get => _mediaKey;
        private set => SetField(ref _mediaKey, value);
    }

    public ImageSource? Artwork
    {
        get => _artwork;
        private set
        {
            if (SetField(ref _artwork, value))
                OnPropertyChanged(nameof(HasArtwork));
        }
    }

    public bool HasArtwork => _artwork is not null;

    private bool _collapseWhenUnavailable;

    public bool CollapseWhenUnavailable
    {
        get => _collapseWhenUnavailable;
        set
        {
            if (SetField(ref _collapseWhenUnavailable, value))
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    public Visibility Visibility => (!_isAvailable && _collapseWhenUnavailable)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetField(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetField(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseGlyph));
                OnPropertyChanged(nameof(PlayPauseLabel));
            }
        }
    }

    public TimeSpan Position
    {
        get => _currentPosition;
        private set
        {
            if (SetField(ref _currentPosition, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(PositionText));
            }
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            if (SetField(ref _duration, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }

    public double ProgressPercent
    {
        get
        {
            if (_duration <= TimeSpan.Zero)
                return 0.0;
            var ratio = _currentPosition.TotalMilliseconds / _duration.TotalMilliseconds;
            return Math.Clamp(ratio * 100.0, 0.0, 100.0);
        }
        set
        {
            if (_duration <= TimeSpan.Zero)
                return;

            var ratio = Math.Clamp(value, 0.0, 100.0) / 100.0;
            var position = TimeSpan.FromTicks((long)Math.Round(_duration.Ticks * ratio));
            if (position == Position) return;
            Position = position;
            if (!_isSeeking) CommitSeek();
        }
    }

    public bool IsSeeking
    {
        get => _isSeeking;
        set
        {
            if (!SetField(ref _isSeeking, value))
                return;

            if (!value) CommitSeek();

            EvaluateTimer();
        }
    }

    private void CommitSeek()
    {
        if (!_isAvailable || _duration <= TimeSpan.Zero) return;
        _basePosition = Position;
        _baseTimestamp = DateTimeOffset.UtcNow;
        if (_mediaSessionService is not null)
            _ = ExecuteSafeAsync(() => _mediaSessionService.SeekAsync(Position));
    }

    public bool CanPrevious
    {
        get => _canPrevious;
        private set
        {
            if (SetField(ref _canPrevious, value))
                _previousCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanPlayPause
    {
        get => _canPlayPause;
        private set
        {
            if (SetField(ref _canPlayPause, value))
                _playPauseCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanNext
    {
        get => _canNext;
        private set
        {
            if (SetField(ref _canNext, value))
                _nextCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand PreviousCommand => _previousCommand;
    public ICommand PlayPauseCommand => _playPauseCommand;
    public ICommand NextCommand => _nextCommand;

    public string PositionText => FormatTimeSpan(_currentPosition);
    public string DurationText => FormatTimeSpan(_duration);
    public string PlayPauseGlyph => _isPlaying ? "\uE769" : "\uE768";
    public string PlayPauseLabel => _isPlaying ? "Pause playback" : "Play media";

    public static MediaItemViewModel CreatePreview(
        double width = 360,
        string title = "Example Track",
        string artist = "Example Artist",
        TimeSpan? position = null,
        TimeSpan? duration = null,
        bool isPlaying = true,
        ImageSource? artwork = null)
    {
        var pos = position ?? TimeSpan.FromMinutes(1.5);
        var dur = duration ?? TimeSpan.FromMinutes(3.5);
        return new MediaItemViewModel(width, title, artist, pos, dur, isPlaying, artwork ?? CreatePreviewArtwork());
    }

    private static ImageSource CreatePreviewArtwork()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            System.Windows.Media.Brushes.DimGray,
            null,
            new RectangleGeometry(new Rect(0, 0, 36, 36), 4, 4)));
        drawing.Children.Add(new GeometryDrawing(
            System.Windows.Media.Brushes.Gray,
            null,
            new EllipseGeometry(new System.Windows.Point(18, 18), 10, 10)));
        drawing.Children.Add(new GeometryDrawing(
            System.Windows.Media.Brushes.Black,
            null,
            new EllipseGeometry(new System.Windows.Point(18, 18), 3, 3)));
        drawing.Freeze();

        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private MediaItemViewModel(
        double width,
        string title,
        string artist,
        TimeSpan position,
        TimeSpan duration,
        bool isPlaying,
        ImageSource? artwork)
    {
        Width = width > 0 ? width : 360;
        _collapseWhenUnavailable = false;
        _mediaSessionService = null;
        _dispatcher = null;
        _progressTimer = null;

        _title = NormalizeTitle(title);
        _artist = NormalizeArtist(artist);
        _mediaKey = "designer-preview";
        _artwork = artwork;
        _isAvailable = true;
        _isPlaying = isPlaying;
        _canPrevious = true;
        _canPlayPause = true;
        _canNext = true;
        _duration = duration;
        _currentPosition = position;

        _playPauseCommand = new DelegateCommand(() => { }, () => true);
        _previousCommand = new DelegateCommand(() => { }, () => true);
        _nextCommand = new DelegateCommand(() => { }, () => true);
    }

    public MediaItemViewModel(
        MediaSessionService mediaSessionService,
        double width = 520,
        bool collapseWhenUnavailable = false,
        Dispatcher? dispatcher = null)
    {
        _mediaSessionService = mediaSessionService ?? throw new ArgumentNullException(nameof(mediaSessionService));
        Width = width;
        _collapseWhenUnavailable = collapseWhenUnavailable;

        _dispatcher = dispatcher
            ?? System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        _playPauseCommand = new DelegateCommand(
            execute: () => _ = ExecuteSafeAsync(mediaSessionService.PlayPauseAsync),
            canExecute: () => CanPlayPause);

        _previousCommand = new DelegateCommand(
            execute: () => _ = ExecuteSafeAsync(mediaSessionService.PreviousAsync),
            canExecute: () => CanPrevious);

        _nextCommand = new DelegateCommand(
            execute: () => _ = ExecuteSafeAsync(mediaSessionService.NextAsync),
            canExecute: () => CanNext);

        _progressTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher!)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _progressTimer.Tick += OnProgressTimerTick;

        mediaSessionService.StateChanged += OnMediaSessionStateChanged;
        mediaSessionService.ArtworkChanged += OnMediaArtworkChanged;

        ApplyState(mediaSessionService.CurrentState);
        ApplyArtworkState(mediaSessionService.CurrentArtworkState);
    }

    private void OnMediaSessionStateChanged(object? sender, MediaSessionState state)
    {
        Dispatch(() => ApplyState(state));
    }

    private void OnMediaArtworkChanged(object? sender, MediaArtworkState state)
    {
        Dispatch(() => ApplyArtworkState(state));
    }

    private void Dispatch(Action action)
    {
        if (_disposed || _dispatcher == null) return;

        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_disposed)
                    action();
            }));
    }

    private void ApplyState(MediaSessionState state)
    {
        if (_disposed) return;

        if (!string.Equals(MediaKey, state.MediaKey, StringComparison.Ordinal))
        {
            MediaKey = state.MediaKey;
            Artwork = null;
        }

        IsAvailable = state.IsAvailable;
        Title = NormalizeTitle(state.Title);
        Artist = NormalizeArtist(state.Artist);
        IsPlaying = state.IsPlaying;

        CanPrevious = state.IsAvailable && state.CanSkipPrevious;
        CanNext = state.IsAvailable && state.CanSkipNext;
        CanPlayPause = state.IsAvailable && (state.CanPlay || state.CanPause || state.IsPlaying);

        Duration = state.Duration > TimeSpan.Zero ? state.Duration : TimeSpan.Zero;
        _basePosition = state.Position;
        _baseTimestamp = DateTimeOffset.UtcNow;

        UpdatePosition();
        EvaluateTimer();
    }

    private void ApplyArtworkState(MediaArtworkState state)
    {
        if (_disposed)
            return;

        if (state.Artwork is null && state.MediaKey.Length == 0)
        {
            Artwork = null;
            return;
        }

        if (!string.Equals(state.MediaKey, MediaKey, StringComparison.Ordinal))
            return;

        Artwork = state.Artwork;
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || _progressTimer == null)
        {
            _progressTimer?.Stop();
            return;
        }

        UpdatePosition();

        if (_currentPosition >= _duration)
        {
            _progressTimer.Stop();
        }
    }

    private void UpdatePosition()
    {
        if (_isSeeking)
            return;

        if (!_isAvailable || _duration <= TimeSpan.Zero)
        {
            Position = TimeSpan.Zero;
            return;
        }

        if (!_isPlaying)
        {
            Position = Clamp(_basePosition, TimeSpan.Zero, _duration);
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _baseTimestamp;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var extrapolated = _basePosition + elapsed;
        Position = Clamp(extrapolated, TimeSpan.Zero, _duration);
    }

    private void EvaluateTimer()
    {
        if (_disposed || _progressTimer == null)
            return;

        bool shouldRun = !_isSeeking && _isAvailable && _isPlaying && _duration > TimeSpan.Zero;
        if (shouldRun && !_progressTimer.IsEnabled)
        {
            _progressTimer.Start();
        }
        else if (!shouldRun && _progressTimer.IsEnabled)
        {
            _progressTimer.Stop();
        }
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    private static string NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Unknown track" : title;

    private static string NormalizeArtist(string? artist) =>
        string.IsNullOrWhiteSpace(artist) ? "Unknown artist" : artist;

    private static async Task ExecuteSafeAsync(Func<Task<bool>> command)
    {
        try
        {
            await command();
        }
        catch
        {
            // Failures are contained and logged at service boundary.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_mediaSessionService != null)
        {
            _mediaSessionService.StateChanged -= OnMediaSessionStateChanged;
            _mediaSessionService.ArtworkChanged -= OnMediaArtworkChanged;
        }

        if (_progressTimer != null)
        {
            _progressTimer.Stop();
            _progressTimer.Tick -= OnProgressTimerTick;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
