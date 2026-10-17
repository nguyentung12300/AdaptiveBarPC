using System.Runtime.InteropServices;

namespace AdaptiveBarPC.Interop;

internal static class AppBarInterop
{
    // AppBar message IDs
    public const uint ABM_NEW              = 0x00000000;
    public const uint ABM_REMOVE           = 0x00000001;
    public const uint ABM_QUERYPOS         = 0x00000002;
    public const uint ABM_SETPOS           = 0x00000003;
    public const uint ABM_GETSTATE         = 0x00000004;
    public const uint ABM_GETTASKBARPOS    = 0x00000005;
    public const uint ABM_ACTIVATE         = 0x00000006;
    public const uint ABM_GETAUTOHIDEBAR   = 0x00000007;
    public const uint ABM_SETAUTOHIDEBAR   = 0x00000008;
    public const uint ABM_WINDOWPOSCHANGED = 0x00000009;
    public const uint ABM_SETSTATE         = 0x0000000A;

    // AppBar screen edges
    public const uint ABE_LEFT   = 0;
    public const uint ABE_TOP    = 1;
    public const uint ABE_RIGHT  = 2;
    public const uint ABE_BOTTOM = 3;

    // Notification messages sent to callback
    public const int ABN_STATECHANGE   = 0x00000000;
    public const int ABN_POSCHANGED    = 0x00000001;
    public const int ABN_FULLSCREENAPP = 0x00000002;
    public const int ABN_WINDOWARRANGE = 0x00000003;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public MonitorInterop.RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);
}
