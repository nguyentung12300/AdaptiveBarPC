using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Controls;

public sealed class DecorativeMediaView : Grid
{
    private int _loadGeneration;
    internal Task MediaLoadTask { get; private set; } = Task.CompletedTask;
    private CancellationTokenSource? _gifLoadCancellation;
    private ObjectAnimationUsingKeyFrames? _gifAnimation;
    internal AnimationClock? GifClock { get; private set; }
    private readonly Canvas _mediaLayer = new()
    {
        ClipToBounds = false,
        Focusable = false,
        IsHitTestVisible = false
    };
    private readonly TranslateTransform _imageOffset = new();
    private readonly TranslateTransform _videoOffset = new();
    private double _imageNaturalWidth;
    private double _imageNaturalHeight;
    private double _videoNaturalWidth;
    private double _videoNaturalHeight;
    private readonly System.Windows.Controls.Image _image = new()
    {
        Focusable = false,
        IsHitTestVisible = false
    };

    private readonly MediaElement _video = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Manual,
        IsMuted = true,
        Volume = 0,
        Focusable = false,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };

    private readonly TextBlock _diagnostic = new()
    {
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(6),
        Opacity = 0.7,
        Visibility = Visibility.Collapsed,
        Focusable = false,
        IsHitTestVisible = false
    };

    public DecorativeMediaView()
    {
        ClipToBounds = true;
        Focusable = false;
        IsHitTestVisible = false;
        _image.RenderTransform = _imageOffset;
        _video.RenderTransform = _videoOffset;
        _mediaLayer.Children.Add(_image);
        _mediaLayer.Children.Add(_video);
        Children.Add(_mediaLayer);
        Children.Add(_diagnostic);
        _video.MediaOpened += (_, _) =>
        {
            _videoNaturalWidth = _video.NaturalVideoWidth;
            _videoNaturalHeight = _video.NaturalVideoHeight;
            ApplyVisuals();
            UpdatePlaybackState();
        };
        _video.MediaEnded += OnVideoEnded;
        _video.MediaFailed += OnVideoFailed;
        Loaded += (_, _) => RefreshMedia();
        Unloaded += (_, _) => StopMedia();
        IsVisibleChanged += (_, _) => UpdatePlaybackState();
        SizeChanged += (_, _) => ApplyVisuals();
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(DecorativeMediaView),
        new PropertyMetadata(null, OnMediaChanged));

    public static readonly DependencyProperty FitProperty = DependencyProperty.Register(
        nameof(Fit), typeof(string), typeof(DecorativeMediaView),
        new PropertyMetadata("contain", OnVisualChanged));

    public static readonly DependencyProperty LoopProperty = DependencyProperty.Register(
        nameof(Loop), typeof(bool), typeof(DecorativeMediaView),
        new PropertyMetadata(true, OnPlaybackChanged));

    public static readonly DependencyProperty MediaOpacityProperty = DependencyProperty.Register(
        nameof(MediaOpacity), typeof(double), typeof(DecorativeMediaView),
        new PropertyMetadata(1d, OnVisualChanged));

    public static readonly DependencyProperty MediaEnabledProperty = DependencyProperty.Register(
        nameof(MediaEnabled), typeof(bool), typeof(DecorativeMediaView),
        new PropertyMetadata(true, OnMediaChanged));

    public static readonly DependencyProperty ShowDiagnosticsProperty = DependencyProperty.Register(
        nameof(ShowDiagnostics), typeof(bool), typeof(DecorativeMediaView),
        new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset), typeof(double), typeof(DecorativeMediaView),
        new PropertyMetadata(0d, OnVisualChanged));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(DecorativeMediaView),
        new PropertyMetadata(0d, OnVisualChanged));

    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(
        nameof(MotionEnabled), typeof(bool), typeof(DecorativeMediaView),
        new PropertyMetadata(true, OnPlaybackChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(DecorativeMediaView),
        new PropertyMetadata(1d, OnVisualChanged));

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string Fit
    {
        get => (string)GetValue(FitProperty);
        set => SetValue(FitProperty, value);
    }

    public bool Loop
    {
        get => (bool)GetValue(LoopProperty);
        set => SetValue(LoopProperty, value);
    }

    public double MediaOpacity
    {
        get => (double)GetValue(MediaOpacityProperty);
        set => SetValue(MediaOpacityProperty, value);
    }

    public bool MediaEnabled
    {
        get => (bool)GetValue(MediaEnabledProperty);
        set => SetValue(MediaEnabledProperty, value);
    }

    public bool ShowDiagnostics
    {
        get => (bool)GetValue(ShowDiagnosticsProperty);
        set => SetValue(ShowDiagnosticsProperty, value);
    }

    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public bool MotionEnabled
    {
        get => (bool)GetValue(MotionEnabledProperty);
        set => SetValue(MotionEnabledProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    internal static Stretch ResolveStretch(string? fit) =>
        fit?.Trim().ToLowerInvariant() switch
        {
            "cover" => Stretch.UniformToFill,
            "fill" => Stretch.Fill,
            _ => Stretch.Uniform
        };

    internal static System.Windows.Size ResolveMediaSize(
        double viewportWidth,
        double viewportHeight,
        double naturalWidth,
        double naturalHeight,
        string? fit,
        double zoom)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || naturalWidth <= 0 || naturalHeight <= 0)
            return System.Windows.Size.Empty;

        zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 0.1d, 4d) : 1d;
        if (fit?.Trim().Equals("fill", StringComparison.OrdinalIgnoreCase) == true)
            return new System.Windows.Size(viewportWidth * zoom, viewportHeight * zoom);

        var scaleX = viewportWidth / naturalWidth;
        var scaleY = viewportHeight / naturalHeight;
        var scale = fit?.Trim().Equals("cover", StringComparison.OrdinalIgnoreCase) == true
            ? Math.Max(1d, Math.Max(scaleX, scaleY))
            : Math.Min(scaleX, scaleY);
        return new System.Windows.Size(naturalWidth * scale * zoom, naturalHeight * scale * zoom);
    }

    internal enum MediaKind
    {
        Unsupported,
        StaticImage,
        Gif,
        Video
    }

    internal static MediaKind ClassifySource(string? source)
    {
        var extension = Path.GetExtension(source ?? "").ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg") return MediaKind.StaticImage;
        if (extension == ".gif") return MediaKind.Gif;
        if (extension == ".mp4") return MediaKind.Video;
        return MediaKind.Unsupported;
    }

    private static void OnMediaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DecorativeMediaView view && view.IsLoaded)
            view.RefreshMedia();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DecorativeMediaView view)
            view.ApplyVisuals();
    }

    private static void OnPlaybackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DecorativeMediaView view)
        {
            if (e.Property == LoopProperty)
                view.StartGifAnimation();
            view.UpdatePlaybackState();
        }
    }

    private void ApplyVisuals()
    {
        var opacity = double.IsFinite(MediaOpacity) ? Math.Clamp(MediaOpacity, 0d, 1d) : 1d;
        var viewportWidth = ActualWidth > 0 ? ActualWidth : double.IsFinite(Width) ? Width : 0d;
        var viewportHeight = ActualHeight > 0 ? ActualHeight : double.IsFinite(Height) ? Height : 0d;
        var imageSize = ResolveMediaSize(viewportWidth, viewportHeight, _imageNaturalWidth, _imageNaturalHeight, Fit, Zoom);
        var videoSize = ResolveMediaSize(viewportWidth, viewportHeight, _videoNaturalWidth, _videoNaturalHeight, Fit, Zoom);

        _image.Stretch = Stretch.Fill;
        _image.Width = imageSize.IsEmpty ? double.NaN : imageSize.Width;
        _image.Height = imageSize.IsEmpty ? double.NaN : imageSize.Height;
        Canvas.SetLeft(_image, imageSize.IsEmpty ? 0d : (viewportWidth - imageSize.Width) / 2d);
        Canvas.SetTop(_image, imageSize.IsEmpty ? 0d : (viewportHeight - imageSize.Height) / 2d);
        _image.Opacity = opacity;
        _video.Stretch = Stretch.Fill;
        _video.Width = videoSize.IsEmpty ? double.NaN : videoSize.Width;
        _video.Height = videoSize.IsEmpty ? double.NaN : videoSize.Height;
        Canvas.SetLeft(_video, videoSize.IsEmpty ? 0d : (viewportWidth - videoSize.Width) / 2d);
        Canvas.SetTop(_video, videoSize.IsEmpty ? 0d : (viewportHeight - videoSize.Height) / 2d);
        _video.Opacity = opacity;
        var offsetX = double.IsFinite(HorizontalOffset) ? HorizontalOffset : 0d;
        var offsetY = double.IsFinite(VerticalOffset) ? VerticalOffset : 0d;
        _imageOffset.X = offsetX;
        _imageOffset.Y = offsetY;
        _videoOffset.X = offsetX;
        _videoOffset.Y = offsetY;
        _image.Visibility = MediaEnabled && _image.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        _video.Visibility = MediaEnabled && _video.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        _diagnostic.Visibility = ShowDiagnostics && !string.IsNullOrWhiteSpace(_diagnostic.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshMedia()
    {
        StopMedia();
        _diagnostic.Text = "";

        if (!MediaEnabled)
        {
            ApplyVisuals();
            return;
        }

        if (string.IsNullOrWhiteSpace(Source))
        {
            SetDiagnostic("Missing asset");
            return;
        }

        var kind = ClassifySource(Source);
        if (kind == MediaKind.Unsupported)
        {
            SetDiagnostic("Unsupported media");
            return;
        }

        if (kind == MediaKind.Video)
        {
            StartVideo(Source);
            return;
        }

        var decodeWidth = (int)Math.Clamp(
            Math.Ceiling((ActualWidth > 0 ? ActualWidth : Width > 0 ? Width : 160d) *
                         VisualTreeHelper.GetDpi(this).DpiScaleX),
            32d,
            2048d);
        if (kind == MediaKind.StaticImage)
        {
            MediaLoadTask = LoadStaticImageAsync(Source, decodeWidth, _loadGeneration);
            return;
        }
        if (kind == MediaKind.Gif)
        {
            _gifLoadCancellation = new CancellationTokenSource();
            MediaLoadTask = LoadGifAsync(Source, _loadGeneration, _gifLoadCancellation.Token);
        }
    }

    private async Task LoadGifAsync(string source, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await Task.Run(() => (
                Gif: GifImageLoader.Load(source, cancellationToken),
                Exists: File.Exists(source)), cancellationToken).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != _loadGeneration || !IsLoaded) return;
                if (loaded.Gif is not { } gif)
                {
                    SetDiagnostic(loaded.Exists ? "Unable to decode" : "Missing asset");
                    return;
                }
                _gifAnimation = gif.Animation;
                _imageNaturalWidth = gif.Size.Width;
                _imageNaturalHeight = gif.Size.Height;
                StartGifAnimation();
                ApplyVisuals();
                UpdatePlaybackState();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void StartGifAnimation()
    {
        if (_gifAnimation is null) return;
        GifClock?.Controller?.Remove();
        _image.Source = (ImageSource)_gifAnimation.KeyFrames[0].Value;
        var animation = new ObjectAnimationUsingKeyFrames
        {
            KeyFrames = _gifAnimation.KeyFrames,
            Duration = _gifAnimation.Duration,
            RepeatBehavior = Loop ? RepeatBehavior.Forever : new RepeatBehavior(1)
        };
        GifClock = (AnimationClock)animation.CreateClock(true);
        _image.ApplyAnimationClock(System.Windows.Controls.Image.SourceProperty, GifClock);
    }

    private async Task LoadStaticImageAsync(string source, int decodeWidth, int generation)
    {
        var loaded = await Task.Run(() => (
            Image: AssetImageLoader.LoadBitmapFile(source, decodeWidth) as BitmapSource,
            Size: ReadNaturalImageSize(source),
            Exists: File.Exists(source))).ConfigureAwait(false);

        // The decode is frozen by AssetImageLoader. Only this small assignment needs the UI thread.
        await Dispatcher.InvokeAsync(() =>
        {
            if (generation != _loadGeneration || !IsLoaded) return;
            if (loaded.Image is null)
            {
                SetDiagnostic(loaded.Exists ? "Unable to decode" : "Missing asset");
                return;
            }
            _image.Source = loaded.Image;
            _imageNaturalWidth = loaded.Size.IsEmpty ? loaded.Image.PixelWidth : loaded.Size.Width;
            _imageNaturalHeight = loaded.Size.IsEmpty ? loaded.Image.PixelHeight : loaded.Size.Height;
            ApplyVisuals();
            UpdatePlaybackState();
        });
    }

    private static System.Windows.Size ReadNaturalImageSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            return new System.Windows.Size(frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return System.Windows.Size.Empty;
        }
    }

    private void StartVideo(string path)
    {
        try
        {
            _video.Source = new Uri(path, UriKind.Absolute);
            ApplyVisuals();
            UpdatePlaybackState();
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException or InvalidOperationException)
        {
            SetDiagnostic("Unable to decode");
        }
    }

    private bool ShouldPlay => MediaEnabled && MotionEnabled && IsLoaded && IsVisible;

    private void UpdatePlaybackState()
    {
        var controller = GifClock?.Controller;
        if (controller is not null)
        {
            if (ShouldPlay)
                controller.Resume();
            else
                controller.Pause();
        }

        if (_video.Source is not null)
        {
            if (ShouldPlay)
                _video.Play();
            else
                _video.Pause();
        }
    }

    private void OnVideoEnded(object sender, RoutedEventArgs e)
    {
        if (!ShouldPlay)
            return;

        if (Loop)
        {
            _video.Position = TimeSpan.Zero;
            _video.Play();
        }
        else
        {
            _video.Pause();
        }
    }

    private void OnVideoFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _video.Close();
        _video.Source = null;
        SetDiagnostic("Unable to decode");
    }

    private void StopMedia()
    {
        _loadGeneration++;
        _gifLoadCancellation?.Cancel();
        _gifLoadCancellation?.Dispose();
        _gifLoadCancellation = null;
        GifClock?.Controller?.Remove();
        GifClock = null;
        _gifAnimation = null;
        _image.ApplyAnimationClock(System.Windows.Controls.Image.SourceProperty, null);
        _image.Source = null;
        _imageNaturalWidth = 0;
        _imageNaturalHeight = 0;
        if (_video.Source is not null)
        {
            try
            {
                _video.Stop();
                _video.Close();
            }
            catch (InvalidOperationException)
            {
                // WPF can tear down the media graph before Unloaded is raised.
            }
        }
        _video.Source = null;
        _videoNaturalWidth = 0;
        _videoNaturalHeight = 0;
    }

    private void SetDiagnostic(string message)
    {
        _diagnostic.Text = message;
        ApplyVisuals();
    }
}
