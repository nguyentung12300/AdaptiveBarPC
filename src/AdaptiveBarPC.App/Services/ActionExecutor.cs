using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public sealed class ActionExecutor
{
    private const double MasterVolumeStep = 5.0;

    private readonly AppConfig _config;
    private readonly AppLogger _logger;
    private readonly IAudioProvider? _audioProvider;
    private readonly MediaSessionService? _mediaSession;
    private readonly Action<string> _clipboardWriter;
    private readonly Action<BitmapSource> _clipboardImageWriter;
    private readonly Func<KeyboardInterop.INPUT[], uint> _inputSender;
    private Action<string>? _pageNavigator;

    public ActionExecutor(
        AppConfig config,
        AppLogger logger,
        IAudioProvider? audioProvider = null,
        MediaSessionService? mediaSession = null)
        : this(config, logger, System.Windows.Clipboard.SetText, System.Windows.Clipboard.SetImage, SendNativeInputs, audioProvider, mediaSession)
    {
    }

    internal ActionExecutor(
        AppConfig config,
        AppLogger logger,
        Action<string> clipboardWriter,
        Func<KeyboardInterop.INPUT[], uint> inputSender,
        IAudioProvider? audioProvider = null,
        MediaSessionService? mediaSession = null)
        : this(config, logger, clipboardWriter, System.Windows.Clipboard.SetImage, inputSender, audioProvider, mediaSession)
    {
    }

    internal ActionExecutor(
        AppConfig config,
        AppLogger logger,
        Action<string> clipboardWriter,
        Action<BitmapSource> clipboardImageWriter,
        Func<KeyboardInterop.INPUT[], uint> inputSender,
        IAudioProvider? audioProvider = null,
        MediaSessionService? mediaSession = null)
    {
        _config = config;
        _logger = logger;
        _audioProvider = audioProvider;
        _mediaSession = mediaSession;
        _clipboardWriter = clipboardWriter;
        _clipboardImageWriter = clipboardImageWriter;
        _inputSender = inputSender;
    }

    internal void SetPageNavigator(Action<string> pageNavigator) => _pageNavigator = pageNavigator;

    public void Execute(ActionDefinition? action)
    {
        if (action is null || string.IsNullOrWhiteSpace(action.Type))
            return;

        try
        {
            switch (action.Type.Trim().ToLowerInvariant())
            {
                case "key":
                case "hotkey":
                    SendChord(action.Value ?? "");
                    break;

                case "media":
                    ExecuteMedia(action.Value ?? "");
                    break;

                case "mute":
                    ToggleMasterMute();
                    break;

                case "volume":
                    ExecuteVolume(action.Value ?? "");
                    break;

                case "launch":
                    Launch(action.Value ?? "");
                    break;

                case "url":
                    OpenUrl(action.Value ?? "");
                    break;

                case "page":
                    ExecutePage(action.Value ?? "");
                    break;

                case "text":
                    SendText(action.Value ?? "");
                    break;

                case "clipboard_text":
                    PasteClipboardText(action.Value ?? "");
                    break;

                case "clipboard_image":
                    PasteClipboardImage(action.Value ?? "");
                    break;

                case "shell":
                    RunShell(action.Value ?? "");
                    break;

                case "close_app":
                    System.Windows.Application.Current.Shutdown();
                    break;

                case "noop":
                    break;

                default:
                    _logger.Info($"Unknown action type: {action.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Action failed: {action.Type} / {action.Value}", ex);
        }
    }

    private void ExecuteMedia(string action)
    {
        switch (action.Trim().ToLowerInvariant())
        {
            case "play_pause" when _mediaSession is not null:
                _ = _mediaSession.PlayPauseAsync();
                break;
            case "next" when _mediaSession is not null:
                _ = _mediaSession.NextAsync();
                break;
            case "previous" when _mediaSession is not null:
                _ = _mediaSession.PreviousAsync();
                break;
            case "volume_up":
                AdjustMasterVolume(+MasterVolumeStep);
                break;
            case "volume_down":
                AdjustMasterVolume(-MasterVolumeStep);
                break;
            case "mute":
                ToggleMasterMute();
                break;
            default:
                SendMedia(action);
                break;
        }
    }

    private void ExecuteVolume(string action)
    {
        switch (action.Trim().ToLowerInvariant())
        {
            case "up":
            case "volume_up":
                AdjustMasterVolume(+MasterVolumeStep);
                break;
            case "down":
            case "volume_down":
                AdjustMasterVolume(-MasterVolumeStep);
                break;
        }
    }

    private void AdjustMasterVolume(double delta)
    {
        if (_audioProvider is null)
        {
            SendMedia(delta > 0 ? "volume_up" : "volume_down");
            return;
        }

        var current = _audioProvider.GetMasterVolumePercent();
        _audioProvider.SetMasterVolumePercent(Math.Clamp(current + delta, 0, 100));
    }

    private void ToggleMasterMute()
    {
        if (_audioProvider is not null)
            _audioProvider.ToggleMute();
        else
            SendMedia("mute");
    }

    private void ExecutePage(string target)
    {
        target = target.Trim();
        if (target.Length > 0)
            _pageNavigator?.Invoke(target);
    }

    /// <summary>
    /// URI schemes that could execute script or read arbitrary local files when
    /// handed to ShellExecute. These are blocked unconditionally because a shared
    /// profile must not be able to smuggle code execution through "launch".
    /// </summary>
    private static readonly string[] BlockedUriSchemes =
    {
        "javascript:",
        "vbscript:",
        "data:",
        "file:",
        "ms-msdt:",
        "search-ms:"
    };

    private void Launch(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        if (!_config.AllowLaunchActions)
        {
            _logger.Info("Blocked launch action because allowLaunchActions=false.");
            return;
        }

        var trimmed = target.Trim();

        foreach (var scheme in BlockedUriSchemes)
        {
            if (trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info($"Blocked launch action with disallowed URI scheme: {scheme}");
                return;
            }
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = trimmed,
            UseShellExecute = true
        });
    }

    private void OpenUrl(string target)
    {
        if (!Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.Info($"Ignored invalid URL action: {target}");
            return;
        }

        Launch(uri.AbsoluteUri);
    }

    private void RunShell(string command)
    {
        if (!_config.AllowShellCommands)
        {
            _logger.Info("Blocked shell action because allowShellCommands=false.");
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /s /c " + command,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void SendMedia(string action)
    {
        ushort vk = action.Trim().ToLowerInvariant() switch
        {
            "play_pause" => 0xB3,
            "next" => 0xB0,
            "previous" => 0xB1,
            "volume_up" => 0xAF,
            "volume_down" => 0xAE,
            "mute" => 0xAD,
            _ => 0
        };

        if (vk != 0)
            SendVirtualKeys([vk]);
    }

    private void SendChord(string chord)
    {
        var tokens = chord.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return;

        var keys = new List<ushort>(tokens.Length);

        foreach (var token in tokens)
        {
            var vk = MapKey(token);
            if (vk == 0)
            {
                // A single unknown token used to be silently dropped, which meant
                // "CTRL+FOO" degraded into a bare CTRL press. Fail loudly instead.
                _logger.Info($"Unknown key token '{token}' in chord '{chord}'. Chord aborted.");
                return;
            }

            keys.Add(vk);
        }

        if (keys.Count == 0)
            return;

        SendVirtualKeys(keys);
    }

    private void SendText(string text)
    {
        if (text.Length > 0)
            SendInputs(BuildUnicodeInputs(text));
    }

    private void PasteClipboardText(string text)
    {
        if (text.Length == 0)
            return;

        _clipboardWriter(text);
        SendChord("CTRL+V");
    }

    private void PasteClipboardImage(string path)
    {
        var image = StickerImageLoader.Load(path);
        if (image is null)
        {
            _logger.Info("Ignored invalid clipboard image; only PNG/JPEG up to 20 MB and 16 MP are supported.");
            return;
        }

        _clipboardImageWriter(image);
        SendChord("CTRL+V");
    }

    internal static KeyboardInterop.INPUT[] BuildUnicodeInputs(string text)
    {
        var inputs = new KeyboardInterop.INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = CreateUnicodeInput(text[i], false);
            inputs[(i * 2) + 1] = CreateUnicodeInput(text[i], true);
        }

        return inputs;
    }

    private void SendVirtualKeys(IReadOnlyList<ushort> keys)
    {
        var inputs = new List<KeyboardInterop.INPUT>();

        foreach (var key in keys)
            inputs.Add(CreateInput(key, false));

        for (var i = keys.Count - 1; i >= 0; i--)
            inputs.Add(CreateInput(keys[i], true));

        SendInputs(inputs.ToArray());
    }

    private void SendInputs(KeyboardInterop.INPUT[] array)
    {
        if (array.Length == 0)
            return;

        var sent = _inputSender(array);

        if (sent == 0)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.Error($"SendInput returned {sent}. Marshal.GetLastWin32Error()={error}");
        }
    }

    private static uint SendNativeInputs(KeyboardInterop.INPUT[] inputs) =>
        KeyboardInterop.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<KeyboardInterop.INPUT>());

    private static KeyboardInterop.INPUT CreateUnicodeInput(char value, bool keyUp)
    {
        return new KeyboardInterop.INPUT
        {
            type = KeyboardInterop.INPUT_KEYBOARD,
            U = new KeyboardInterop.InputUnion
            {
                ki = new KeyboardInterop.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = value,
                    dwFlags = KeyboardInterop.KEYEVENTF_UNICODE |
                              (keyUp ? KeyboardInterop.KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private static KeyboardInterop.INPUT CreateInput(ushort vk, bool keyUp)
    {
        return new KeyboardInterop.INPUT
        {
            type = KeyboardInterop.INPUT_KEYBOARD,
            U = new KeyboardInterop.InputUnion
            {
                ki = new KeyboardInterop.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KeyboardInterop.KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private static ushort MapKey(string key)
    {
        key = key.Trim().ToUpperInvariant();

        if (key.Length == 1)
        {
            var c = key[0];
            // Win32 VK codes for A–Z (0x41–0x5A) and 0–9 (0x30–0x39) are
            // identical to the ASCII/Unicode code point values for those characters,
            // so the char value can be used directly as a ushort VK code.
            if (c is >= 'A' and <= 'Z')
                return c;
            if (c is >= '0' and <= '9')
                return c;
        }

        if (key.StartsWith("F") &&
            int.TryParse(key[1..], out var f) &&
            f is >= 1 and <= 12)
        {
            return (ushort)(0x70 + (f - 1));
        }

        return key switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "ALT" => 0x12,
            "SHIFT" => 0x10,
            "WIN" or "WINDOWS" => 0x5B,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "GRAVE" or "BACKQUOTE" or "`" or "~" or "TILDE" => 0xC0,
            _ => 0
        };
    }
}
