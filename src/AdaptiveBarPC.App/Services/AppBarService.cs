using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using NativeWindow = AdaptiveBarPC.Interop.NativeWindow;

namespace AdaptiveBarPC.Services;

public sealed class AppBarService : IDisposable
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly AppConfig _config;
    private readonly AppLogger? _logger;
    private readonly uint _callbackMessage;

    private bool _isRegistered;
    private bool _isUpdatingPosition;
    private bool _disposed;

    public uint CallbackMessage => _callbackMessage;
    public bool IsRegistered => _isRegistered;

    internal MonitorInterop.RECT TargetMonitorRect { get; private set; }
    internal MonitorInterop.RECT ReservedAppBarRect { get; private set; }
    public Rect FinalWpfDipRect { get; private set; }

    public AppBarService(Window window, IntPtr hwnd, AppConfig config, AppLogger? logger = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _hwnd = hwnd;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        if (_hwnd == IntPtr.Zero)
            throw new ArgumentException("A valid WPF HWND is required before AppBar registration.", nameof(hwnd));

        _callbackMessage = AppBarInterop.RegisterWindowMessage("AdaptiveBarPC_AppBarCallback");
        if (_callbackMessage == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterWindowMessage failed for AdaptiveBarPC_AppBarCallback.");
    }

    /// <summary>
    /// Registers the window as a Windows AppBar with ABM_NEW and negotiates its position.
    /// </summary>
    public bool Register()
    {
        if (_isRegistered || _disposed)
            return _isRegistered;

        var screens = Screen.AllScreens;
        Screen? targetScreen = screens.Length > 0 ? MonitorDockService.SelectScreen(screens, _config.TargetMonitor) : null;
        IntPtr hMonTarget = IntPtr.Zero;
        var miBefore = new MonitorInterop.MONITORINFOEX { cbSize = Marshal.SizeOf<MonitorInterop.MONITORINFOEX>() };

        if (targetScreen != null)
        {
            var pt = new MonitorInterop.POINT
            {
                x = targetScreen.Bounds.Left + targetScreen.Bounds.Width / 2,
                y = targetScreen.Bounds.Top + targetScreen.Bounds.Height / 2
            };
            hMonTarget = MonitorInterop.MonitorFromPoint(pt, MonitorInterop.MONITOR_DEFAULTTONEAREST);
            MonitorInterop.GetMonitorInfo(hMonTarget, ref miBefore);
        }

        MonitorInterop.GetWindowRect(_hwnd, out var hwndRectBefore);

        // BEFORE ABM_NEW / ABM_SETPOS diagnostics
        if (_logger != null)
        {
            _logger.Info($"[AppBar-Diag] === BEFORE ABM_NEW / ABM_SETPOS ===");
            _logger.Info($"[AppBar-Diag] Target monitor: '{targetScreen?.DeviceName}' (primary={targetScreen?.Primary}) handle: 0x{hMonTarget.ToInt64():X8}");
            _logger.Info($"[AppBar-Diag] Target MONITORINFO.rcMonitor: [{miBefore.rcMonitor.left},{miBefore.rcMonitor.top} - {miBefore.rcMonitor.right},{miBefore.rcMonitor.bottom}] ({miBefore.rcMonitor.Width}x{miBefore.rcMonitor.Height} px)");
            _logger.Info($"[AppBar-Diag] Target MONITORINFO.rcWork:    [{miBefore.rcWork.left},{miBefore.rcWork.top} - {miBefore.rcWork.right},{miBefore.rcWork.bottom}] ({miBefore.rcWork.Width}x{miBefore.rcWork.Height} px)");
            _logger.Info($"[AppBar-Diag] Current HWND GetWindowRect:   [{hwndRectBefore.left},{hwndRectBefore.top} - {hwndRectBefore.right},{hwndRectBefore.bottom}] ({hwndRectBefore.Width}x{hwndRectBefore.Height} px)");
        }

        var abd = new AppBarInterop.APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<AppBarInterop.APPBARDATA>(),
            hWnd = _hwnd,
            uCallbackMessage = _callbackMessage
        };

        // Normalize any stale registration for this exact HWND before registering.
        // ABM_NEW returns FALSE both on failure and when the HWND is already registered,
        // so a one-time ABM_REMOVE here makes startup deterministic.
        var removeAbd = new AppBarInterop.APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<AppBarInterop.APPBARDATA>(),
            hWnd = _hwnd
        };
        AppBarInterop.SHAppBarMessage(AppBarInterop.ABM_REMOVE, ref removeAbd);

        var res = AppBarInterop.SHAppBarMessage(AppBarInterop.ABM_NEW, ref abd);
        _isRegistered = res != UIntPtr.Zero;

        if (_logger != null)
        {
            _logger.Info($"[AppBar-Diag] === AFTER ABM_NEW ===");
            _logger.Info($"[AppBar-Diag] ABM_NEW return value: {res} (UIntPtr)");
            _logger.Info($"[AppBar-Diag] Registered state: {_isRegistered}");
        }

        if (!_isRegistered)
        {
            _logger?.Error("[AppBar] ABM_NEW failed; AppBar positioning was not attempted.",
                new InvalidOperationException("SHAppBarMessage(ABM_NEW) returned FALSE."));
            return false;
        }

        UpdatePosition();
        return true;
    }

    /// <summary>
    /// Negotiates the AppBar's screen position with the Windows Shell (ABM_QUERYPOS / ABM_SETPOS),
    /// positions the Win32 HWND, synchronizes WPF DIP dimensions, and logs diagnostics.
    /// </summary>
    public void UpdatePosition()
    {
        if (_isUpdatingPosition || _disposed || !_isRegistered)
            return;

        _isUpdatingPosition = true;
        try
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 0)
                return;

            var screen = MonitorDockService.SelectScreen(screens, _config.TargetMonitor);

            // Locate monitor from physical screen center
            var pt = new MonitorInterop.POINT
            {
                x = screen.Bounds.Left + screen.Bounds.Width / 2,
                y = screen.Bounds.Top + screen.Bounds.Height / 2
            };
            var hMon = MonitorInterop.MonitorFromPoint(pt, MonitorInterop.MONITOR_DEFAULTTONEAREST);

            var mi = new MonitorInterop.MONITORINFOEX { cbSize = Marshal.SizeOf<MonitorInterop.MONITORINFOEX>() };
            if (!MonitorInterop.GetMonitorInfo(hMon, ref mi))
            {
                mi.rcMonitor = new MonitorInterop.RECT
                {
                    left = screen.Bounds.Left,
                    top = screen.Bounds.Top,
                    right = screen.Bounds.Right,
                    bottom = screen.Bounds.Bottom
                };
                mi.rcWork = mi.rcMonitor;
            }

            TargetMonitorRect = mi.rcMonitor;

            // Query native effective monitor DPI
            uint dpiX = 96, dpiY = 96;
            int hr = MonitorInterop.GetDpiForMonitor(hMon, MonitorInterop.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
            if (hr != 0 || dpiX == 0)
            {
                dpiX = MonitorInterop.GetDpiForWindow(_hwnd);
                if (dpiX == 0) dpiX = 96;
                dpiY = dpiX;
            }

            double scaleY = dpiY / 96.0;

            // Calculate physical bar height from DIP config
            double dipBarHeight = Math.Clamp(_config.BarHeightPixels, AppConfig.MinimumBarHeight, Math.Max(AppConfig.MinimumBarHeight, (int)(mi.rcMonitor.Height * 96.0 / dpiY)));
            int physBarHeight = (int)Math.Round(dipBarHeight * scaleY);

            bool isTop = _config.Position.Equals("top", StringComparison.OrdinalIgnoreCase);
            uint edge = isTop ? AppBarInterop.ABE_TOP : AppBarInterop.ABE_BOTTOM;

            // Initialize proposed physical bounding rectangle
            var abd = new AppBarInterop.APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<AppBarInterop.APPBARDATA>(),
                hWnd = _hwnd,
                uCallbackMessage = _callbackMessage,
                uEdge = edge,
                rc = new MonitorInterop.RECT
                {
                    left = mi.rcMonitor.left,
                    top = isTop ? mi.rcMonitor.top : (mi.rcMonitor.bottom - physBarHeight),
                    right = mi.rcMonitor.right,
                    bottom = isTop ? (mi.rcMonitor.top + physBarHeight) : mi.rcMonitor.bottom
                }
            };

            // 1. Query shell for approved position
            var resQuery = AppBarInterop.SHAppBarMessage(AppBarInterop.ABM_QUERYPOS, ref abd);
            if (_logger != null)
            {
                _logger.Info($"[AppBar-Diag] === AFTER ABM_QUERYPOS ===");
                _logger.Info($"[AppBar-Diag] Returned APPBARDATA.rc: [{abd.rc.left},{abd.rc.top} - {abd.rc.right},{abd.rc.bottom}] ({abd.rc.Width}x{abd.rc.Height} px) | res={resQuery}");
            }

            // 2. Fix the desired bar thickness along the docked edge
            if (isTop)
                abd.rc.bottom = abd.rc.top + physBarHeight;
            else
                abd.rc.top = abd.rc.bottom - physBarHeight;

            // 3. Commit position and reserve screen space
            var resSetPos = AppBarInterop.SHAppBarMessage(AppBarInterop.ABM_SETPOS, ref abd);
            if (_logger != null)
            {
                _logger.Info($"[AppBar-Diag] === AFTER ABM_SETPOS ===");
                _logger.Info($"[AppBar-Diag] Returned APPBARDATA.rc: [{abd.rc.left},{abd.rc.top} - {abd.rc.right},{abd.rc.bottom}] ({abd.rc.Width}x{abd.rc.Height} px) | res={resSetPos}");
            }

            ReservedAppBarRect = abd.rc;

            // 4. Convert shell-approved physical rectangle to WPF DIPs
            double dipX = abd.rc.left * 96.0 / dpiX;
            double dipY = abd.rc.top * 96.0 / dpiY;
            double dipWidth = abd.rc.Width * 96.0 / dpiX;
            double dipHeight = abd.rc.Height * 96.0 / dpiY;

            FinalWpfDipRect = new Rect(dipX, dipY, dipWidth, dipHeight);

            // 5. Update WPF Window layout properties in DIPs
            _window.Left = dipX;
            _window.Top = dipY;
            _window.Width = dipWidth;
            _window.Height = dipHeight;

            // 6. Set HWND physical bounds without activation
            NativeWindow.SetWindowPos(
                _hwnd,
                _config.Topmost ? NativeWindow.HWND_TOPMOST : IntPtr.Zero,
                abd.rc.left,
                abd.rc.top,
                abd.rc.Width,
                abd.rc.Height,
                NativeWindow.SWP_NOACTIVATE | NativeWindow.SWP_SHOWWINDOW);

            // 7. THEN IMPORTANTLY: call GetMonitorInfo(targetMonitor) AGAIN and log REAL rcWork returned by Windows
            var miAfter = new MonitorInterop.MONITORINFOEX { cbSize = Marshal.SizeOf<MonitorInterop.MONITORINFOEX>() };
            MonitorInterop.GetMonitorInfo(hMon, ref miAfter);

            // 8. Also log MonitorFromWindow and MonitorFromRect
            var hMonFromHwnd = MonitorInterop.MonitorFromWindow(_hwnd, MonitorInterop.MONITOR_DEFAULTTONEAREST);
            var hMonFromRect = MonitorInterop.MonitorFromRect(ref abd.rc, MonitorInterop.MONITOR_DEFAULTTONEAREST);
            bool hwndEqualsTarget = hMonFromHwnd == hMon;
            bool rectEqualsTarget = hMonFromRect == hMon;

            if (_logger != null)
            {
                _logger.Info($"[AppBar-Diag] === REAL rcWork AFTER ABM_SETPOS ===");
                _logger.Info($"[AppBar-Diag] Real target rcMonitor: [{miAfter.rcMonitor.left},{miAfter.rcMonitor.top} - {miAfter.rcMonitor.right},{miAfter.rcMonitor.bottom}] ({miAfter.rcMonitor.Width}x{miAfter.rcMonitor.Height} px)");
                _logger.Info($"[AppBar-Diag] Real target rcWork:    [{miAfter.rcWork.left},{miAfter.rcWork.top} - {miAfter.rcWork.right},{miAfter.rcWork.bottom}] ({miAfter.rcWork.Width}x{miAfter.rcWork.Height} px)");
                _logger.Info($"[AppBar-Diag] rcWork bottom delta:   {miAfter.rcMonitor.bottom - miAfter.rcWork.bottom} px (expected ~{physBarHeight} px)");
                _logger.Info($"[AppBar-Diag] MonitorFromWindow(AdaptiveBar HWND): 0x{hMonFromHwnd.ToInt64():X8} (equals intended DISPLAY2: {hwndEqualsTarget})");
                _logger.Info($"[AppBar-Diag] MonitorFromRect(AppBar rect):    0x{hMonFromRect.ToInt64():X8} (equals intended DISPLAY2: {rectEqualsTarget})");

                MonitorInterop.GetWindowRect(_hwnd, out var winRect);
                var vDpi = VisualTreeHelper.GetDpi(_window);
                _logger.Info($"[AppBar-Diag] MainWindow DIPs: Left={_window.Left:F1}, Top={_window.Top:F1}, Width={_window.Width:F1}, Height={_window.Height:F1}, ActualWidth={_window.ActualWidth:F1}, ActualHeight={_window.ActualHeight:F1}");
                _logger.Info($"[AppBar-Diag] HWND GetWindowRect (physical): [{winRect.left},{winRect.top} - {winRect.right},{winRect.bottom}] ({winRect.Width}x{winRect.Height} px)");
                _logger.Info($"[AppBar-Diag] VisualTreeHelper DPI: {vDpi.PixelsPerInchX:F0}x{vDpi.PixelsPerInchY:F0}, DpiScale={vDpi.DpiScaleX:F2}x{vDpi.DpiScaleY:F2}");
            }

        }
        finally
        {
            _isUpdatingPosition = false;
        }
    }

    /// <summary>
    /// Unregisters the AppBar from the Windows Shell, releasing the reserved screen space.
    /// </summary>
    public void Unregister()
    {
        if (!_isRegistered || _hwnd == IntPtr.Zero)
            return;

        var abd = new AppBarInterop.APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<AppBarInterop.APPBARDATA>(),
            hWnd = _hwnd
        };

        var res = AppBarInterop.SHAppBarMessage(AppBarInterop.ABM_REMOVE, ref abd);
        _isRegistered = false;

        if (_logger != null)
        {
            _logger.Info($"[AppBar-Diag] Unregistered (ABM_REMOVE res={res})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _disposed = true;
    }
}
