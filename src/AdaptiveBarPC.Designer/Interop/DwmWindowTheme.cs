using System.Runtime.InteropServices;

namespace AdaptiveBarPC.Designer.Interop;

internal static class DwmWindowTheme
{
    // COLORREF is 0x00BBGGRR. Explicit colors override the Windows accent caption.
    // https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
    internal static int ApplyDarkCaption(IntPtr hwnd)
    {
        if (Set(hwnd, 20, 1) != 0) Set(hwnd, 19, 1);
        // Windows 10 ignores these unsupported attributes and keeps dark-mode chrome.
        Set(hwnd, 34, 0x00433B34); // border: #343B43
        var captionResult = Set(hwnd, 35, 0x00221D18); // caption: #181D22
        Set(hwnd, 36, 0x00F7F5F5); // text: #F5F5F7
        return captionResult;
    }

    private static int Set(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
