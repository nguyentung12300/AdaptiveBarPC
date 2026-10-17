using System.IO;
using System.Reflection;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public class MediaSessionServiceTests
{
    [Fact]
    public void CurrentState_ExtrapolatesPlayingPositionFromCachedTimestamp()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBar_MediaStateTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var service = new MediaSessionService(new AppLogger(tempDir));
            var state = new MediaSessionState(
                IsAvailable: true,
                SourceAppId: "test",
                Title: "Track",
                Artist: "Artist",
                IsPlaying: true,
                CanPlay: true,
                CanPause: true,
                CanSkipNext: false,
                CanSkipPrevious: false,
                Position: TimeSpan.FromSeconds(10),
                Duration: TimeSpan.FromSeconds(60));

            typeof(MediaSessionService).GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, state);
            typeof(MediaSessionService).GetField("_currentStateTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(3));

            var current = service.CurrentState;
            Assert.InRange(current.Position.TotalSeconds, 12.9, 13.5);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
