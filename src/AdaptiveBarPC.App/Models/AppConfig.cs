namespace AdaptiveBarPC.Models;

public sealed class AppConfig
{
    public const int MinimumBarHeight = 72;

    public string TargetMonitor { get; set; } = "secondary";
    public int BarHeightPixels { get; set; } = MinimumBarHeight;
    public string Position { get; set; } = "bottom";
    public int PollIntervalMs { get; set; } = 350;
    public bool NoActivate { get; set; } = true;
    public bool Topmost { get; set; } = true;
    public bool UseWorkingArea { get; set; } = true;
    public bool AllowShellCommands { get; set; } = false;

    // v0.2 interaction defaults.
    public int LongPressMilliseconds { get; set; } = 650;
    public double SwipeThresholdPixels { get; set; } = 40;
    public bool EnableSwipePages { get; set; } = true;
    public bool EnableAnimations { get; set; } = true;

    // Audio backend configuration ("windows" | "voicemeeter")
    public string AudioBackend { get; set; } = "windows";
    public int VoiceMeeterBusIndex { get; set; } = 0;

    // Security: allow "launch" actions to open arbitrary targets from profile JSON.
    // Kept true for backward compatibility with existing profiles, but dangerous
    // URI schemes (javascript:, vbscript:, data:, file:) are always blocked.
    public bool AllowLaunchActions { get; set; } = true;
}
