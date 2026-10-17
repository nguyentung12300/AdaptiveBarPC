using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

/// <summary>Chooses an available audio backend while keeping unsupported machines usable.</summary>
public static class AudioProviderFactory
{
    public static IAudioProvider Create(AppConfig config, AppLogger logger)
    {
        var backend = config.AudioBackend?.Trim();
        if (string.Equals(backend, "windows", StringComparison.OrdinalIgnoreCase))
            return new WindowsAudioProvider();

        if (string.IsNullOrWhiteSpace(backend)
            || string.Equals(backend, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(backend, "voicemeeter", StringComparison.OrdinalIgnoreCase))
        {
            var voiceMeeter = new VoiceMeeterAudioProvider(config, logger);
            if (voiceMeeter.IsAvailable)
                return voiceMeeter;

            voiceMeeter.Dispose();
            logger.Info($"VoiceMeeter is unavailable for audio backend '{backend ?? "auto"}'. Falling back to Windows audio.");
            return new WindowsAudioProvider();
        }

        logger.Warn($"Unknown audio backend '{backend}'. Falling back to Windows audio.");
        return new WindowsAudioProvider();
    }
}
