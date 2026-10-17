using System.Diagnostics;
using System.Text;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using NativeWindow = AdaptiveBarPC.Interop.NativeWindow;

namespace AdaptiveBarPC.Services;

public sealed class ForegroundAppService : IDisposable
{
    private readonly AppLogger? _logger;
    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private ForegroundAppInfo? _lastContext;
    private bool _textInputActive;
    private int _timerTickActive;
    private bool _disposed;
    private IntPtr _ignoredHwnd = IntPtr.Zero;

    public event EventHandler<ForegroundAppInfo>? ContextChanged;
    public event Action<bool>? TextInputContextChanged;

    public ForegroundAppInfo? LastContext
    {
        get
        {
            lock (_lock)
                return _lastContext;
        }
    }

    public ForegroundAppService(AppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets an HWND (e.g. AdaptiveBar's own window) to ignore so foreground focus on AdaptiveBar does not switch profiles.
    /// </summary>
    public void SetIgnoredWindow(IntPtr hwnd)
    {
        _ignoredHwnd = hwnd;
    }

    /// <summary>
    /// Starts the background polling timer for foreground window changes.
    /// </summary>
    public void Start(int pollIntervalMs = 250)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _timer?.Dispose();
            var interval = Math.Clamp(pollIntervalMs, 50, 5000);
            _timer = new System.Threading.Timer(OnTimerTick, null, 0, interval);
        }
    }

    /// <summary>
    /// Stops the background polling timer.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnTimerTick(object? state)
    {
        if (Interlocked.Exchange(ref _timerTickActive, 1) != 0)
            return;

        try
        {
            CheckCurrent();
            CheckTextInputContext();
        }
        finally
        {
            Volatile.Write(ref _timerTickActive, 0);
        }
    }

    private void CheckTextInputContext()
    {
        var detected = TextInputContextDetector.Detect();
        if (detected is null)
            return;

        lock (_lock)
        {
            if (_textInputActive == detected.Value)
                return;

            _textInputActive = detected.Value;
        }

        try
        {
            TextInputContextChanged?.Invoke(detected.Value);
        }
        catch (Exception ex)
        {
            _logger?.Error("Exception in foreground text-input subscriber.", ex);
        }
    }

    /// <summary>
    /// Checks the current foreground window and raises ContextChanged if the effective application context has changed.
    /// </summary>
    public ForegroundAppInfo? CheckCurrent()
    {
        if (_disposed)
            return null;

        ForegroundAppInfo current;
        try
        {
            current = GetCurrent();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to query current foreground application.", ex);
            return null;
        }

        // Ignore AdaptiveBar's own process / window to prevent self-switching
        if (_ignoredHwnd != IntPtr.Zero && current.Hwnd == _ignoredHwnd)
            return null;

        if (current.ProcessName.Equals("AdaptiveBarPC", StringComparison.OrdinalIgnoreCase))
            return null;

        lock (_lock)
        {
            if (current.IsSameEffectiveContext(_lastContext))
                return _lastContext;

            _lastContext = current;
        }

        try
        {
            ContextChanged?.Invoke(this, current);
        }
        catch (Exception ex)
        {
            _logger?.Error("Exception in ForegroundAppService.ContextChanged subscriber.", ex);
        }

        return current;
    }

    /// <summary>
    /// Queries the current foreground window and constructs the full ForegroundAppInfo context.
    /// </summary>
    public ForegroundAppInfo GetCurrent()
    {
        var hwnd = NativeWindow.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return new ForegroundAppInfo(IntPtr.Zero, "", "", null, "Desktop", null, "Progman");

        NativeWindow.GetWindowThreadProcessId(hwnd, out var pid);

        string processName = "";
        string? exePath = null;

        if (pid > 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch
            {
                // Process may exit between calls.
            }

            exePath = AppMetadataService.TryGetExecutablePath((int)pid);
        }

        var titleBuffer = new StringBuilder(512);
        NativeWindow.GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
        var windowTitle = titleBuffer.ToString();

        var classBuffer = new StringBuilder(256);
        ProcessInterop.GetClassName(hwnd, classBuffer, classBuffer.Capacity);
        var windowClass = classBuffer.ToString();

        var (friendlyName, category) = AppMetadataService.Resolve(processName, exePath, windowClass);

        return new ForegroundAppInfo(
            hwnd,
            processName,
            windowTitle,
            exePath,
            friendlyName,
            category,
            windowClass);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
