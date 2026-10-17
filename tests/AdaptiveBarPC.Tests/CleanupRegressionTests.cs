using System.IO;
using System.Reflection;
using AdaptiveBarPC.Interop;
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

        VerifyMissingEndpointFallbackAndReconnect();
    }

    private static void VerifyMissingEndpointFallbackAndReconnect()
    {
        var endpointAvailable = false;
        var enumeratorCalls = 0;
        float endpointVolume = 0.65f;
        var endpointMuted = false;
        var endpointVolumeControl = Stub<IAudioEndpointVolume>((method, args) =>
        {
            switch (method)
            {
                case "GetMasterVolumeLevelScalar": args[0] = endpointVolume; break;
                case "SetMasterVolumeLevelScalar": endpointVolume = (float)args[0]!; break;
                case "GetMute": args[0] = endpointMuted; break;
                case "SetMute": endpointMuted = (bool)args[0]!; break;
            }

            return 0;
        });
        var device = Stub<IMMDevice>((method, args) =>
        {
            if (method == "Activate") args[3] = endpointVolumeControl;
            return 0;
        });
        var enumerator = Stub<IMMDeviceEnumerator>((method, args) =>
        {
            if (method != "GetDefaultAudioEndpoint")
                return 0;

            if (!endpointAvailable)
                return unchecked((int)0x80070490);

            args[2] = device;
            return 0;
        });

        using var audio = new WindowsAudioProvider(() =>
        {
            enumeratorCalls++;
            return enumerator;
        });

        Assert.Equal(50.0, audio.GetMasterVolumePercent());
        Assert.False(audio.GetMute());
        audio.SetMasterVolumePercent(23.5);
        audio.ToggleMute();
        Assert.Equal(23.5, audio.GetMasterVolumePercent());
        Assert.True(audio.GetMute());
        Assert.Equal(6, enumeratorCalls);

        endpointAvailable = true;
        Assert.Equal(65.0, audio.GetMasterVolumePercent(), precision: 2);
        Assert.False(audio.GetMute());
        audio.SetMasterVolumePercent(10);
        Assert.Equal(10.0, endpointVolume * 100.0, precision: 2);
        audio.ToggleMute();
        Assert.True(audio.GetMute());
    }

    public class ComProxy : DispatchProxy
    {
        internal Func<string, object?[], object?> Handler = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Handler(method!.Name, args!);
    }

    private static T Stub<T>(Func<string, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ComProxy>();
        ((ComProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
