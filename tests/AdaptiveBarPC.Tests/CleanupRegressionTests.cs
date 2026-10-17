using System.IO;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public class CleanupRegressionTests
{
    [Fact]
    public void ProfileBurstReloadsLatestFileAndStopsAfterDisposal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AdaptiveBarCleanup_" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "profile.json");
        File.WriteAllText(path, "{\"id\":\"old\",\"isDefault\":true}");
        try
        {
            using var changed = new ManualResetEventSlim();
            using var profiles = new ProfileService(directory, new AppLogger(directory));
            var app = new ForegroundAppInfo(IntPtr.Zero, "", "");
            string? loaded = null;
            var notifications = 0;
            profiles.ProfilesChanged += (_, _) =>
            {
                loaded = profiles.Select(app).Id;
                Interlocked.Increment(ref notifications);
                changed.Set();
            };
            for (var i = 0; i < 5; i++)
                File.WriteAllText(path, $"{{\"id\":\"new{i}\",\"isDefault\":true}}");

            Assert.True(changed.Wait(TimeSpan.FromSeconds(5)), "Profile hot reload did not fire.");
            Assert.Equal("new4", loaded);
            profiles.Dispose();
            var count = notifications;
            File.WriteAllText(path, "{\"id\":\"after-dispose\",\"isDefault\":true}");
            Thread.Sleep(250);
            Assert.Equal(count, notifications);
            Assert.Equal("new4", profiles.Select(app).Id);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WindowsEndpointCanBeReadRepeatedlyAfterRelease()
    {
        using var audio = new WindowsAudioProvider();
        for (var i = 0; i < 3; i++)
        {
            Assert.InRange(audio.GetMasterVolumePercent(), 0, 100);
            _ = audio.GetMute();
        }
    }
}
