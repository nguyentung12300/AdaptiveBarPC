using System.Runtime.InteropServices;
using AdaptiveBarPC.Interop;

namespace AdaptiveBarPC.Services;

public sealed class WindowsAudioProvider : IAudioProvider
{
    private static readonly Guid EndpointVolumeGuid =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public double GetMasterVolumePercent() => WithEndpoint(endpoint =>
    {
        Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var scalar));
        return Math.Clamp(scalar * 100.0, 0, 100);
    });

    public void SetMasterVolumePercent(double percent) => WithEndpoint(endpoint =>
    {
        var scalar = (float)(Math.Clamp(percent, 0, 100) / 100.0);
        Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(scalar, Guid.Empty));
        return 0;
    });

    public void ToggleMute() => WithEndpoint(endpoint =>
    {
        Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
        Marshal.ThrowExceptionForHR(endpoint.SetMute(!muted, Guid.Empty));
        return 0;
    });

    public bool GetMute() => WithEndpoint(endpoint =>
    {
        Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
        return muted;
    });

    private static T WithEndpoint<T>(Func<IAudioEndpointVolume, T> action)
    {
        object? endpointObject = null;
        IMMDevice? device = null;
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(
                EDataFlow.eRender, ERole.eMultimedia, out device));
            Marshal.ThrowExceptionForHR(device.Activate(
                EndpointVolumeGuid, CLSCTX.ALL, IntPtr.Zero, out endpointObject));
            return action((IAudioEndpointVolume)endpointObject);
        }
        finally
        {
            ReleaseCom(endpointObject);
            ReleaseCom(device);
            ReleaseCom(enumerator);
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); }
            catch { }
        }
    }

    public void Dispose() { }
}
