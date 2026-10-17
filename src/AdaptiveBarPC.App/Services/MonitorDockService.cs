using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using NativeWindow = AdaptiveBarPC.Interop.NativeWindow;

namespace AdaptiveBarPC.Services;

public static class MonitorDockService
{
    /// <summary>
    /// Pre-configures window startup location and DIP dimensions before Show()
    /// so the HWND is created directly on the target monitor at its native DPI,
    /// preventing primary-monitor DPI inheritance and window scaling bugs.
    /// </summary>
    public static void PrepareWindow(Window window, AppConfig config)
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
            return;

        var screen = SelectScreen(screens, config.TargetMonitor);
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
            mi.rcWork = new MonitorInterop.RECT
            {
                left = screen.WorkingArea.Left,
                top = screen.WorkingArea.Top,
                right = screen.WorkingArea.Right,
                bottom = screen.WorkingArea.Bottom
            };
        }

        var bounds = config.UseWorkingArea ? mi.rcWork : mi.rcMonitor;
        int physX = bounds.left;
        int physY = bounds.top;
        int physWidth = bounds.Width;
        int physHeight = bounds.Height;

        uint dpiX = 96, dpiY = 96;
        int hr = MonitorInterop.GetDpiForMonitor(hMon, MonitorInterop.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
        if (hr != 0 || dpiX == 0)
            dpiX = dpiY = 96;

        double dipX = physX * 96.0 / dpiX;
        double dipY = physY * 96.0 / dpiY;
        double dipWidth = physWidth * 96.0 / dpiX;
        double dipHeight = physHeight * 96.0 / dpiY;

        double dipBarHeight = Math.Clamp(config.BarHeightPixels, AppConfig.MinimumBarHeight, Math.Max(AppConfig.MinimumBarHeight, (int)dipHeight));
        bool isTop = config.Position.Equals("top", StringComparison.OrdinalIgnoreCase);
        double dipBarTop = isTop ? dipY : (dipY + dipHeight - dipBarHeight);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = dipX;
        window.Top = dipBarTop;
        window.Width = dipWidth;
        window.Height = dipBarHeight;
    }

    public static void Dock(Window? window, IntPtr hwnd, AppConfig config, AppLogger? logger = null)
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
            return;

        var screen = SelectScreen(screens, config.TargetMonitor);

        // Query physical monitor handle using screen center point
        var pt = new MonitorInterop.POINT
        {
            x = screen.Bounds.Left + screen.Bounds.Width / 2,
            y = screen.Bounds.Top + screen.Bounds.Height / 2
        };
        var hMon = MonitorInterop.MonitorFromPoint(pt, MonitorInterop.MONITOR_DEFAULTTONEAREST);

        // Query monitor bounds and working area in physical pixels
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
            mi.rcWork = new MonitorInterop.RECT
            {
                left = screen.WorkingArea.Left,
                top = screen.WorkingArea.Top,
                right = screen.WorkingArea.Right,
                bottom = screen.WorkingArea.Bottom
            };
        }

        var bounds = config.UseWorkingArea ? mi.rcWork : mi.rcMonitor;
        int physX = bounds.left;
        int physY = bounds.top;
        int physWidth = bounds.Width;
        int physHeight = bounds.Height;

        // Query monitor DPI (effective DPI for PerMonitorV2)
        uint dpiX = 96, dpiY = 96;
        int hr = MonitorInterop.GetDpiForMonitor(hMon, MonitorInterop.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
        if (hr != 0 || dpiX == 0)
        {
            dpiX = MonitorInterop.GetDpiForWindow(hwnd);
            if (dpiX == 0) dpiX = 96;
            dpiY = dpiX;
        }

        double scaleX = dpiX / 96.0;
        double scaleY = dpiY / 96.0;

        // Convert physical bounds to WPF DIPs
        // Rule: dip = physical * 96.0 / dpi
        double dipX = physX * 96.0 / dpiX;
        double dipY = physY * 96.0 / dpiY;
        double dipWidth = physWidth * 96.0 / dpiX;
        double dipHeight = physHeight * 96.0 / dpiY;

        // Bar height: config.BarHeightPixels specifies height in WPF DIPs (minimum/default 72)
        double dipBarHeight = Math.Clamp(config.BarHeightPixels, AppConfig.MinimumBarHeight, Math.Max(AppConfig.MinimumBarHeight, (int)dipHeight));
        int physBarHeight = (int)Math.Round(dipBarHeight * scaleY);

        bool isTop = config.Position.Equals("top", StringComparison.OrdinalIgnoreCase);
        double dipBarTop = isTop ? dipY : (dipY + dipHeight - dipBarHeight);
        int physBarTop = isTop ? physY : (physY + physHeight - physBarHeight);

        // 1. Set WPF Window properties in DIPs so WPF layout engine uses exact DIP dimensions
        if (window != null)
        {
            window.Left = dipX;
            window.Top = dipBarTop;
            window.Width = dipWidth;
            window.Height = dipBarHeight;
        }

        // 2. Set Win32 HWND physical bounds without activation
        NativeWindow.SetWindowPos(
            hwnd,
            config.Topmost ? NativeWindow.HWND_TOPMOST : IntPtr.Zero,
            physX,
            physBarTop,
            physWidth,
            physBarHeight,
            NativeWindow.SWP_NOACTIVATE | NativeWindow.SWP_SHOWWINDOW);

        // 3. Log diagnostics as requested
        if (logger != null)
        {
            MonitorInterop.GetWindowRect(hwnd, out var winRect);
            var vDpi = window != null ? VisualTreeHelper.GetDpi(window) : default;

            logger.Info($"Target monitor: '{screen.DeviceName}' (primary={screen.Primary})");
            logger.Info($"1. Target monitor Bounds (physical): [{physX},{physY} - {physX + physWidth},{physY + physHeight}] ({physWidth}x{physHeight} px)");
            logger.Info($"2. Target monitor DPI: {dpiX}x{dpiY}");
            logger.Info($"3. DPI scale: {scaleX:F2} (dpiX/96={scaleX:F4}, dpiY/96={scaleY:F4})");
            if (window != null)
            {
                logger.Info($"4. MainWindow DIPs: Left={window.Left:F1}, Top={window.Top:F1}, Width={window.Width:F1}, Height={window.Height:F1}, ActualWidth={window.ActualWidth:F1}, ActualHeight={window.ActualHeight:F1}");
            }
            logger.Info($"5. HWND GetWindowRect (physical): [{winRect.left},{winRect.top} - {winRect.right},{winRect.bottom}] ({winRect.Width}x{winRect.Height} px)");
            if (window != null)
            {
                logger.Info($"6. VisualTreeHelper DPI: {vDpi.PixelsPerInchX:F0}x{vDpi.PixelsPerInchY:F0}, DpiScale={vDpi.DpiScaleX:F2}x{vDpi.DpiScaleY:F2}");

                // Also log post-layout values once WPF finishes its rendering pass
                window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    MonitorInterop.GetWindowRect(hwnd, out var postRect);
                    var postDpi = VisualTreeHelper.GetDpi(window);
                    logger.Info($"[Post-Layout] MainWindow ActualWidth={window.ActualWidth:F1}, ActualHeight={window.ActualHeight:F1} DIPs | HWND physical: {postRect.Width}x{postRect.Height} px | VisualTreeHelper DpiScale={postDpi.DpiScaleX:F2}x{postDpi.DpiScaleY:F2}");
                });
            }
        }
    }

    public static void Dock(IntPtr hwnd, AppConfig config)
    {
        Dock(null, hwnd, config, null);
    }

    internal static Screen SelectScreen(Screen[] screens, string target)
    {
        target = target.Trim();

        if (target.Equals("primary", StringComparison.OrdinalIgnoreCase))
            return screens.FirstOrDefault(s => s.Primary) ?? screens[0];

        if (target.Equals("secondary", StringComparison.OrdinalIgnoreCase))
            return screens.FirstOrDefault(s => !s.Primary) ??
                   screens.FirstOrDefault(s => s.Primary) ??
                   screens[0];

        if (target.StartsWith("index:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(target["index:".Length..], out var index) &&
            index >= 0 && index < screens.Length)
        {
            return screens[index];
        }

        return screens.FirstOrDefault(s =>
                   s.DeviceName.Contains(target, StringComparison.OrdinalIgnoreCase))
               ?? screens[0];
    }
}
