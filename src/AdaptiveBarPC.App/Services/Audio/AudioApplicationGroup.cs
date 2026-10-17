using AdaptiveBarPC.Interop;

namespace AdaptiveBarPC.Services.Audio;

/// <summary>
/// Application-level grouped audio representation.
/// Groups multiple underlying Core Audio sessions belonging to the same application.
/// </summary>
public sealed class AudioApplicationGroup
{
    public string Id { get; init; } = "";
    public string ApplicationKey { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public string? ExecutablePath { get; init; }
    public string EndpointId { get; init; } = "";
    public string MembershipFingerprint { get; init; } = "";
    public IReadOnlyList<AudioSessionDescriptor> Sessions { get; init; } = Array.Empty<AudioSessionDescriptor>();
    public float EffectiveVolume { get; init; } = 1.0f;
    public bool IsMuted { get; init; }
    public bool IsForeground { get; init; }
    public AudioSessionState State { get; init; } = AudioSessionState.AudioSessionStateActive;

    public void SetVolume(float volume)
    {
        var clamped = Math.Clamp(volume, 0.0f, 1.0f);
        foreach (var session in Sessions)
        {
            try
            {
                session.SetVolumeAction?.Invoke(clamped);
            }
            catch
            {
                // Continue remaining sessions if one fails
            }
        }
    }

    public void SetMute(bool mute)
    {
        foreach (var session in Sessions)
        {
            try
            {
                session.SetMuteAction?.Invoke(mute);
            }
            catch
            {
                // Continue remaining sessions if one fails
            }
        }
    }
}
