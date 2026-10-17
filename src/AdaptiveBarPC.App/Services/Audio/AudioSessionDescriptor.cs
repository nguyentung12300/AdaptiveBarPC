using AdaptiveBarPC.Interop;

namespace AdaptiveBarPC.Services.Audio;

/// <summary>
/// Immutable or live record representing an underlying Windows Core Audio session.
/// </summary>
public sealed class AudioSessionDescriptor
{
    public string EndpointId { get; init; } = "";
    public string EndpointFriendlyName { get; init; } = "";
    public string SessionIdentifier { get; init; } = "";
    public string SessionInstanceIdentifier { get; init; } = "";
    public int ProcessId { get; init; }
    public string? ExecutablePath { get; init; }
    public string ProcessName { get; init; } = "";
    public string FriendlyAppName { get; init; } = "";
    public AudioSessionState State { get; set; } = AudioSessionState.AudioSessionStateActive;
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public bool IsSystemSound { get; init; }

    /// <summary>
    /// Programmatic callback to write volume to this specific underlying audio session.
    /// Range: 0.0f to 1.0f.
    /// </summary>
    public Action<float>? SetVolumeAction { get; init; }

    /// <summary>
    /// Programmatic callback to write mute state to this specific underlying audio session.
    /// </summary>
    public Action<bool>? SetMuteAction { get; init; }
}
