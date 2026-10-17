namespace AdaptiveBarPC.Models;

public sealed record ForegroundAppInfo(
    IntPtr Hwnd,
    string ProcessName,
    string WindowTitle,
    string? ExecutablePath = null,
    string? FriendlyAppName = null,
    string? Category = null,
    string? WindowClass = null)
{
    public IntPtr WindowHandle => Hwnd;

    /// <summary>
    /// Returns true if this context represents the same effective foreground application.
    /// Used to suppress redundant layout and profile evaluations.
    /// </summary>
    public bool IsSameEffectiveContext(ForegroundAppInfo? other)
    {
        if (other is null)
            return false;

        return Hwnd == other.Hwnd
            && string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(WindowTitle, other.WindowTitle, StringComparison.Ordinal)
            && string.Equals(FriendlyAppName, other.FriendlyAppName, StringComparison.Ordinal)
            && string.Equals(Category, other.Category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(WindowClass, other.WindowClass, StringComparison.Ordinal);
    }
}
