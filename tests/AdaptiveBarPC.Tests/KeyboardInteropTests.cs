using System;
using System.Runtime.InteropServices;
using AdaptiveBarPC.Interop;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class KeyboardInteropTests
{
    [Fact]
    public void InputStruct_SizeAndOffsets_MatchWin32OnX64()
    {
        if (Environment.Is64BitProcess)
        {
            Assert.Equal(40, Marshal.SizeOf<KeyboardInterop.INPUT>());
            Assert.Equal(32, Marshal.SizeOf<KeyboardInterop.InputUnion>());
            Assert.Equal(32, Marshal.SizeOf<KeyboardInterop.MOUSEINPUT>());
            Assert.Equal(24, Marshal.SizeOf<KeyboardInterop.KEYBDINPUT>());
            Assert.Equal(8, Marshal.SizeOf<KeyboardInterop.HARDWAREINPUT>());

            Assert.Equal(0, (int)Marshal.OffsetOf<KeyboardInterop.InputUnion>("mi"));
            Assert.Equal(0, (int)Marshal.OffsetOf<KeyboardInterop.InputUnion>("ki"));
            Assert.Equal(0, (int)Marshal.OffsetOf<KeyboardInterop.InputUnion>("hi"));

            Assert.Equal(0, (int)Marshal.OffsetOf<KeyboardInterop.INPUT>("type"));
            Assert.Equal(8, (int)Marshal.OffsetOf<KeyboardInterop.INPUT>("U"));
        }
    }
}
