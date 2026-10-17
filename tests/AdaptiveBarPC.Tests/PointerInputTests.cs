using System;
using AdaptiveBarPC.Interop;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class PointerInputTests
{
    [Theory]
    [InlineData(NativeWindow.WM_POINTERDOWN, true)]
    [InlineData(NativeWindow.WM_POINTERUPDATE, true)]
    [InlineData(NativeWindow.WM_POINTERUP, true)]
    [InlineData(NativeWindow.WM_POINTERACTIVATE, true)]
    [InlineData(NativeWindow.WM_POINTERENTER, true)]
    [InlineData(NativeWindow.WM_POINTERLEAVE, true)]
    [InlineData(NativeWindow.WM_POINTERCAPTURECHANGED, true)]
    [InlineData(NativeWindow.WM_NCPOINTERDOWN, true)]
    [InlineData(NativeWindow.WM_NCPOINTERUP, true)]
    [InlineData(NativeWindow.WM_NCPOINTERUPDATE, true)]
    [InlineData(0x0200 /* WM_MOUSEMOVE */, false)]
    [InlineData(0x0201 /* WM_LBUTTONDOWN */, false)]
    [InlineData(0x0202 /* WM_LBUTTONUP */, false)]
    [InlineData(NativeWindow.WM_MOUSEACTIVATE, false)]
    public void IsPointerMessage_FiltersCorrectly(int msg, bool expected)
    {
        Assert.Equal(expected, NativeWindow.IsPointerMessage(msg));
    }

    [Fact]
    public void PointerIdExtraction_ExtractsLowWord()
    {
        var wParam = new IntPtr(0x1234005A);
        Assert.Equal(0x005Au, NativeWindow.GetPointerId(wParam));
    }

    [Fact]
    public void PointerConstants_MatchWin32()
    {
        Assert.Equal(2u, NativeWindow.PT_TOUCH);
        Assert.Equal(3u, NativeWindow.PT_PEN);
        Assert.Equal(4u, NativeWindow.PT_MOUSE);
        Assert.Equal(3, NativeWindow.PA_NOACTIVATE);
        Assert.Equal(1, NativeWindow.PA_ACTIVATE);
    }

    [Theory]
    [InlineData(NativeWindow.WM_POINTERUP, true)]
    [InlineData(NativeWindow.WM_NCPOINTERUP, true)]
    [InlineData(NativeWindow.WM_POINTERLEAVE, true)]
    [InlineData(NativeWindow.WM_POINTERCAPTURECHANGED, true)]
    [InlineData(NativeWindow.WM_POINTERDOWN, false)]
    [InlineData(NativeWindow.WM_POINTERUPDATE, false)]
    public void IsTerminalPointerMessage_ClassifiesSequenceEnd(int msg, bool expected)
    {
        Assert.Equal(expected, NativeWindow.IsTerminalPointerMessage(msg));
    }



}


