using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using AdaptiveBarPC.Interop;

namespace AdaptiveBarPC.Services;

public static class AppMetadataService
{
    private static readonly ConcurrentDictionary<string, (string FriendlyName, string? Category)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
        "arc", "waterfox", "tor", "safari", "iexplore"
    };

    private static readonly HashSet<string> DevelopmentProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "devenv", "rider64", "rider", "idea64", "idea",
        "pycharm64", "pycharm", "webstorm64", "webstorm",
        "antigravity", "antigravity-ide", "antigravity_ide", "antigravityide"
    };

    private static readonly HashSet<string> CommunicationProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "teams", "ms-teams", "msteams", "slack", "telegram", "whatsapp"
    };

    /// <summary>
    /// Attempts to query the executable path for the given process ID without requiring elevated rights.
    /// </summary>
    public static string? TryGetExecutablePath(int processId)
    {
        if (processId <= 4)
            return null;

        try
        {
            var hProcess = ProcessInterop.OpenProcess(
                ProcessInterop.PROCESS_QUERY_LIMITED_INFORMATION,
                false,
                (uint)processId);

            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var buffer = new StringBuilder(1024);
                    var size = (uint)buffer.Capacity;
                    if (ProcessInterop.QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                    {
                        return buffer.ToString();
                    }
                }
                finally
                {
                    ProcessInterop.CloseHandle(hProcess);
                }
            }
        }
        catch
        {
            // Ignore failure, fall back to Process class.
        }

        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the friendly application name and category, using the in-memory cache to avoid repeated disk hits.
    /// </summary>
    public static (string FriendlyName, string? Category) Resolve(string processName, string? executablePath, string? windowClass = null)
    {
        var cacheKey = !string.IsNullOrWhiteSpace(executablePath)
            ? executablePath
            : processName;

        if (string.IsNullOrWhiteSpace(cacheKey))
            return ("Unknown", null);

        // Special handling for explorer.exe to distinguish File Explorer from Desktop/Shell
        if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(windowClass, "CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(windowClass, "ExploreWClass", StringComparison.OrdinalIgnoreCase))
            {
                return ("File Explorer", null);
            }

            if (string.Equals(windowClass, "Progman", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(windowClass, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(windowClass, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(windowClass, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                return ("Desktop", null);
            }

            return ("File Explorer", null);
        }

        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var friendlyName = ResolveFriendlyName(processName, executablePath);
        var category = ResolveCategory(processName, friendlyName);

        var result = (friendlyName, category);
        Cache[cacheKey] = result;
        return result;
    }

    private static string ResolveFriendlyName(string processName, string? executablePath)
    {
        // 1. Try FileDescription / ProductName from executable metadata
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            try
            {
                if (File.Exists(executablePath))
                {
                    var info = FileVersionInfo.GetVersionInfo(executablePath);
                    if (!string.IsNullOrWhiteSpace(info.FileDescription))
                        return CleanName(info.FileDescription);

                    if (!string.IsNullOrWhiteSpace(info.ProductName))
                        return CleanName(info.ProductName);
                }
            }
            catch
            {
                // Metadata extraction failed; fall through to filename
            }

            // 2. Executable filename without extension
            try
            {
                var fileStem = Path.GetFileNameWithoutExtension(executablePath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    return FormatStem(fileStem);
            }
            catch
            {
                // Fall through
            }
        }

        // 3. Known friendly name overrides when file metadata is absent
        var proc = NormalizeProcess(processName);
        if (proc.Equals("code", StringComparison.OrdinalIgnoreCase))
            return "Visual Studio Code";
        if (proc.Equals("devenv", StringComparison.OrdinalIgnoreCase))
            return "Visual Studio";
        if (proc.Equals("rider64", StringComparison.OrdinalIgnoreCase) || proc.Equals("rider", StringComparison.OrdinalIgnoreCase))
            return "JetBrains Rider";
        if (proc.Equals("idea64", StringComparison.OrdinalIgnoreCase) || proc.Equals("idea", StringComparison.OrdinalIgnoreCase))
            return "IntelliJ IDEA";
        if (proc.Equals("pycharm64", StringComparison.OrdinalIgnoreCase) || proc.Equals("pycharm", StringComparison.OrdinalIgnoreCase))
            return "PyCharm";
        if (proc.Equals("webstorm64", StringComparison.OrdinalIgnoreCase) || proc.Equals("webstorm", StringComparison.OrdinalIgnoreCase))
            return "WebStorm";
        if (proc.Equals("ms-teams", StringComparison.OrdinalIgnoreCase) || proc.Equals("msteams", StringComparison.OrdinalIgnoreCase) || proc.Equals("teams", StringComparison.OrdinalIgnoreCase))
            return "Microsoft Teams";
        if (proc.Equals("whatsapp", StringComparison.OrdinalIgnoreCase))
            return "WhatsApp";

        // 4. Process name fallback
        if (!string.IsNullOrWhiteSpace(processName))
            return FormatStem(processName);

        return "Unknown";
    }

    private static string? ResolveCategory(string processName, string friendlyName)
    {
        var proc = NormalizeProcess(processName);
        if (BrowserProcesses.Contains(proc))
            return "browser";

        if (DevelopmentProcesses.Contains(proc))
            return "development";

        if (CommunicationProcesses.Contains(proc))
            return "communication";

        if (friendlyName.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
            friendlyName.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
            friendlyName.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ||
            friendlyName.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase))
        {
            return "browser";
        }

        return null;
    }

    private static string CleanName(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4].Trim();
        return trimmed;
    }

    private static string FormatStem(string raw)
    {
        var cleaned = CleanName(raw);
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Unknown";

        // If it's all lower-case (e.g., "discord", "notepad", "unknown"), capitalize first letter
        if (cleaned.All(c => !char.IsLetter(c) || char.IsLower(c)))
        {
            return char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
        }

        return cleaned;
    }

    private static string NormalizeProcess(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    /// <summary>
    /// Clears the metadata cache (useful for testing).
    /// </summary>
    public static void ClearCache()
    {
        Cache.Clear();
    }
}
