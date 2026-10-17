using System.Threading;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public sealed class VoiceMeeterAudioProvider : IAudioProvider
{
    private readonly VoiceMeeterInterop _interop;
    private readonly AppLogger _logger;
    private readonly int _busIndex;
    private readonly object _lock = new();
    private readonly System.Threading.Timer? _pollTimer;

    private double _currentVolumePercent = 50.0;
    private DateTimeOffset _lastWriteTime = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPollTime = DateTimeOffset.MinValue;
    private bool _disposed;

    public VoiceMeeterAudioProvider(AppConfig config, AppLogger logger)
    {
        _logger = logger;
        _busIndex = Math.Clamp(config.VoiceMeeterBusIndex, 0, 7);
        _interop = new VoiceMeeterInterop();

        if (!_interop.IsLoaded)
        {
            _logger.Info("Voicemeeter remote DLL could not be loaded. VoiceMeeterAudioProvider will be inactive.");
        }
        else
        {
            var res = _interop.Login();
            if (res == 0)
                _logger.Info($"Connected to VoiceMeeter (controlling Bus[{_busIndex}]).");
            else if (res == 1)
                _logger.Info($"Logged into VoiceMeeter API (VoiceMeeter GUI not currently open, controlling Bus[{_busIndex}]).");
            else
                _logger.Info($"VoiceMeeter login returned {res}.");

            try
            {
                _interop.IsParametersDirty();
                if (_interop.GetParameterFloat($"Bus[{_busIndex}].Gain", out var initialGainDb) == 0)
                {
                    _currentVolumePercent = GainDbToPercent(initialGainDb);
                    _logger.Info($"Initial VoiceMeeter Bus[{_busIndex}].Gain: {initialGainDb:F2} dB ({_currentVolumePercent:F0}%)");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed reading initial VoiceMeeter gain.", ex);
            }

            _pollTimer = new System.Threading.Timer(OnPollTimer, null, 150, 150);
        }
    }

    private void OnPollTimer(object? state)
    {
        if (_disposed || !EnsureConnected())
            return;

        try
        {
            int dirty = _interop.IsParametersDirty();
            var now = DateTimeOffset.UtcNow;

            if (dirty == 1 || (now - _lastPollTime).TotalMilliseconds >= 1000)
            {
                _lastPollTime = now;
                if (_interop.GetParameterFloat($"Bus[{_busIndex}].Gain", out var gainDb) == 0)
                {
                    var remotePercent = GainDbToPercent(gainDb);

                    lock (_lock)
                    {
                        if ((now - _lastWriteTime).TotalMilliseconds < 1000)
                        {
                            if (Math.Abs(remotePercent - _currentVolumePercent) < 1.0)
                            {
                                _lastWriteTime = DateTimeOffset.MinValue;
                                _currentVolumePercent = remotePercent;
                            }
                        }
                        else
                        {
                            _currentVolumePercent = remotePercent;
                        }
                    }
                }
            }
        }
        catch
        {
            // Background polling must never throw
        }
    }

    private bool EnsureConnected()
    {
        if (_disposed || !_interop.IsLoaded)
            return false;

        if (!_interop.IsLoggedIn)
        {
            var res = _interop.Login();
            return res == 0 || res == 1;
        }

        return true;
    }

    public double GetMasterVolumePercent()
    {
        lock (_lock)
        {
            return _currentVolumePercent;
        }
    }

    public void SetMasterVolumePercent(double percent)
    {
        if (!EnsureConnected())
            return;

        percent = Math.Clamp(percent, 0.0, 100.0);
        var targetDb = PercentToGainDb(percent);

        lock (_lock)
        {
            _currentVolumePercent = percent;
            _lastWriteTime = DateTimeOffset.UtcNow;
        }

        try
        {
            _interop.SetParameterFloat($"Bus[{_busIndex}].Gain", targetDb);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set VoiceMeeter Bus[{_busIndex}].Gain to {percent}% ({targetDb} dB)", ex);
        }
    }

    public void ToggleMute()
    {
        if (!EnsureConnected())
            return;

        try
        {
            _interop.IsParametersDirty();
            var res = _interop.GetParameterFloat($"Bus[{_busIndex}].Mute", out var muteVal);
            if (res == 0)
            {
                var newMute = muteVal > 0.5f ? 0.0f : 1.0f;
                _interop.SetParameterFloat($"Bus[{_busIndex}].Mute", newMute);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to toggle VoiceMeeter Bus[{_busIndex}].Mute", ex);
        }
    }

    public bool GetMute()
    {
        if (!EnsureConnected())
            return false;

        try
        {
            _interop.IsParametersDirty();
            var res = _interop.GetParameterFloat($"Bus[{_busIndex}].Mute", out var muteVal);
            return res == 0 && muteVal > 0.5f;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read VoiceMeeter Bus[{_busIndex}].Mute", ex);
            return false;
        }
    }

    private static double GainDbToPercent(float gainDb)
    {
        if (gainDb <= -59.5f)
            return 0.0;
        if (gainDb >= 0.0f)
            return 100.0;

        var percent = Math.Pow(10.0, gainDb / 20.0) * 100.0;
        return Math.Round(Math.Clamp(percent, 0.0, 100.0), 2);
    }

    private static float PercentToGainDb(double percent)
    {
        if (percent <= 0.001)
            return -60.0f;
        if (percent >= 100.0)
            return 0.0f;

        return (float)Math.Clamp(20.0 * Math.Log10(percent / 100.0), -60.0, 0.0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pollTimer?.Dispose();
        _interop.Dispose();
    }
}
