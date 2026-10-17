using System.Reflection;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services.Audio;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public class AudioPass2Tests
{
    private sealed class FakeBackend : IAudioSessionBackend
    {
        public string CurrentEndpointId { get; set; } = "ep-1";
        public string CurrentEndpointName { get; set; } = "Speakers";
        public List<AudioSessionDescriptor> Sessions { get; } = new();
        public event EventHandler? SessionsChanged;
        public bool Initialize() => true;
        public IReadOnlyList<AudioSessionDescriptor> GetSessions() => Sessions.ToArray();
        public void Notify() => SessionsChanged?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }

    private static AudioSessionDescriptor Session(
        string instance,
        int pid = 100,
        string path = @"C:\Apps\player.exe",
        string endpoint = "ep-1",
        float volume = 0.5f,
        string processName = "player",
        string friendlyName = "Player",
        Action<float>? setVolume = null,
        Action<bool>? setMute = null) => new()
        {
            EndpointId = endpoint,
            SessionIdentifier = $"session-{instance}",
            SessionInstanceIdentifier = instance,
            ProcessId = pid,
            ProcessName = processName,
            FriendlyAppName = friendlyName,
            ExecutablePath = path,
            State = AudioSessionState.AudioSessionStateActive,
            Volume = volume,
            SetVolumeAction = setVolume,
            SetMuteAction = setMute
        };

    [Fact]
    public void SessionReplacement_WithSameVisibleState_RefreshesSetterTarget()
    {
        var backend = new FakeBackend();
        int writesA = 0, writesB = 0;
        backend.Sessions.Add(Session("A", setVolume: _ => writesA++));

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);
        var stableId = provider.CurrentItems[0].Id;

        backend.Sessions.Clear();
        backend.Sessions.Add(Session("B", setVolume: _ => writesB++));
        service.ReconcileGroups(null);

        Assert.Equal(stableId, provider.CurrentItems[0].Id);
        provider.CurrentItems[0].ValueSetter!(70);
        Assert.Equal(0, writesA);
        Assert.Equal(1, writesB);
    }

    [Fact]
    public void MembershipChange_WithSameAggregate_RefreshesExactlyCurrentTargets()
    {
        var backend = new FakeBackend();
        int writesA = 0, writesB = 0;
        var a = Session("A", setVolume: _ => writesA++);
        var b = Session("B", pid: 101, setVolume: _ => writesB++);
        backend.Sessions.Add(a);

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        backend.Sessions.Add(b);
        service.ReconcileGroups(null);
        provider.CurrentItems[0].ValueSetter!(60);
        Assert.Equal(1, writesA);
        Assert.Equal(1, writesB);

        backend.Sessions.Remove(a);
        service.ReconcileGroups(null);
        provider.CurrentItems[0].ValueSetter!(70);
        Assert.Equal(1, writesA);
        Assert.Equal(2, writesB);
    }

    [Fact]
    public void ApplicationIdentity_UsesFullPathEndpointAndStableFallbacks()
    {
        var split = WindowsAudioSessionService.BuildApplicationGroups(new[]
        {
            Session("A", path: @"C:\AppA\player.exe"),
            Session("B", pid: 101, path: @"D:\AppB\player.exe")
        });
        Assert.Equal(2, split.Count);

        var before = Assert.Single(WindowsAudioSessionService.BuildApplicationGroups(new[]
        {
            Session("old", pid: 111, path: @"C:\Apps\..\Apps\player.exe")
        }));
        var after = Assert.Single(WindowsAudioSessionService.BuildApplicationGroups(new[]
        {
            Session("new", pid: 222, path: @"C:\Apps\player.exe")
        }));
        Assert.Equal(before.Id, after.Id);

        var endpoints = WindowsAudioSessionService.BuildApplicationGroups(new[]
        {
            Session("A", endpoint: "ep:a"),
            Session("B", pid: 101, endpoint: "ep.a")
        });
        Assert.Equal(2, endpoints.Count);
        Assert.NotEqual(endpoints[0].Id, endpoints[1].Id);

        var unresolved = WindowsAudioSessionService.BuildApplicationGroups(new[]
        {
            new AudioSessionDescriptor { EndpointId = "ep", ProcessId = 201, SessionInstanceIdentifier = "unknown-a" },
            new AudioSessionDescriptor { EndpointId = "ep", ProcessId = 202, SessionInstanceIdentifier = "unknown-b" }
        });
        Assert.Equal(2, unresolved.Count);
    }

    [Fact]
    public void ForegroundPriority_PersistsAcrossNormalReconciliation()
    {
        var backend = new FakeBackend();
        backend.Sessions.Add(Session("chrome", path: @"C:\Chrome\chrome.exe", processName: "chrome", friendlyName: "Chrome"));
        backend.Sessions.Add(Session("discord", pid: 101, path: @"C:\Discord\discord.exe", processName: "discord", friendlyName: "Discord"));

        using var service = new WindowsAudioSessionService(backend);
        service.ReconcileGroups(new ForegroundAppInfo(
            (IntPtr)1, "discord", "Discord", @"C:\Discord\discord.exe", "Discord"));
        Assert.Equal("Discord", service.CurrentGroups[0].FriendlyName);
        Assert.True(service.CurrentGroups[0].IsForeground);

        service.ReconcileGroups(null);
        Assert.Equal("Discord", service.CurrentGroups[0].FriendlyName);
        Assert.True(service.CurrentGroups[0].IsForeground);

        service.ReconcileGroups(new ForegroundAppInfo(
            (IntPtr)2, "chrome", "Chrome", @"C:\Chrome\chrome.exe", "Chrome"));
        Assert.Equal("Chrome", service.CurrentGroups[0].FriendlyName);
    }

    [Fact]
    public async Task ProviderPublish_DoesNotHoldProviderLockWhileWaitingForServiceLock()
    {
        var backend = new FakeBackend();
        backend.Sessions.Add(Session("A"));
        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        var serviceGate = typeof(WindowsAudioSessionService)
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        var providerGate = typeof(AudioSessionProvider)
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provider)!;
        using var started = new ManualResetEventSlim();
        Task publish;

        lock (serviceGate)
        {
            publish = Task.Run(() =>
            {
                started.Set();
                provider.PublishSnapshot();
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
#pragma warning disable xUnit1031 // Bounded blocking wait is deliberate while this thread owns serviceGate.
            Assert.False(publish.Wait(TimeSpan.FromMilliseconds(100)));
#pragma warning restore xUnit1031

            Assert.True(Monitor.TryEnter(providerGate, TimeSpan.FromSeconds(1)));
            Monitor.Exit(providerGate);
        }

        await publish.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SliderInteraction_RejectsStaleReadback_ThenAcceptsAckAndExternalUpdate()
    {
        int writes = 0;
        var vm = new AudioSessionItemViewModel("Player", 230, 0, 100, 60, false, _ => writes++, _ => { });
        vm.IsInteracting = true;
        vm.Value = 70;
        vm.Value = 80;
        vm.Value = 90;

        vm.UpdateFromSnapshot(new DynamicItemSnapshot { Value = 60, Min = 0, Max = 100, Label = "Player" });
        Assert.Equal(90, vm.Value);
        Assert.Equal(0, writes);

        vm.IsInteracting = false;
        vm.UpdateFromSnapshot(new DynamicItemSnapshot { Value = 60, Min = 0, Max = 100, Label = "Player" });
        Assert.Equal(90, vm.Value);
        Assert.Equal(1, writes);

        vm.UpdateFromSnapshot(new DynamicItemSnapshot { Value = 90, Min = 0, Max = 100, Label = "Player" });
        vm.UpdateFromSnapshot(new DynamicItemSnapshot { Value = 40, Min = 0, Max = 100, Label = "Player" });
        Assert.Equal(40, vm.Value);
        Assert.Equal(1, writes);
    }
}
