using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;

namespace AdaptiveBarPC.Tests;

public class MonitorDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public MonitorDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    [Fact]
    public void DumpMonitors()
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        _output.WriteLine($"System DPI: {GetDpiForSystem()}");

        _output.WriteLine("--- WinForms Screen.AllScreens ---");
        foreach (var s in Screen.AllScreens)
        {
            _output.WriteLine($"Device: {s.DeviceName}, Primary: {s.Primary}, Bounds: {s.Bounds}, WorkingArea: {s.WorkingArea}");
        }

        _output.WriteLine("--- Win32 EnumDisplayMonitors ---");
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(mi);
            GetMonitorInfo(hMon, ref mi);
            uint dpiX = 0, dpiY = 0;
            try { GetDpiForMonitor(hMon, 0, out dpiX, out dpiY); } catch { }
            bool isPrimary = (mi.dwFlags & 1) != 0;
            int width = mi.rcMonitor.right - mi.rcMonitor.left;
            int height = mi.rcMonitor.bottom - mi.rcMonitor.top;
            _output.WriteLine($"Device: {mi.szDevice}, Primary: {isPrimary}, MonitorRect: [{mi.rcMonitor.left},{mi.rcMonitor.top} - {mi.rcMonitor.right},{mi.rcMonitor.bottom}] ({width}x{height}), Dpi: {dpiX}x{dpiY}, Scale: {dpiX / 96.0:F2}");
            return true;
        }, IntPtr.Zero);
    }

    [Theory]
    [InlineData(1920, 1080, 96)]   // 100% scaling
    [InlineData(1920, 1080, 120)]  // 125% scaling
    [InlineData(1920, 1080, 144)]  // 150% scaling
    [InlineData(1920, 1080, 168)]  // 175% scaling
    [InlineData(1920, 1080, 192)]  // 200% scaling
    [InlineData(2560, 1440, 120)]  // 125% QHD
    [InlineData(3840, 2160, 192)]  // 200% 4K
    public void PhysicalToDip_Conversion_FitsExactMonitorWidth(int physWidth, int physHeight, uint dpi)
    {
        double scale = dpi / 96.0;
        double dipWidth = physWidth * 96.0 / dpi;
        double dipHeight = physHeight * 96.0 / dpi;

        // Verify that rendering dipWidth at this DPI scale equals physWidth exactly
        double renderedPhysWidth = dipWidth * scale;
        Assert.Equal((double)physWidth, renderedPhysWidth, 4);

        // Verify bar height DIP calculation doesn't clip
        double dipBarHeight = 68.0;
        int physBarHeight = (int)Math.Round(dipBarHeight * scale);
        double renderedBarHeightDip = physBarHeight / scale;
        Assert.True(renderedBarHeightDip >= 60.0);
    }
}
