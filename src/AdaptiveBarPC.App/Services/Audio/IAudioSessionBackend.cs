namespace AdaptiveBarPC.Services.Audio;

/// <summary>
/// Small testable abstraction separating native Core Audio COM interaction from grouping/ordering.
/// </summary>
public interface IAudioSessionBackend : IDisposable
{
    string CurrentEndpointId { get; }
    string CurrentEndpointName { get; }
    IReadOnlyList<AudioSessionDescriptor> GetSessions();
    event EventHandler? SessionsChanged;
    bool Initialize();
}
