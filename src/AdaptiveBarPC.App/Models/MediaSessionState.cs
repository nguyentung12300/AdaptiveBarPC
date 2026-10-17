namespace AdaptiveBarPC.Models;

public sealed record MediaSessionState(
    bool IsAvailable,
    string SourceAppId,
    string Title,
    string Artist,
    bool IsPlaying,
    bool CanPlay,
    bool CanPause,
    bool CanSkipNext,
    bool CanSkipPrevious,
    TimeSpan Position,
    TimeSpan Duration,
    string AlbumTitle = "",
    string MediaKey = "")
{
    public static readonly MediaSessionState Empty = new(
        IsAvailable: false,
        SourceAppId: string.Empty,
        Title: string.Empty,
        Artist: string.Empty,
        IsPlaying: false,
        CanPlay: false,
        CanPause: false,
        CanSkipNext: false,
        CanSkipPrevious: false,
        Position: TimeSpan.Zero,
        Duration: TimeSpan.Zero,
        AlbumTitle: string.Empty,
        MediaKey: string.Empty);
}
