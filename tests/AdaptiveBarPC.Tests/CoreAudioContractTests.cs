using System.Reflection;
using System.Runtime.InteropServices;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Services.Audio;

namespace AdaptiveBarPC.Tests;

public class CoreAudioContractTests
{
    private const int EFail = unchecked((int)0x80004005);
    private const int NoSingleProcess = 0x0889000D; // AUDCLNT_SUCCESS(0x00d), FACILITY_AUDCLNT = 2185.

    [Fact]
    public void HResultMethods_PreserveTheirNativeSignatures()
    {
        var interfaces = typeof(IAudioSessionControl).Assembly.GetTypes()
            .Where(t => t.Namespace == "AdaptiveBarPC.Interop" && t.IsInterface && t.IsImport);
        Assert.NotEmpty(interfaces);
        foreach (var type in interfaces)
        {
            Assert.Equal(ComInterfaceType.InterfaceIsIUnknown, type.GetCustomAttribute<InterfaceTypeAttribute>()!.Value);
            foreach (var method in type.GetMethods())
            {
                Assert.Equal(typeof(int), method.ReturnType);
                Assert.True((method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0,
                    $"{type.Name}.{method.Name} must preserve its HRESULT.");
                foreach (var parameter in method.GetParameters().Where(p => p.ParameterType == typeof(Guid)))
                    Assert.Equal(UnmanagedType.LPStruct, parameter.GetCustomAttribute<MarshalAsAttribute>()!.Value);
            }
        }
    }

    [Fact]
    public void FlattenedInterfaces_KeepWindowsSdkVtableOrder()
    {
        static string[] Methods(Type type) => type.GetMethods().OrderBy(m => m.MetadataToken).Select(m => m.Name).ToArray();
        var control = new[] { "GetState", "GetDisplayName", "SetDisplayName", "GetIconPath", "SetIconPath",
            "GetGroupingParam", "SetGroupingParam", "RegisterAudioSessionNotification", "UnregisterAudioSessionNotification" };
        Assert.Equal(control, Methods(typeof(IAudioSessionControl)));
        Assert.Equal(control.Concat(new[] { "GetSessionIdentifier", "GetSessionInstanceIdentifier", "GetProcessId",
            "IsSystemSoundsSession", "SetDuckingPreference" }), Methods(typeof(IAudioSessionControl2)));
        Assert.Equal(new[] { "GetAudioSessionControl", "GetSimpleAudioVolume", "GetSessionEnumerator",
            "RegisterSessionNotification", "UnregisterSessionNotification", "RegisterDuckNotification", "UnregisterDuckNotification" },
            Methods(typeof(IAudioSessionManager2)));
    }

    [Fact]
    public void PropVariant_HasNativeStorageAndOffsets()
    {
        Console.WriteLine($"Core Audio ABI: process={RuntimeInformation.ProcessArchitecture}, pointer={IntPtr.Size}, PROPVARIANT={Marshal.SizeOf<PropVariant>()}");
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<PropVariant>());
        Assert.Equal(0, Marshal.OffsetOf<PropVariant>(nameof(PropVariant.vt)).ToInt32());
        Assert.Equal(2, Marshal.OffsetOf<PropVariant>(nameof(PropVariant.wReserved1)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<PropVariant>(nameof(PropVariant.wReserved2)).ToInt32());
        Assert.Equal(6, Marshal.OffsetOf<PropVariant>(nameof(PropVariant.wReserved3)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<PropVariant>(nameof(PropVariant.pwszVal)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<PropVariant>(nameof(PropVariant.caub)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4,
            Marshal.OffsetOf<PropVariant.CountedArray>(nameof(PropVariant.CountedArray.pElems)).ToInt32());

        var pv = new PropVariant { vt = 31, pwszVal = Marshal.StringToCoTaskMemUni("Audio endpoint") };
        try { Assert.Equal("Audio endpoint", pv.GetString()); }
        finally { Marshal.ThrowExceptionForHR(PropVariantInterop.PropVariantClear(ref pv)); }
        Assert.Equal((ushort)0, pv.vt);
        Assert.Equal(IntPtr.Zero, pv.pwszVal);
    }

    [DllImport("propsys.dll")]
    private static extern int PSCreateMemoryPropertyStore(
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IPropertyStore store);

    [Theory]
    [InlineData(0)]
    [InlineData(EFail)]
    public void FriendlyNameRead_ReleasesNativePropertyStore_EvenOnFailure(int openHr)
    {
        Marshal.ThrowExceptionForHR(PSCreateMemoryPropertyStore(typeof(IPropertyStore).GUID, out var store));
        var key = PropertyKey.PKEY_Device_FriendlyName;
        var value = new PropVariant { vt = 31, pwszVal = Marshal.StringToCoTaskMemUni("Test audio endpoint") };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        finally { Marshal.ThrowExceptionForHR(PropVariantInterop.PropVariantClear(ref value)); }
        var device = Stub<IMMDevice>((method, args) => { args[1] = store; return openHr; });
        var query = typeof(WindowsCoreAudioBackend).GetMethod("QueryDeviceFriendlyName", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            // Transfer the sole native store acquisition to QueryDeviceFriendlyName.
            Assert.Equal(openHr >= 0 ? "Test audio endpoint" : null, query.Invoke(null, new object[] { device }));
            Assert.Throws<InvalidComObjectException>(() => store.GetCount(out _));
        }
        finally
        {
            try { Marshal.ReleaseComObject(store); } catch (InvalidComObjectException) { }
        }
    }

    [Fact]
    public void DuplicateNativeAcquisition_ReleaseKeepsRetainedRcwUsable()
    {
        Marshal.ThrowExceptionForHR(PSCreateMemoryPropertyStore(typeof(IPropertyStore).GUID, out var retained));
        var release = typeof(WindowsCoreAudioBackend).GetMethod("ReleaseCom", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? duplicate = null;
        try
        {
            // Simulate COM returning the same identity again; balance the temporary native pointer.
            var unknown = Marshal.GetIUnknownForObject(retained);
            try { duplicate = Marshal.GetObjectForIUnknown(unknown); }
            finally { Marshal.Release(unknown); }
            Assert.Same(retained, duplicate);
            release.Invoke(null, new[] { duplicate });
            duplicate = null;
            Marshal.ThrowExceptionForHR(retained.GetCount(out var count));
            Assert.Equal(0U, count);
        }
        finally
        {
            if (duplicate != null) release.Invoke(null, new[] { duplicate });
            release.Invoke(null, new object[] { retained });
        }
        Assert.Throws<InvalidComObjectException>(() => retained.GetCount(out _));
    }

    [Theory]
    [InlineData(0, 123456U, 1)]
    [InlineData(NoSingleProcess, 123456U, 1)]
    [InlineData(EFail, 123456U, 0)]
    [InlineData(NoSingleProcess, 0U, 0)]
    [InlineData(NoSingleProcess, uint.MaxValue, 0)]
    public void ProcessIdStatus_AcceptsSuccessAndValidatesPid(int hr, uint pid, int expectedCount)
    {
        var session = new SessionStub { ProcessIdHr = hr, ProcessId = pid };
        var native = new BackendStub(session);
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        Assert.Equal(expectedCount, backend.GetSessions().Count);
    }

    [Fact]
    public void RepeatedEnumeration_PreservesTargets_ThenSuccessfulEmptyUnsubscribes()
    {
        var session = new SessionStub();
        var native = new BackendStub(session) { CountHr = 1 }; // Nonzero successful HRESULT.
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        var first = Assert.Single(backend.GetSessions());
        for (int i = 0; i < 3; i++) Assert.Single(backend.GetSessions());
        first.SetVolumeAction!(0.42f);
        first.SetMuteAction!(true);
        Assert.Equal(0.42f, Assert.Single(backend.GetSessions()).Volume);
        Assert.True(Assert.Single(backend.GetSessions()).IsMuted);
        Assert.Equal(1, session.RegisterCount);
        Assert.Equal(0, session.UnregisterCount);

        native.Sessions.Clear();
        Assert.Empty(backend.GetSessions());
        Assert.Equal(1, session.UnregisterCount);
        int writes = session.VolumeWrites;
        first.SetVolumeAction(0.9f);
        first.SetMuteAction(false);
        Assert.Equal(writes, session.VolumeWrites);
        Assert.True(session.Muted);
        backend.Dispose();
        Assert.Equal(1, session.UnregisterCount);
    }

    [Fact]
    public void FailedEnumeration_DoesNotDisposeRetainedTargets_AndBrokenSessionDoesNotAbortSiblings()
    {
        var valid = new SessionStub();
        var broken = new SessionStub { ProcessIdHr = EFail };
        var native = new BackendStub(broken, valid);
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        var retained = Assert.Single(backend.GetSessions());
        native.CountHr = EFail;
        Assert.Empty(backend.GetSessions());
        Assert.Equal(0, valid.UnregisterCount);
        retained.SetVolumeAction!(0.3f);
        Assert.Equal(0.3f, valid.Volume);
        native.CountHr = 0;
        Assert.Single(backend.GetSessions());
    }

    [Theory]
    [InlineData("RegisterEndpointNotificationCallback")]
    [InlineData("GetDefaultAudioEndpoint")]
    [InlineData("Activate")]
    [InlineData("RegisterSessionNotification")]
    public void PartialInitializationFailure_Unwinds_AndRetryStartsClean(string failedMethod)
    {
        var native = new BackendStub { FailedMethod = failedMethod };
        using var backend = native.CreateBackend();
        Assert.False(backend.Initialize());
        Assert.Equal(failedMethod == "RegisterEndpointNotificationCallback" ? 0 : 1, native.EndpointUnregisters);
        foreach (var field in new[] { "_enumerator", "_notificationClient", "_currentDevice", "_sessionManager", "_sessionNotification" })
            Assert.Null(typeof(WindowsCoreAudioBackend).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend));
        Assert.Equal("default", backend.CurrentEndpointId);
        native.FailedMethod = null;
        Assert.True(backend.Initialize());
        Assert.Equal(2, native.FactoryCalls);
        backend.Dispose();
        Assert.Equal(1, native.ManagerUnregisters);
    }

    [Fact]
    public async Task InFlightWrite_CompletesBeforeCleanup_AndDisposedSettersDoNothing()
    {
        var session = new SessionStub();
        var native = new BackendStub(session);
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        var descriptor = Assert.Single(backend.GetSessions());
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var cleanupStarted = new ManualResetEventSlim();
        session.OnVolumeWrite = () => { entered.Set(); Assert.True(finish.Wait(TimeSpan.FromSeconds(5))); };
        var write = Task.Run(() => descriptor.SetVolumeAction!(0.25f));
        Task? cleanup = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            cleanup = Task.Run(() => { cleanupStarted.Set(); backend.Dispose(); });
            Assert.True(cleanupStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, Volatile.Read(ref session.UnregisterCount));
        }
        finally
        {
            finish.Set();
            await write.WaitAsync(TimeSpan.FromSeconds(5));
            if (cleanup != null) await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, session.UnregisterCount);
        descriptor.SetVolumeAction!(0.9f);
        descriptor.SetMuteAction!(true);
        Assert.Equal(0.25f, session.Volume);
        Assert.False(session.Muted);
    }

    [Fact]
    public async Task NativeSessionCallback_QueuesWork_AndReturnsBeforeSubscriberCompletes()
    {
        var session = new SessionStub();
        var native = new BackendStub(session);
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        Assert.Single(backend.GetSessions());
        var callback = Assert.IsAssignableFrom<IAudioSessionEvents>(session.Events);
        using var handlerEntered = new ManualResetEventSlim();
        using var releaseHandler = new ManualResetEventSlim();
        using var handlerDone = new ManualResetEventSlim();
        backend.SessionsChanged += (_, _) =>
        {
            handlerEntered.Set();
            Assert.True(releaseHandler.Wait(TimeSpan.FromSeconds(5)));
            handlerDone.Set();
        };

        var invoke = Task.Run(() => callback.OnSimpleVolumeChanged(0.7f, false, Guid.Empty));
        try
        {
            Assert.Equal(0, await invoke.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(handlerEntered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseHandler.Set();
        }
        Assert.True(handlerDone.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void NativeCallback_AfterDispose_DoesNotPublish()
    {
        var session = new SessionStub();
        var native = new BackendStub(session);
        var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        Assert.Single(backend.GetSessions());
        var callback = Assert.IsAssignableFrom<IAudioSessionEvents>(session.Events);
        using var changed = new ManualResetEventSlim();
        backend.SessionsChanged += (_, _) => changed.Set();

        backend.Dispose();
        Assert.Equal(0, callback.OnSimpleVolumeChanged(0.8f, false, Guid.Empty));
        Assert.False(changed.Wait(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void EndpointChange_CleansUpOnlyWhenBackgroundReconciliationRuns()
    {
        var session = new SessionStub();
        var native = new BackendStub(session);
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        Assert.Single(backend.GetSessions());
        Assert.Equal(0, native.ManagerUnregisters);

        native.EndpointId = "endpoint-b";
        Assert.Equal(0, native.EndpointNotification!.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, "endpoint-b"));
        Assert.Equal(0, native.ManagerUnregisters);

        Assert.Single(backend.GetSessions());
        Assert.Equal(1, native.ManagerUnregisters);
        Assert.Equal("endpoint-b", backend.CurrentEndpointId);
    }

    [Fact]
    public void OldEndpointSessionCallback_IsIgnoredAfterRebind()
    {
        var session = new SessionStub();
        var native = new BackendStub(session);
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        Assert.Single(backend.GetSessions());
        var oldCallback = Assert.IsAssignableFrom<IAudioSessionEvents>(session.Events);
        using var endpointChanged = new ManualResetEventSlim();
        int notifications = 0;
        backend.SessionsChanged += (_, _) =>
        {
            Interlocked.Increment(ref notifications);
            endpointChanged.Set();
        };

        native.EndpointId = "endpoint-b";
        native.EndpointNotification!.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, "endpoint-b");
        Assert.True(endpointChanged.Wait(TimeSpan.FromSeconds(5)));
        Assert.Single(backend.GetSessions());
        Interlocked.Exchange(ref notifications, 0);
        endpointChanged.Reset();

        oldCallback.OnSimpleVolumeChanged(0.9f, false, Guid.Empty);
        Assert.False(endpointChanged.Wait(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public void TransientEndpointBindFailure_RecoversOnNextNormalReconciliation()
    {
        var session = new SessionStub();
        var native = new BackendStub(session);
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        Assert.Single(backend.GetSessions());

        native.EndpointId = "endpoint-b";
        native.GetDefaultFailuresRemaining = 1;
        native.EndpointNotification!.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, "endpoint-b");

        Assert.Empty(backend.GetSessions());
        Assert.Single(backend.GetSessions());
        Assert.Equal("endpoint-b", backend.CurrentEndpointId);
    }

    [Fact]
    public void DuplicateCurrentEndpointNotification_DoesNotRebindOrResubscribe()
    {
        var native = new BackendStub(new SessionStub());
        using var backend = native.CreateBackend();
        Assert.True(backend.Initialize());
        Assert.Equal(1, native.ManagerRegisters);
        using var changed = new ManualResetEventSlim();
        backend.SessionsChanged += (_, _) => changed.Set();

        native.EndpointNotification!.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, native.EndpointId);

        Assert.False(changed.Wait(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(1, native.ManagerRegisters);
        Assert.Equal(0, native.ManagerUnregisters);
    }

    [Fact]
    public void ProductionService_InitializesCoreAudioOnMtaWorker()
    {
        var native = new BackendStub(new SessionStub());
        using var factoryCalled = new ManualResetEventSlim();
        ApartmentState observed = ApartmentState.Unknown;
        native.OnFactoryCall = () =>
        {
            observed = Thread.CurrentThread.GetApartmentState();
            factoryCalled.Set();
        };

        using var service = new WindowsAudioSessionService(native.CreateBackend(), reconcileIntervalMs: 5000);
        Assert.True(factoryCalled.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(ApartmentState.MTA, observed);
    }

    [Fact]
    public async Task Dispose_DuringQueuedCoreAudioInitialization_DoesNotInstallTimerAfterDispose()
    {
        var native = new BackendStub(new SessionStub());
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        native.OnFactoryCall = () =>
        {
            factoryEntered.Set();
            Assert.True(releaseFactory.Wait(TimeSpan.FromSeconds(5)));
        };
        var backend = native.CreateBackend();
        var service = new WindowsAudioSessionService(backend, reconcileIntervalMs: 5000);
        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(5)));

        var dispose = Task.Run(service.Dispose);
        releaseFactory.Set();
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(typeof(WindowsAudioSessionService)
            .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service));
        Assert.False(backend.Initialize());
    }

    // Managed COM-interface doubles test backend control flow, not CLR RCW reference counting.
    private interface ITestSession : IAudioSessionControl, IAudioSessionControl2, ISimpleAudioVolume { }

    public class ComProxy : DispatchProxy
    {
        internal Func<string, object?[], int> Handler = null!;
        protected override object Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args!);
    }

    private static T Stub<T>(Func<string, object?[], int> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ComProxy>();
        ((ComProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed class SessionStub
    {
        public int ProcessIdHr;
        public uint ProcessId = 123456;
        public float Volume = 0.5f;
        public bool Muted;
        public int RegisterCount;
        public int UnregisterCount;
        public int VolumeWrites;
        public Action? OnVolumeWrite;
        public IAudioSessionControl Control { get; }
        public IAudioSessionEvents? Events { get; private set; }

        public SessionStub()
        {
            Control = Stub<ITestSession>((method, args) =>
            {
                switch (method)
                {
                    case "IsSystemSoundsSession": return 1;
                    case "GetProcessId": args[0] = ProcessId; return ProcessIdHr;
                    case "GetState": args[0] = AudioSessionState.AudioSessionStateActive; break;
                    case "GetSessionIdentifier": args[0] = "test-session"; break;
                    case "GetSessionInstanceIdentifier": args[0] = "test-instance"; break;
                    case "GetMasterVolume": args[0] = Volume; break;
                    case "GetMute": args[0] = Muted; break;
                    case "SetMasterVolume": OnVolumeWrite?.Invoke(); Volume = (float)args[0]!; VolumeWrites++; break;
                    case "SetMute": Muted = (bool)args[0]!; break;
                    case "RegisterAudioSessionNotification": Events = (IAudioSessionEvents)args[0]!; RegisterCount++; break;
                    case "UnregisterAudioSessionNotification": Interlocked.Increment(ref UnregisterCount); break;
                }
                return 0;
            });
        }
    }

    private sealed class BackendStub
    {
        public List<SessionStub> Sessions { get; }
        public int CountHr;
        public string? FailedMethod;
        public int FactoryCalls;
        public int EndpointUnregisters;
        public int ManagerRegisters;
        public int ManagerUnregisters;
        public int GetDefaultFailuresRemaining;
        public string EndpointId = "test-endpoint";
        public Action? OnFactoryCall;
        public IMMNotificationClient? EndpointNotification;
        public IAudioSessionNotification? SessionNotification;
        private readonly IMMDeviceEnumerator _enumerator;

        public BackendStub(params SessionStub[] sessions)
        {
            Sessions = sessions.ToList();
            var sessionEnum = Stub<IAudioSessionEnumerator>((method, args) =>
            {
                if (method == "GetCount") { args[0] = Sessions.Count; return CountHr; }
                args[1] = Sessions[(int)args[0]!].Control;
                return 0;
            });
            var manager = Stub<IAudioSessionManager2>((method, args) =>
            {
                if (method == FailedMethod) return EFail;
                if (method == "GetSessionEnumerator") args[0] = sessionEnum;
                if (method == "RegisterSessionNotification") { SessionNotification = (IAudioSessionNotification)args[0]!; ManagerRegisters++; }
                if (method == "UnregisterSessionNotification") ManagerUnregisters++;
                return 0;
            });
            var device = Stub<IMMDevice>((method, args) =>
            {
                if (method == FailedMethod) return EFail;
                if (method == "GetId") args[0] = EndpointId;
                if (method == "OpenPropertyStore") return EFail;
                if (method == "Activate") args[3] = manager;
                return 0;
            });
            _enumerator = Stub<IMMDeviceEnumerator>((method, args) =>
            {
                if (method == FailedMethod) return EFail;
                if (method == "RegisterEndpointNotificationCallback") EndpointNotification = (IMMNotificationClient)args[0]!;
                if (method == "GetDefaultAudioEndpoint")
                {
                    if (GetDefaultFailuresRemaining > 0) { GetDefaultFailuresRemaining--; return EFail; }
                    args[2] = device;
                }
                if (method == "UnregisterEndpointNotificationCallback") EndpointUnregisters++;
                return 0;
            });
        }

        public WindowsCoreAudioBackend CreateBackend() => new(null, () =>
        {
            FactoryCalls++;
            OnFactoryCall?.Invoke();
            return _enumerator;
        });
    }
}
