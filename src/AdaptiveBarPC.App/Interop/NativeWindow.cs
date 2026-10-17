using System.Runtime.InteropServices;
using System.Text;

namespace AdaptiveBarPC.Interop;

internal static class NativeWindow
{
    public const int GWL_EXSTYLE = -20;

    // Win32 extended window style flags (DWORD = uint in C#).
    public const uint WS_EX_NOACTIVATE  = 0x08000000u;
    public const uint WS_EX_TOOLWINDOW  = 0x00000080u;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;

    // Pointer input message IDs
    public const int WM_NCPOINTERUPDATE       = 0x0241;
    public const int WM_NCPOINTERDOWN         = 0x0242;
    public const int WM_NCPOINTERUP           = 0x0243;
    public const int WM_POINTERUPDATE         = 0x0245;
    public const int WM_POINTERDOWN           = 0x0246;
    public const int WM_POINTERUP             = 0x0247;
    public const int WM_POINTERENTER          = 0x0249;
    public const int WM_POINTERLEAVE          = 0x024A;
    public const int WM_POINTERACTIVATE       = 0x024B;
    public const int WM_POINTERCAPTURECHANGED = 0x024C;
    public const int WM_POINTERWHEEL          = 0x024E;
    public const int WM_POINTERHWHEEL         = 0x024F;

    // Pointer input types
    public const uint PT_POINTER  = 1;
    public const uint PT_TOUCH    = 2;
    public const uint PT_PEN      = 3;
    public const uint PT_MOUSE    = 4;
    public const uint PT_TOUCHPAD = 5;

    // Pointer activation flags
    public const int PA_ACTIVATE   = 1;
    public const int PA_NOACTIVATE = 3;

    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public static uint GetPointerId(IntPtr wParam) =>
        (uint)(wParam.ToInt64() & 0xFFFF);

    public static bool IsPointerMessage(int msg) =>
        msg is WM_NCPOINTERDOWN or WM_NCPOINTERUP or WM_NCPOINTERUPDATE
            or WM_POINTERDOWN or WM_POINTERUP or WM_POINTERUPDATE
            or WM_POINTERENTER or WM_POINTERLEAVE
            or WM_POINTERACTIVATE or WM_POINTERCAPTURECHANGED
            or WM_POINTERWHEEL or WM_POINTERHWHEEL;

    public static bool IsTerminalPointerMessage(int msg) =>
        msg is WM_POINTERUP or WM_NCPOINTERUP
            or WM_POINTERLEAVE or WM_POINTERCAPTURECHANGED;


    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetPointerType(uint pointerId, out uint pointerType);


    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(
        IntPtr hWnd,
        StringBuilder lpString,
        int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(
        IntPtr hWnd,
        int nIndex,
        int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    /// <summary>
    /// Applies WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW to the window's extended style.
    /// On 64-bit processes we use GetWindowLongPtr; on 32-bit, GetWindowLong.
    /// The style mask is uint in both paths — no unchecked cast required.
    /// </summary>
    public static void SetNoActivate(IntPtr hwnd)
    {
        const uint mask = WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

        if (IntPtr.Size == 8)
        {
            var style = (uint)GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
            style |= mask;
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr((long)style));
        }
        else
        {
            var style = (uint)GetWindowLong32(hwnd, GWL_EXSTYLE);
            style |= mask;
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)style);
        }
    }
}

