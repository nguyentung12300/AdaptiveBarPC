namespace AdaptiveBarPC.Services;

public interface IAudioProvider : IDisposable
{
    double GetMasterVolumePercent();
    void SetMasterVolumePercent(double percent);
    void ToggleMute();
    bool GetMute();
}
