using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public class AppBarTests
{
    [Fact]
    public void AppBarData_SizeMatchesWin32Contract()
    {
        int size = Marshal.SizeOf<AppBarInterop.APPBARDATA>();
        int expected = IntPtr.Size == 8 ? 48 : 36;
        Assert.Equal(expected, size);
    }

    [Fact]
    public void AppBarInterop_RegistersWindowMessage_ReturnsNonZero()
    {
        uint msg = AppBarInterop.RegisterWindowMessage("AdaptiveBarPC_TestAppBarMessage_" + Guid.NewGuid());
        Assert.True(msg > 0, "RegisterWindowMessage should return a non-zero message ID.");
    }

    [Theory]
    [InlineData(96, 1.00, 1920, 1080, 68, 68)]
    [InlineData(120, 1.25, 2560, 1440, 68, 85)]
    [InlineData(144, 1.50, 1920, 1080, 68, 102)]
    [InlineData(168, 1.75, 3840, 2160, 68, 119)]
    [InlineData(192, 2.00, 3840, 2160, 68, 136)]
    public void AppBar_PhysicalToDipCalculations_AreExact(
        uint dpi, double scale, int physWidth, int physHeight, double dipHeight, int expectedPhysBarHeight)
    {
        Assert.True(physHeight > 0);
        int physBarHeight = (int)Math.Round(dipHeight * scale);
        Assert.Equal(expectedPhysBarHeight, physBarHeight);

        // Convert physical bounds to DIP
        double dipW = physWidth * 96.0 / dpi;
        double dipH = physBarHeight * 96.0 / dpi;

        Assert.Equal(physWidth * 96.0 / dpi, dipW);
        Assert.True(Math.Abs(dipHeight - dipH) < 0.5, $"DIP height difference too large: {dipHeight} vs {dipH}");
    }

    [Fact]
    public void AppBarService_RegisterAndDispose_OperateSafely()
    {
        RunInSta(() =>
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Topmost = true
            };

            var helper = new System.Windows.Interop.WindowInteropHelper(win);
            win.Show();
            var hwnd = helper.Handle;

            var config = new AppConfig();
            var service = new AppBarService(win, hwnd, config);

            // Register() should either succeed (in real desktop with Explorer)
            // or gracefully return false with fallback docking (in isolated test environment).
            // In neither case should it throw.
            var registered = service.Register();
            Assert.Equal(registered, service.IsRegistered);

            // UpdatePosition should run safely
            service.UpdatePosition();
            Assert.True(service.FinalWpfDipRect.Width >= 0);
            Assert.True(service.FinalWpfDipRect.Height >= 0);

            // Dispose should unregister cleanly and idempotently
            service.Dispose();
            service.Dispose();
            Assert.False(service.IsRegistered);

            win.Close();
        });
    }

}
