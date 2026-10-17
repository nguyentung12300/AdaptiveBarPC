using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AdaptiveBarPC.Interop;
using Xunit.Abstractions;

namespace AdaptiveBarPC.Tests;

public class TouchMouseFilterTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0x0200, 0xFF515780UL, -100, 950, true)]
    [InlineData(0x0200, 0xFF5157FFUL, -100, 950, true)]
    [InlineData(0x0200, 0UL, -100, 950, false)] // physical mouse
    [InlineData(0x0200, 0x80UL, -100, 950, false)] // unrelated injected input
    [InlineData(0x0200, 0xFF515700UL, -100, 950, false)] // pen
    [InlineData(0x0200, 0xFF515780UL, 100, 950, false)] // outside bar
    [InlineData(0x0200, 0xFF515780UL, -100, 899, false)]
    [InlineData(0x0200, 0xFF515780UL, -100, 1000, false)]
    [InlineData(0x0201, 0xFF515780UL, -100, 950, false)] // down still reaches WPF
    [InlineData(0x0202, 0xFF515780UL, -100, 950, false)] // up still reaches WPF
    public void OnlyTouchMovesWithinBarAreBlocked(int message, ulong extraInfo, int x, int y, bool expected)
    {
        Assert.Equal(expected, TouchMouseFilter.ShouldBlock(message, extraInfo, x, y, -1920, 900, 0, 1000));
    }

    [Fact]
    public void NativeHookCanBeInstalledAndDisposedTwice()
    {
        using var filter = new TouchMouseFilter(IntPtr.Zero);
        var hook = typeof(TouchMouseFilter).GetField("_hook", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotEqual(IntPtr.Zero, (IntPtr)hook.GetValue(filter)!);
        filter.Dispose();
        filter.Dispose();
        Assert.True(SpinWait.SpinUntil(() => (IntPtr)hook.GetValue(filter)! == IntPtr.Zero, 1000),
            "Disposing the filter must release its native hook.");
    }

    [Fact]
    public void TouchMovesAreFilteredWhileWindowThreadIsBusy()
    {
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        TouchMouseFilter? filter = null;
        Exception? failure = null;
        var windowThread = new Thread(() =>
        {
            try
            {
                // Oversized and invisible: covers mixed-DPI monitors without activating a window.
                using var window = new HwndSource(new HwndSourceParameters("Touch filter regression")
                {
                    WindowStyle = unchecked((int)0x80000000), // WS_POPUP, no WS_VISIBLE
                    PositionX = -16000,
                    PositionY = -16000,
                    Width = 32000,
                    Height = 32000
                });
                using var installed = new TouchMouseFilter(window.Handle);
                filter = installed;
                ready.Set();
                // STA WaitHandle waits may pump messages; this must model real UI work instead.
                SpinWait.SpinUntil(() => release.IsSet, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                failure = ex;
                ready.Set();
            }
        }) { IsBackground = true };
        windowThread.SetApartmentState(ApartmentState.STA);
        windowThread.Start();

        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)), "Filter did not start.");
            Assert.Null(failure);
            Assert.NotNull(filter);
            var before = filter.BlockedMoves;
            var input = new KeyboardInterop.INPUT
            {
                type = KeyboardInterop.INPUT_MOUSE,
                U = new KeyboardInterop.InputUnion
                {
                    mi = new KeyboardInterop.MOUSEINPUT
                    {
                        dx = 1,
                        dwFlags = 0x2001, // MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE
                        dwExtraInfo = new UIntPtr(0xFF515780u)
                    }
                }
            };
            // A nonzero delta is required for Windows to deliver a move. The filter consumes it.
            var elapsed = Stopwatch.StartNew();
            Assert.Equal(1u, KeyboardInterop.SendInput(1, [input], Marshal.SizeOf<KeyboardInterop.INPUT>()));
            var filtered = SpinWait.SpinUntil(() => filter.BlockedMoves > before, 500);
            output.WriteLine($"Hook responded: {filtered}; elapsed: {elapsed.Elapsed.TotalMilliseconds:F1} ms");
            Assert.True(filtered,
                "Mouse filtering waited for the window thread instead of processing input independently.");
        }
        finally
        {
            release.Set();
            Assert.True(windowThread.Join(TimeSpan.FromSeconds(5)), "Window thread did not stop.");
        }
    }
}
