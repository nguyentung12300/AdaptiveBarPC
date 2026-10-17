using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public sealed class RichMediaTests
{
    [Fact]
    public void DecodeArtwork_DecodesSmallFrozenBitmap()
    {
        var image = MediaSessionService.DecodeArtwork(CreatePngBytes());

        var bitmap = Assert.IsAssignableFrom<BitmapSource>(image);
        Assert.True(bitmap.IsFrozen);
        Assert.InRange(bitmap.PixelWidth, 1, MediaSessionService.ArtworkDecodePixelWidth);
    }

    [Fact]
    public void DecodeArtwork_MissingOrCorrupt_ReturnsNull()
    {
        Assert.Null(MediaSessionService.DecodeArtwork(null));
        Assert.Null(MediaSessionService.DecodeArtwork([]));
        Assert.Null(MediaSessionService.DecodeArtwork([1, 2, 3, 4, 5]));
    }

    [Fact]
    public void ArtworkCache_IsBoundedAndEvictsOldest()
    {
        var cache = new MediaArtworkCache(2);
        var first = new DrawingImage();
        var second = new DrawingImage();
        var third = new DrawingImage();

        cache.Set("first", first);
        cache.Set("second", second);
        cache.Set("third", third);

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("first", out _));
        Assert.True(cache.TryGet("second", out var secondResult));
        Assert.Same(second, secondResult);
        Assert.True(cache.TryGet("third", out var thirdResult));
        Assert.Same(third, thirdResult);
    }

    [Fact]
    public void ArtworkRefresh_OnlyWhenMediaIdentityChanges()
    {
        Assert.False(MediaSessionService.ArtworkNeedsRefresh("same", "same"));
        Assert.True(MediaSessionService.ArtworkNeedsRefresh("track-a", "track-b"));
    }

    [Fact]
    public void ArtworkResult_RequiresCurrentGenerationAndMediaKey()
    {
        Assert.True(MediaSessionService.IsArtworkResultCurrent(3, 3, "track-c", "track-c"));
        Assert.False(MediaSessionService.IsArtworkResultCurrent(2, 3, "track-c", "track-c"));
        Assert.False(MediaSessionService.IsArtworkResultCurrent(3, 3, "track-a", "track-c"));
    }

    [Fact]
    public void MediaPreview_MissingMetadataAndUnknownDuration_AreSafe()
    {
        using var preview = MediaItemViewModel.CreatePreview(
            title: " ",
            artist: "",
            position: TimeSpan.FromMinutes(2),
            duration: TimeSpan.Zero,
            isPlaying: false);

        Assert.Equal("Unknown track", preview.Title);
        Assert.Equal("Unknown artist", preview.Artist);
        Assert.Equal(TimeSpan.Zero, preview.Duration);
        Assert.Equal(0, preview.ProgressPercent);
        Assert.False(preview.IsPlaying);
        Assert.Equal("\uE768", preview.PlayPauseGlyph);
    }

    [Fact]
    public void TrackIdentityChange_ClearsPreviousArtworkBeforeReplacement()
    {
        var artworkA = new DrawingImage();
        using var vm = MediaItemViewModel.CreatePreview(title: "A", artist: "Artist", artwork: artworkA);

        ApplyState(vm, CreateState("track-b", "B"));

        Assert.Equal("B", vm.Title);
        Assert.Equal("track-b", vm.MediaKey);
        Assert.Null(vm.Artwork);
        Assert.False(vm.HasArtwork);
    }

    [Fact]
    public void StartupStaleThumbnail_InitialMetadataThumbnailCannotBeRefreshInput()
    {
        var method = typeof(MediaSessionService).GetMethod(
            "RefreshArtworkAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType.Name.Contains("RandomAccessStreamReference", StringComparison.Ordinal));
        Assert.Equal(2, MediaSessionService.ArtworkSettleReadCount);
        Assert.True(MediaSessionService.ArtworkSettleDelay > TimeSpan.Zero);
    }

    [Fact]
    public void ArtworkOutOfOrder_AtoBtoC_OnlyCurrentArtworkCanBecomeVisible()
    {
        var artworkA = new DrawingImage();
        var artworkB = new DrawingImage();
        var artworkC = new DrawingImage();
        using var vm = MediaItemViewModel.CreatePreview(title: "A", artist: "Artist", artwork: artworkA);

        ApplyState(vm, CreateState("track-b", "B"));
        ApplyState(vm, CreateState("track-c", "C"));
        ApplyArtwork(vm, new MediaArtworkState("track-b", artworkB));
        ApplyArtwork(vm, new MediaArtworkState("track-a", artworkA));

        Assert.Null(vm.Artwork);

        ApplyArtwork(vm, new MediaArtworkState("track-c", artworkC));

        Assert.Same(artworkC, vm.Artwork);
    }

    [Fact]
    public void ArtworkCacheIdentity_DifferentTracksDoNotShareStrongKey_AndWeakIdentityDoesNotCache()
    {
        var firstKey = MediaSessionService.BuildMediaKey("player", "Song", "Artist", "Album", trackNumber: 1);
        var secondKey = MediaSessionService.BuildMediaKey("player", "Song", "Artist", "Album", trackNumber: 2);
        var cache = new MediaArtworkCache(2);
        var firstArtwork = new DrawingImage();
        var secondArtwork = new DrawingImage();

        cache.Set(firstKey, firstArtwork);
        cache.Set(secondKey, secondArtwork);

        Assert.NotEqual(firstKey, secondKey);
        Assert.Same(firstArtwork, AssertCacheHit(cache, firstKey));
        Assert.Same(secondArtwork, AssertCacheHit(cache, secondKey));
        Assert.False(MediaSessionService.IsArtworkCacheIdentityStrong("player", "", "", ""));
        Assert.False(MediaSessionService.IsArtworkCacheIdentityStrong("player", "Title only", "", ""));
        Assert.True(MediaSessionService.IsArtworkCacheIdentityStrong("player", "Song", "Artist", ""));
    }

    [Fact]
    public void NullArtworkUpdate_ClearsMatchingViewModelArtwork()
    {
        var artwork = new DrawingImage();
        using var vm = MediaItemViewModel.CreatePreview(title: "A", artist: "Artist", artwork: artwork);
        ApplyState(vm, CreateState("track-a", "A"));
        ApplyArtwork(vm, new MediaArtworkState("track-a", artwork));

        ApplyArtwork(vm, new MediaArtworkState("track-a", null));

        Assert.Null(vm.Artwork);
        Assert.False(vm.HasArtwork);
    }

    [Fact]
    public void SessionReset_ClearsPreviousSessionArtworkImmediately()
    {
        var artwork = new DrawingImage();
        using var vm = MediaItemViewModel.CreatePreview(title: "A", artist: "Artist", artwork: artwork);
        ApplyState(vm, CreateState("track-a", "A"));
        ApplyArtwork(vm, new MediaArtworkState("track-a", artwork));

        ApplyArtwork(vm, new MediaArtworkState(string.Empty, null));

        Assert.Null(vm.Artwork);
    }

    private static byte[] CreatePngBytes()
    {
        var pixels = new byte[]
        {
            0x20, 0x60, 0xD0, 0xFF,
            0xD0, 0x60, 0x20, 0xFF,
            0x50, 0xD0, 0x60, 0xFF,
            0x80, 0x40, 0xA0, 0xFF
        };
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static MediaSessionState CreateState(string mediaKey, string title) => new(
        IsAvailable: true,
        SourceAppId: "player",
        Title: title,
        Artist: "Artist",
        IsPlaying: false,
        CanPlay: true,
        CanPause: true,
        CanSkipNext: true,
        CanSkipPrevious: true,
        Position: TimeSpan.Zero,
        Duration: TimeSpan.FromMinutes(3),
        AlbumTitle: "Album",
        MediaKey: mediaKey);

    private static void ApplyState(MediaItemViewModel vm, MediaSessionState state) =>
        typeof(MediaItemViewModel)
            .GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [state]);

    private static void ApplyArtwork(MediaItemViewModel vm, MediaArtworkState state) =>
        typeof(MediaItemViewModel)
            .GetMethod("ApplyArtworkState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [state]);

    private static ImageSource AssertCacheHit(MediaArtworkCache cache, string key)
    {
        Assert.True(cache.TryGet(key, out var artwork));
        return Assert.IsAssignableFrom<ImageSource>(artwork);
    }
}
