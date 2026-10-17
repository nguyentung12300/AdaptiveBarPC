using System.Runtime.InteropServices;
using AdaptiveBarPC.Interop;

namespace AdaptiveBarPC.Services;

public sealed class WindowsAudioProvider : IAudioProvider
{
    private static readonly Guid EndpointVolumeGuid =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const int EndpointNotFound = unchecked((int)0x80070490);

    private readonly object _gate = new();
    private readonly Func<IMMDeviceEnumerator> _createEnumerator;
    private double _fallbackVolumePercent = 50.0;
    private bool _fallbackMuted;

    public WindowsAudioProvider()
        : this(() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject())
    {
    }

    // Keep fault injection at the COM boundary so endpoint-less machines can be tested.
    internal WindowsAudioProvider(Func<IMMDeviceEnumerator> createEnumerator)
    {
        _createEnumerator = createEnumerator;
    }

    public double GetMasterVolumePercent()
    {
        lock (_gate)
        {
            if (TryWithEndpoint(endpoint =>
                {
                    Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var scalar));
                    return Math.Clamp(scalar * 100.0, 0, 100);
                }, out double volume))
            {
                _fallbackVolumePercent = volume;
            }

            return _fallbackVolumePercent;
        }
    }

    public void SetMasterVolumePercent(double percent)
    {
        lock (_gate)
        {
            _fallbackVolumePercent = Math.Clamp(percent, 0, 100);
            var scalar = (float)(_fallbackVolumePercent / 100.0);
            _ = TryWithEndpoint(endpoint =>
            {
                Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(scalar, Guid.Empty));
                return 0;
            }, out int _);
        }
    }

    public void ToggleMute()
    {
        lock (_gate)
        {
            bool? requestedMute = null;
            var succeeded = TryWithEndpoint(endpoint =>
            {
                Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
                requestedMute = !muted;
                Marshal.ThrowExceptionForHR(endpoint.SetMute(requestedMute.Value, Guid.Empty));
                return requestedMute.Value;
            }, out bool mutedAfterToggle);

            _fallbackMuted = succeeded
                ? mutedAfterToggle
                : requestedMute ?? !_fallbackMuted;
        }
    }

    public bool GetMute()
    {
        lock (_gate)
        {
            if (TryWithEndpoint(endpoint =>
                {
                    Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
                    return muted;
                }, out bool muted))
            {
                _fallbackMuted = muted;
            }

            return _fallbackMuted;
        }
    }

    private bool TryWithEndpoint<T>(Func<IAudioEndpointVolume, T> action, out T result)
    {
        object? endpointObject = null;
        IMMDevice? device = null;
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = _createEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(
                EDataFlow.eRender, ERole.eMultimedia, out device));
            if (device is null)
            {
                result = default!;
                return false;
            }

            Marshal.ThrowExceptionForHR(device.Activate(
                EndpointVolumeGuid, CLSCTX.ALL, IntPtr.Zero, out endpointObject));
            result = action((IAudioEndpointVolume)endpointObject);
            return true;
        }
        catch (COMException ex) when (ex.ErrorCode == EndpointNotFound)
        {
            // No physical/default endpoint is normal on headless PCs and CI runners.
            result = default!;
            return false;
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
