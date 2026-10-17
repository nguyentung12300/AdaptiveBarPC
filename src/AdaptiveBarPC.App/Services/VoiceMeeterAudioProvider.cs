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

    private double _fallbackVolumePercent = 50.0;
    private bool _fallbackMuted;
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
            try
            {
                var res = _interop.Login();
                if (res == 0)
                    _logger.Info($"Connected to VoiceMeeter (controlling Bus[{_busIndex}]).");
                else if (res == 1)
                    _logger.Info($"Logged into VoiceMeeter API (VoiceMeeter GUI not currently open, controlling Bus[{_busIndex}]).");
                else
                    _logger.Info($"VoiceMeeter login returned {res}.");

                if (res >= 0)
                {
                    _interop.IsParametersDirty();
                    if (_interop.GetParameterFloat($"Bus[{_busIndex}].Gain", out var initialGainDb) == 0)
                    {
                        _fallbackVolumePercent = GainDbToPercent(initialGainDb);
                        _logger.Info($"Initial VoiceMeeter Bus[{_busIndex}].Gain: {initialGainDb:F2} dB ({_fallbackVolumePercent:F0}%)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"VoiceMeeter is unavailable; using in-memory audio state. {ex.Message}");
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
                            if (Math.Abs(remotePercent - _fallbackVolumePercent) < 1.0)
                            {
                                _lastWriteTime = DateTimeOffset.MinValue;
                                _fallbackVolumePercent = remotePercent;
                            }
                        }
                        else
                        {
                            _fallbackVolumePercent = remotePercent;
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

        try
        {
            if (!_interop.IsLoggedIn)
            {
                var res = _interop.Login();
                return res == 0 || res == 1;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to connect to VoiceMeeter.", ex);
            return false;
        }
    }

    /// <summary>True only while the VoiceMeeter API can read the configured bus.</summary>
    public bool IsAvailable
    {
        get
        {
            if (!EnsureConnected())
                return false;

            try
            {
                if (_interop.IsParametersDirty() < 0)
                    return false;
                return _interop.GetParameterFloat($"Bus[{_busIndex}].Gain", out _) == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public double GetMasterVolumePercent()
    {
        lock (_lock)
        {
            return _fallbackVolumePercent;
        }
    }

    public void SetMasterVolumePercent(double percent)
    {
        percent = Math.Clamp(percent, 0.0, 100.0);
        var targetDb = PercentToGainDb(percent);

        lock (_lock)
        {
            _fallbackVolumePercent = percent;
            _lastWriteTime = DateTimeOffset.UtcNow;
        }

        if (!EnsureConnected())
            return;

        try
        {
            var result = _interop.SetParameterFloat($"Bus[{_busIndex}].Gain", targetDb);
            if (result != 0)
                _logger.Warn($"VoiceMeeter rejected Bus[{_busIndex}].Gain update ({result}); keeping the requested value in memory.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set VoiceMeeter Bus[{_busIndex}].Gain to {percent}% ({targetDb} dB)", ex);
        }
    }

    public void ToggleMute()
    {
        if (!EnsureConnected())
        {
            lock (_lock)
                _fallbackMuted = !_fallbackMuted;
            return;
        }

        bool? requestedMute = null;
        try
        {
            _interop.IsParametersDirty();
            var res = _interop.GetParameterFloat($"Bus[{_busIndex}].Mute", out var muteVal);
            if (res == 0)
            {
                requestedMute = muteVal <= 0.5f;
                lock (_lock)
                    _fallbackMuted = requestedMute.Value;

                var setResult = _interop.SetParameterFloat($"Bus[{_busIndex}].Mute", requestedMute.Value ? 1.0f : 0.0f);
                if (setResult != 0)
                    _logger.Warn($"VoiceMeeter rejected Bus[{_busIndex}].Mute update ({setResult}); keeping the requested state in memory.");
            }
            else
                ToggleFallbackMute();
        }
        catch (Exception ex)
        {
            if (requestedMute.HasValue)
                SetFallbackMute(requestedMute.Value);
            else
                ToggleFallbackMute();
            _logger.Error($"Failed to toggle VoiceMeeter Bus[{_busIndex}].Mute", ex);
        }
    }

    public bool GetMute()
    {
        if (!EnsureConnected())
            return ReadFallbackMute();

        try
        {
            _interop.IsParametersDirty();
            var res = _interop.GetParameterFloat($"Bus[{_busIndex}].Mute", out var muteVal);
            if (res != 0)
                return ReadFallbackMute();

            var muted = muteVal > 0.5f;
            lock (_lock)
                _fallbackMuted = muted;
            return muted;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read VoiceMeeter Bus[{_busIndex}].Mute", ex);
            return ReadFallbackMute();
        }
    }

    private void ToggleFallbackMute()
    {
        lock (_lock)
            _fallbackMuted = !_fallbackMuted;
    }

    private void SetFallbackMute(bool muted)
    {
        lock (_lock)
            _fallbackMuted = muted;
    }

    private bool ReadFallbackMute()
    {
        lock (_lock)
            return _fallbackMuted;
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
