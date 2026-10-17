using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AdaptiveBarPC.Interop;

// A low-level hook stalls mouse input system-wide if its thread is busy.
// Keep it on a dedicated message loop; never log or dispatch inside the hook.
internal sealed class TouchMouseFilter : IDisposable
{
    private readonly IntPtr _window;
    private readonly HookProc _callback;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hook;
    private int _blockedMoves;
    private int _disposed;

    public int BlockedMoves => Volatile.Read(ref _blockedMoves);

    public TouchMouseFilter(IntPtr window)
    {
        _window = window;
        _callback = OnMouseInput;
        var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher;
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                _hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, _callback, GetModuleHandle(null), 0);
                if (_hook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                started.SetException(ex);
                return;
            }

            started.SetResult(dispatcher);
            try
            {
                Dispatcher.Run();
            }
            finally
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
                GC.KeepAlive(_callback);
            }
        }) { IsBackground = true, Name = "AdaptiveBar touch mouse filter" };
        thread.Start();
        _dispatcher = started.Task.GetAwaiter().GetResult();
    }

    private static bool IsTouchMove(int message, ulong extraInfo) =>
        message == 0x0200 /* WM_MOUSEMOVE */ &&
        (extraInfo & 0xFFFFFF80UL) == 0xFF515780UL;

    internal static bool ShouldBlock(int message, ulong extraInfo, int x, int y,
        int left, int top, int right, int bottom) =>
        IsTouchMove(message, extraInfo) &&
        x >= left && x < right && y >= top && y < bottom;

    private IntPtr OnMouseInput(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message == new IntPtr(0x0200))
        {
            var input = Marshal.PtrToStructure<MouseInput>(data);
            if (IsTouchMove(message.ToInt32(), input.ExtraInfo.ToUInt64()) &&
                GetWindowRect(_window, out var area) &&
                ShouldBlock(message.ToInt32(), input.ExtraInfo.ToUInt64(), input.X, input.Y,
                    area.Left, area.Top, area.Right, area.Bottom))
            {
                Interlocked.Increment(ref _blockedMoves);
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint threadId);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
}
