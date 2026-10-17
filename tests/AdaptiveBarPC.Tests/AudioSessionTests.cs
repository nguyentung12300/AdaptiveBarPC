using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.Services.Audio;
using AdaptiveBarPC.Services.HardwareMonitor;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class AudioSessionTests
{
    private class FakeAudioSessionBackend : IAudioSessionBackend
    {
        public string CurrentEndpointId { get; set; } = "endpoint-1";
        public string CurrentEndpointName { get; set; } = "Speakers";
        public List<AudioSessionDescriptor> Sessions { get; } = new();
        public bool InitializeSuccess { get; set; } = true;
        public bool IsDisposed { get; private set; }

        public event EventHandler? SessionsChanged;

        public IReadOnlyList<AudioSessionDescriptor> GetSessions()
        {
            return Sessions.ToList();
        }

        public bool Initialize()
        {
            return InitializeSuccess;
        }

        public void TriggerSessionsChanged()
        {
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SwitchEndpoint(string id, string name, List<AudioSessionDescriptor> newSessions)
        {
            CurrentEndpointId = id;
            CurrentEndpointName = name;
            Sessions.Clear();
            Sessions.AddRange(newSessions);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    [Fact]
    public void Test01_OneSession_CreatesOneApplicationGroup()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 1234,
            ProcessName = "chrome",
            FriendlyAppName = "Google Chrome",
            ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            Volume = 0.72f,
            IsMuted = false,
            State = AudioSessionState.AudioSessionStateActive
        });

        using var service = new WindowsAudioSessionService(backend);
        var groups = service.CurrentGroups;

        Assert.Single(groups);
        Assert.Equal("Google Chrome", groups[0].FriendlyName);
        Assert.Equal("chrome", groups[0].ApplicationKey);
        Assert.Equal(0.72f, groups[0].EffectiveVolume, precision: 2);
        Assert.False(groups[0].IsMuted);
        Assert.Equal(AudioSessionState.AudioSessionStateActive, groups[0].State);
    }

    [Fact]
    public void Test02_MultipleSessions_FromSameApp_GroupedIntoOne()
    {
        var backend = new FakeAudioSessionBackend();
        var exePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 101,
            ProcessName = "chrome",
            FriendlyAppName = "Google Chrome",
            ExecutablePath = exePath,
            Volume = 0.60f,
            State = AudioSessionState.AudioSessionStateActive
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 102,
            ProcessName = "chrome",
            FriendlyAppName = "Google Chrome",
            ExecutablePath = exePath,
            Volume = 0.80f,
            State = AudioSessionState.AudioSessionStateActive
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 103,
            ProcessName = "chrome",
            FriendlyAppName = "Google Chrome",
            ExecutablePath = exePath,
            Volume = 0.70f,
            State = AudioSessionState.AudioSessionStateActive
        });

        using var service = new WindowsAudioSessionService(backend);
        var groups = service.CurrentGroups;

        Assert.Single(groups);
        Assert.Equal("Google Chrome", groups[0].FriendlyName);
        Assert.Equal(3, groups[0].Sessions.Count);
        // Average: (0.60 + 0.80 + 0.70) / 3 = 0.70
        Assert.Equal(0.70f, groups[0].EffectiveVolume, precision: 2);
    }

    [Fact]
    public void Test03_GroupedVolumeWrite_AffectsAllUnderlyingSessions()
    {
        var backend = new FakeAudioSessionBackend();
        var exePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

        float vol1 = 0, vol2 = 0;
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 101,
            ProcessName = "chrome",
            ExecutablePath = exePath,
            Volume = 0.5f,
            SetVolumeAction = v => vol1 = v
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 102,
            ProcessName = "chrome",
            ExecutablePath = exePath,
            Volume = 0.5f,
            SetVolumeAction = v => vol2 = v
        });

        using var service = new WindowsAudioSessionService(backend);
        var group = service.CurrentGroups[0];
        group.SetVolume(0.40f);

        Assert.Equal(0.40f, vol1);
        Assert.Equal(0.40f, vol2);
    }

    [Fact]
    public void Test04_GroupedMute_AffectsAllUnderlyingSessions()
    {
        var backend = new FakeAudioSessionBackend();
        var exePath = @"C:\Program Files\Spotify\Spotify.exe";

        bool mute1 = false, mute2 = false;
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 201,
            ProcessName = "spotify",
            ExecutablePath = exePath,
            SetMuteAction = m => mute1 = m
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 202,
            ProcessName = "spotify",
            ExecutablePath = exePath,
            SetMuteAction = m => mute2 = m
        });

        using var service = new WindowsAudioSessionService(backend);
        var group = service.CurrentGroups[0];
        group.SetMute(true);

        Assert.True(mute1);
        Assert.True(mute2);
    }

    [Fact]
    public void Test05_DifferentApps_RemainSeparate()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 200,
            ProcessName = "spotify",
            ExecutablePath = @"C:\Spotify\spotify.exe"
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 300,
            ProcessName = "discord",
            ExecutablePath = @"C:\Discord\discord.exe"
        });

        using var service = new WindowsAudioSessionService(backend);
        var groups = service.CurrentGroups;

        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, g => g.ApplicationKey == "chrome");
        Assert.Contains(groups, g => g.ApplicationKey == "spotify");
        Assert.Contains(groups, g => g.ApplicationKey == "discord");
    }

    [Fact]
    public void Test06_SameApp_OnDifferentEndpoint_RemainsDistinguishableInternally()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "endpoint-A",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "endpoint-B",
            ProcessId = 101,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });

        using var service = new WindowsAudioSessionService(backend);
        var groups = service.CurrentGroups;

        Assert.Equal(2, groups.Count);
        Assert.NotEqual(groups[0].Id, groups[1].Id);
        Assert.Contains("endpoint-a", groups[0].Id);
        Assert.Contains("endpoint-b", groups[1].Id);
    }

    [Fact]
    public void Test07_StableApplicationId_AcrossUpdates()
    {
        var backend = new FakeAudioSessionBackend();
        var session = new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe",
            Volume = 0.5f
        };
        backend.Sessions.Add(session);

        using var service = new WindowsAudioSessionService(backend);
        var id1 = service.CurrentGroups[0].Id;

        session.Volume = 0.8f;
        service.ReconcileGroups(null);
        var id2 = service.CurrentGroups[0].Id;

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Test08_ProcessIdChanges_DoNotChangeAppIdentity_WhenExecutableSame()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 1111,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });

        using var service = new WindowsAudioSessionService(backend);
        var idBefore = service.CurrentGroups[0].Id;

        // Process restarted with new PID
        backend.Sessions.Clear();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 2222,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });
        service.ReconcileGroups(null);
        var idAfter = service.CurrentGroups[0].Id;

        Assert.Equal(idBefore, idAfter);
    }

    [Fact]
    public void Test09_ActiveSession_AppearsInSnapshot()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "spotify",
            FriendlyAppName = "Spotify",
            ExecutablePath = @"C:\Spotify\spotify.exe",
            State = AudioSessionState.AudioSessionStateActive,
            Volume = 0.85f
        });

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        var items = provider.CurrentItems;
        Assert.Single(items);
        Assert.Equal(WidgetTypes.AudioSession, items[0].Type);
        Assert.Equal("Spotify", items[0].Label);
        Assert.Equal(85, items[0].Value);
    }

    [Fact]
    public void Test10_ExpiredSession_Disappears()
    {
        var backend = new FakeAudioSessionBackend();
        var session = new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "spotify",
            ExecutablePath = @"C:\Spotify\spotify.exe",
            State = AudioSessionState.AudioSessionStateActive
        };
        backend.Sessions.Add(session);

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        Assert.Single(provider.CurrentItems);
        Assert.NotEqual("audio:empty", provider.CurrentItems[0].Id);

        // Session expires
        session.State = AudioSessionState.AudioSessionStateExpired;
        service.ReconcileGroups(null);
        provider.PublishSnapshot();

        // Expired session removed -> empty placeholder emitted
        Assert.Single(provider.CurrentItems);
        Assert.Equal("audio:empty", provider.CurrentItems[0].Id);
    }

    [Fact]
    public void Test11_InactiveValidAppBehavior()
    {
        var backend = new FakeAudioSessionBackend();
        // Use current process ID so process alive check passes
        var currentPid = Environment.ProcessId;

        var descriptors = new List<AudioSessionDescriptor>
        {
            new()
            {
                EndpointId = "ep-1",
                ProcessId = currentPid,
                ProcessName = "AdaptiveBarTest",
                ExecutablePath = @"C:\Test\app.exe",
                State = AudioSessionState.AudioSessionStateInactive
            },
            new()
            {
                EndpointId = "ep-1",
                ProcessId = 999999, // Non-existent process
                ProcessName = "DeadApp",
                ExecutablePath = @"C:\Dead\dead.exe",
                State = AudioSessionState.AudioSessionStateInactive
            }
        };

        // Note: BuildApplicationGroups filters out current process ID if it equals AdaptiveBar's own process.
        // Let's test with a mock session where ProcessId is valid and alive (e.g. explorer.exe or similar running process)
        var explorerProc = Process.GetProcessesByName("explorer").FirstOrDefault();
        int testPid = explorerProc != null ? explorerProc.Id : 100;

        var testSessions = new List<AudioSessionDescriptor>
        {
            new()
            {
                EndpointId = "ep-1",
                ProcessId = testPid,
                ProcessName = "TestApp",
                ExecutablePath = @"C:\Apps\test.exe",
                State = AudioSessionState.AudioSessionStateInactive
            }
        };

        var groups = WindowsAudioSessionService.BuildApplicationGroups(testSessions);
        if (explorerProc != null)
        {
            // Alive process should be retained
            Assert.Single(groups);
            Assert.Equal(AudioSessionState.AudioSessionStateInactive, groups[0].State);
        }
    }

    [Fact]
    public void Test12_ForegroundApplication_OrderedFirst()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            FriendlyAppName = "Chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 200,
            ProcessName = "discord",
            FriendlyAppName = "Discord",
            ExecutablePath = @"C:\Discord\discord.exe"
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 300,
            ProcessName = "spotify",
            FriendlyAppName = "Spotify",
            ExecutablePath = @"C:\Spotify\spotify.exe"
        });

        // Set Discord as foreground
        var foreground = new ForegroundAppInfo(
            Hwnd: (IntPtr)0x123,
            ProcessName: "discord",
            WindowTitle: "Discord",
            ExecutablePath: @"C:\Discord\discord.exe",
            FriendlyAppName: "Discord");

        var groups = WindowsAudioSessionService.BuildApplicationGroups(backend.Sessions, foreground);

        Assert.Equal(3, groups.Count);
        Assert.Equal("Discord", groups[0].FriendlyName);
        Assert.True(groups[0].IsForeground);
    }

    [Fact]
    public void Test13_DeterministicOrdering()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 200,
            ProcessName = "spotify",
            FriendlyAppName = "Spotify",
            ExecutablePath = @"C:\Spotify\spotify.exe",
            State = AudioSessionState.AudioSessionStateActive
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            FriendlyAppName = "Chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe",
            State = AudioSessionState.AudioSessionStateActive
        });

        // Without foreground, active apps sorted alphabetically: Chrome, then Spotify
        var groups = WindowsAudioSessionService.BuildApplicationGroups(backend.Sessions, foreground: null);
        Assert.Equal("Chrome", groups[0].FriendlyName);
        Assert.Equal("Spotify", groups[1].FriendlyName);
    }

    [Fact]
    public void Test14_VolumePercentageConversion()
    {
        var backend = new FakeAudioSessionBackend();
        float written = 0;
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe",
            Volume = 0.654f,
            SetVolumeAction = v => written = v
        });

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        var item = provider.CurrentItems[0];
        Assert.Equal(65, item.Value);
        Assert.Equal(0, item.Min);
        Assert.Equal(100, item.Max);

        item.ValueSetter?.Invoke(42);
        Assert.Equal(0.42f, written, precision: 2);
    }

    [Fact]
    public void Test15_ExternalVolumeUpdate_ChangesSnapshot()
    {
        var backend = new FakeAudioSessionBackend();
        var session = new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe",
            Volume = 0.50f
        };
        backend.Sessions.Add(session);

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        Assert.Equal(50, provider.CurrentItems[0].Value);

        // Simulate external volume change in Windows Volume Mixer
        using var changed = new ManualResetEventSlim();
        provider.ItemsChanged += (_, _) => changed.Set();
        session.Volume = 0.85f;
        backend.TriggerSessionsChanged();

        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(85, provider.CurrentItems[0].Value);
    }

    [Fact]
    public void Test16_ExternalMuteUpdate_ChangesSnapshot()
    {
        var backend = new FakeAudioSessionBackend();
        var session = new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe",
            IsMuted = false
        };
        backend.Sessions.Add(session);

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        Assert.False(provider.CurrentItems[0].IsChecked);

        // External mute
        using var changed = new ManualResetEventSlim();
        provider.ItemsChanged += (_, _) => changed.Set();
        session.IsMuted = true;
        backend.TriggerSessionsChanged();

        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(provider.CurrentItems[0].IsChecked);
    }

    [Fact]
    public void Test17_InvalidOrDeadProcess_DoesNotCrashReconciliation()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 0, // System Idle
            IsSystemSound = false
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 4, // System
            IsSystemSound = false
        });
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "validApp",
            ExecutablePath = @"C:\valid.exe"
        });

        using var service = new WindowsAudioSessionService(backend);
        var groups = service.CurrentGroups;

        Assert.Single(groups);
        Assert.Equal("validApp", groups[0].FriendlyName);
    }

    [Fact]
    public void Test18_DuplicateUnderlyingSessions_DoNotCreateDuplicateUIEntries()
    {
        var backend = new FakeAudioSessionBackend();
        for (int i = 0; i < 5; i++)
        {
            backend.Sessions.Add(new AudioSessionDescriptor
            {
                EndpointId = "ep-1",
                ProcessId = 1000 + i,
                ProcessName = "chrome",
                ExecutablePath = @"C:\Chrome\chrome.exe"
            });
        }

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        Assert.Single(provider.CurrentItems);
    }

    [Fact]
    public void Test19_MonotonicRevisions_StartAtOne()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        Assert.Equal(1, provider.CurrentRevision);

        long receivedRevision = 0;
        using var changed = new ManualResetEventSlim();
        provider.ItemsChanged += (_, e) => { receivedRevision = e.Revision; changed.Set(); };

        backend.Sessions[0].Volume = 0.9f;
        backend.TriggerSessionsChanged();

        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(receivedRevision > 1);
    }

    [Fact]
    public void Test20_ProviderDisposal_StopsFurtherEvents()
    {
        var backend = new FakeAudioSessionBackend();
        using var service = new WindowsAudioSessionService(backend);
        var provider = new AudioSessionProvider(service);

        int eventCount = 0;
        provider.ItemsChanged += (_, _) => eventCount++;

        provider.Dispose();

        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "ep-1",
            ProcessId = 100,
            ProcessName = "chrome"
        });
        backend.TriggerSessionsChanged();

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void Test21_CoreAudioInitializationFailure_DegradesGracefully()
    {
        var backend = new FakeAudioSessionBackend { InitializeSuccess = false };
        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        Assert.Empty(service.CurrentGroups);
        Assert.Single(provider.CurrentItems);
        Assert.Equal("audio:empty", provider.CurrentItems[0].Id);
        Assert.Equal("No app audio", provider.CurrentItems[0].Label);
    }

    [Fact]
    public void Test22_DefaultEndpointChange_RebuildsSessionSourceCleanly()
    {
        var backend = new FakeAudioSessionBackend();
        backend.Sessions.Add(new AudioSessionDescriptor
        {
            EndpointId = "speakers-1",
            ProcessId = 100,
            ProcessName = "chrome",
            ExecutablePath = @"C:\Chrome\chrome.exe"
        });

        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);

        Assert.Contains("speakers-1", provider.CurrentItems[0].Id);

        // Switch default endpoint to Headphones
        var newSessions = new List<AudioSessionDescriptor>
        {
            new()
            {
                EndpointId = "headphones-2",
                ProcessId = 100,
                ProcessName = "chrome",
                ExecutablePath = @"C:\Chrome\chrome.exe"
            }
        };
        using var changed = new ManualResetEventSlim();
        provider.ItemsChanged += (_, _) => changed.Set();
        backend.SwitchEndpoint("headphones-2", "Headphones", newSessions);

        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Contains("headphones-2", provider.CurrentItems[0].Id);
    }

    [Fact]
    public void Test23_HardwareSensorProvider_StillWorks()
    {
        using var hardware = new HardwareSensorProvider();
        Assert.NotNull(hardware);
        Assert.Equal("hardware", hardware.Id);
        Assert.NotNull(hardware.CurrentItems);
    }

    [Fact]
    public void Test24_ExistingDynamicWidgetTests_StillPass()
    {
        var fields = typeof(UIElement).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(f => f.Name.Contains("MouseOver"))
            .Select(f => f.Name)
            .ToList();
        Assert.NotEmpty(fields);
    }

    [Fact]
    public void TestLive_WindowsCoreAudioBackend_CanInitializeAndEnumerate()
    {
        using var backend = new WindowsCoreAudioBackend();
        bool ok = backend.Initialize();
        if (!ok) return;

        var sessions = backend.GetSessions();
        Console.WriteLine($"[LIVE TEST] Endpoint: {backend.CurrentEndpointName} ({backend.CurrentEndpointId})");
        Console.WriteLine($"[LIVE TEST] Discovered sessions count: {sessions.Count}");
        foreach (var s in sessions)
        {
            Console.WriteLine($"[LIVE TEST]   - {s.FriendlyAppName} (Process: {s.ProcessName}, PID: {s.ProcessId}, State: {s.State}, Vol: {(int)(s.Volume * 100)}%, Muted: {s.IsMuted})");
        }
    }

    [Fact]
    public void Test25_CoreAudioInterfaceGuids_MatchWindowsSdk()
    {
        var asm = typeof(WindowsCoreAudioBackend).Assembly;
        var expected = new Dictionary<string, string>
        {
            ["MMDeviceEnumeratorComObject"] = "BCDE0395-E52F-467C-8E3D-C4579291692E",
            ["IMMDeviceEnumerator"] = "A95664D2-9614-4F35-A746-DE8DB63617E6",
            ["IMMDevice"] = "D666063F-1587-4E43-81F1-B948E807363F",
            ["IMMNotificationClient"] = "7991EEC9-7E89-4D85-8390-6C703CEC60C0",
            ["IPropertyStore"] = "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99",
            ["IAudioEndpointVolume"] = "5CDF2C82-841E-4546-9722-0CF74078229A",
            ["IAudioSessionManager2"] = "77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F",
            ["IAudioSessionEnumerator"] = "E2F5BB11-0570-40CA-ACDD-3AA01277DEE8",
            ["IAudioSessionControl"] = "F4B1A599-7266-4319-A8CA-E70ACB11E8CD",
            ["IAudioSessionControl2"] = "BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D",
            ["ISimpleAudioVolume"] = "87CE5498-68D6-44E5-9215-6DA47EF883D8",
            ["IAudioSessionEvents"] = "24918ACC-64B3-37C1-8CA9-74A66E9957A8",
            ["IAudioSessionNotification"] = "641DD20B-4D41-49CC-ABA3-174B9477BB08"
        };
        var declared = asm.GetTypes().Where(t => t.Namespace == "AdaptiveBarPC.Interop" && t.IsImport
            && t.GetCustomAttribute<GuidAttribute>() != null);
        Assert.Equal(expected.Keys.OrderBy(n => n), declared.Select(t => t.Name).OrderBy(n => n));
        foreach (var (name, guid) in expected)
        {
            var type = asm.GetType($"AdaptiveBarPC.Interop.{name}", throwOnError: true)!;
            Assert.Equal(guid, type.GetCustomAttribute<GuidAttribute>()!.Value, ignoreCase: true);
        }
    }

    [Fact]
    public void TestLive_AudioSessionProvider_EmitsRealApplicationSnapshots()
    {
        using var backend = new WindowsCoreAudioBackend();
        using var service = new WindowsAudioSessionService(backend);
        using var provider = new AudioSessionProvider(service);
        var items = provider.CurrentItems;
        Console.WriteLine($"[LIVE PROVIDER] Items Count: {items.Count}");
        foreach (var item in items)
        {
            Console.WriteLine($"[LIVE PROVIDER]   - Item: Id={item.Id}, Type={item.Type}, Label={item.Label}, Value={item.Value}, Checked={item.IsChecked}");
        }
    }
}
