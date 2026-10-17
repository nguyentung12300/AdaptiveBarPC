using System.Runtime.InteropServices;
using AdaptiveBarPC.Interop;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace AdaptiveBarPC.Services;

public sealed class ClipboardHistoryService : IDisposable
{
    private const int MaxItems = 20;
    private const int MaxTextLength = 100_000;
    private readonly AppLogger _logger;
    private readonly List<string> _items = [];
    private IntPtr _hwnd;

    public ClipboardHistoryService(AppLogger logger) => _logger = logger;

    public IReadOnlyList<string> Items => _items;
    public event EventHandler? HistoryChanged;

    internal void Attach(IntPtr hwnd)
    {
        if (_hwnd == hwnd || hwnd == IntPtr.Zero)
            return;

        Detach();
        if (!ClipboardInterop.AddClipboardFormatListener(hwnd))
        {
            _logger.Warn($"Could not monitor clipboard. Win32 error={Marshal.GetLastWin32Error()}.");
            return;
        }

        _hwnd = hwnd;
        CaptureCurrentText();
    }

    internal void ProcessWindowMessage(int message)
    {
        if (message == ClipboardInterop.WM_CLIPBOARDUPDATE)
            CaptureCurrentText();
    }

    internal void RecordText(string? text)
    {
        // ponytail: text-only, bounded in-memory history; expand when file/image history is requested.
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            return;

        var existing = _items.FindIndex(item => string.Equals(item, text, StringComparison.Ordinal));
        if (existing == 0)
            return;
        if (existing > 0)
            _items.RemoveAt(existing);

        _items.Insert(0, text);
        if (_items.Count > MaxItems)
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CaptureCurrentText()
    {
        try
        {
            if (WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText))
                RecordText(WpfClipboard.GetText(WpfTextDataFormat.UnicodeText));
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not read clipboard text: {ex.Message}");
        }
    }

    internal void Detach()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        ClipboardInterop.RemoveClipboardFormatListener(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    public void Dispose() => Detach();
}
