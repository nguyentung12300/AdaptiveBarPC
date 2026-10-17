using System.Runtime.InteropServices;

namespace AdaptiveBarPC.Interop;

internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

public enum AudioSessionState
{
    AudioSessionStateInactive = 0,
    AudioSessionStateActive = 1,
    AudioSessionStateExpired = 2
}

public enum AudioSessionDisconnectReason
{
    DisconnectReasonDeviceRemoval = 0,
    DisconnectReasonServerShutdown = 1,
    DisconnectReasonFormatChanged = 2,
    DisconnectReasonSessionLogoff = 3,
    DisconnectReasonSessionDisconnected = 4,
    DisconnectReasonExclusiveModeOverride = 5
}

[Flags]
internal enum CLSCTX : uint
{
    INPROC_SERVER = 0x1,
    INPROC_HANDLER = 0x2,
    LOCAL_SERVER = 0x4,
    REMOTE_SERVER = 0x10,
    ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid fmtid;
    public uint pid;

    public PropertyKey(Guid fmtid, uint pid)
    {
        this.fmtid = fmtid;
        this.pid = pid;
    }

    public static readonly PropertyKey PKEY_Device_FriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)]
    public ushort vt;

    [FieldOffset(2)]
    public ushort wReserved1;

    [FieldOffset(4)]
    public ushort wReserved2;

    [FieldOffset(6)]
    public ushort wReserved3;

    [FieldOffset(8)]
    public IntPtr pwszVal;

    [FieldOffset(8)]
    public int intVal;

    [FieldOffset(8)]
    public uint uintVal;

    // The native union includes a count plus a pointer: 16 bytes on x64, 8 on x86.
    [FieldOffset(8)]
    public CountedArray caub;

    [StructLayout(LayoutKind.Sequential)]
    public struct CountedArray
    {
        public uint cElems;
        public IntPtr pElems;
    }

    public string? GetString()
    {
        if (vt == 31 && pwszVal != IntPtr.Zero) // VT_LPWSTR
        {
            return Marshal.PtrToStringUni(pwszVal);
        }
        return null;
    }
}

internal static class PropVariantInterop
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        EDataFlow dataFlow,
        ERole role,
        out IMMDevice ppDevice);

    [PreserveSig]
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
        out IMMDevice ppDevice);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(
        IMMNotificationClient pClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(
        IMMNotificationClient pClient);
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint cProps);
    [PreserveSig]
    int GetAt(uint iProp, out PropertyKey pkey);
    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant pv);
    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant propvar);
    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
        CLSCTX dwClsCtx,
        IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(
        uint stgmAccess,
        out IPropertyStore ppProperties);

    [PreserveSig]
    int GetId(
        [MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    [PreserveSig]
    int GetState(
        out uint pdwState);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig]
    int GetChannelCount(out uint pnChannelCount);
    [PreserveSig]
    int SetMasterVolumeLevel(float fLevelDB, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
    [PreserveSig]
    int SetMasterVolumeLevelScalar(float fLevel, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
    [PreserveSig]
    int GetMasterVolumeLevel(out float pfLevelDB);
    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float pfLevel);
    [PreserveSig]
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
    [PreserveSig]
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    [PreserveSig]
    int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
    [PreserveSig]
    int VolumeStepUp([MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
    [PreserveSig]
    int VolumeStepDown([MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
    [PreserveSig]
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    [PreserveSig]
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig]
    int GetAudioSessionControl(
        [MarshalAs(UnmanagedType.LPStruct)] Guid audioSessionGuid,
        uint streamFlags,
        out IAudioSessionControl sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(
        [MarshalAs(UnmanagedType.LPStruct)] Guid audioSessionGuid,
        uint streamFlags,
        out ISimpleAudioVolume audioVolume);

    // IAudioSessionManager2
    [PreserveSig]
    int GetSessionEnumerator(
        out IAudioSessionEnumerator sessionEnum);

    [PreserveSig]
    int RegisterSessionNotification(
        IAudioSessionNotification newNotifications);

    [PreserveSig]
    int UnregisterSessionNotification(
        IAudioSessionNotification newNotifications);

    [PreserveSig]
    int RegisterDuckNotification(
        [MarshalAs(UnmanagedType.LPWStr)] string sessionId,
        IntPtr duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(
        IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int sessionCount);
    [PreserveSig]
    int GetSession(int sessionIndex, out IAudioSessionControl session);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState state);
    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);
    [PreserveSig]
    int SetGroupingParam([MarshalAs(UnmanagedType.LPStruct)] Guid groupingParam, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents newNotifications);
    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents newNotifications);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig]
    int GetState(out AudioSessionState state);
    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);
    [PreserveSig]
    int SetGroupingParam([MarshalAs(UnmanagedType.LPStruct)] Guid groupingParam, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents newNotifications);
    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents newNotifications);

    // IAudioSessionControl2
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);
    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);
    [PreserveSig]
    int GetProcessId(out uint processId);
    [PreserveSig]
    int IsSystemSoundsSession();
    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float fLevel, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int GetMasterVolume(out float pfLevel);
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

[ComImport]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string newDisplayName, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

    [PreserveSig]
    int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string newIconPath, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

    [PreserveSig]
    int OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

    [PreserveSig]
    int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

    [PreserveSig]
    int OnGroupingParamChanged([MarshalAs(UnmanagedType.LPStruct)] Guid newGroupingParam, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

    [PreserveSig]
    int OnStateChanged(AudioSessionState newState);

    [PreserveSig]
    int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
}

[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IAudioSessionControl newSession);
}
