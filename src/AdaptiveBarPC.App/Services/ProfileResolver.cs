using System.IO;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public sealed class ProfileResolver
{
    /// <summary>
    /// Resolves the effective profile for the given foreground context based on the 4-tier priority:
    /// 1. Exact app profile (matches executable path / process name / window title)
    /// 2. Category profile (matches category: "browser", etc.)
    /// 3. Windows desktop / shell context -> Desktop profile
    /// 4. Non-desktop default profile
    /// 5. Generic runtime profile (backward-compatible fallback)
    /// </summary>
    public ProfileDefinition Resolve(
        ForegroundAppInfo context,
        IReadOnlyList<ProfileDefinition> availableProfiles)
    {
        // 1. Exact app profile (excluding default/desktop profile unless explicitly targeted)
        var exactMatch = availableProfiles
            .Where(p => !p.IsDefault && !p.Id.Equals("desktop", StringComparison.OrdinalIgnoreCase) && MatchesExact(p, context))
            .OrderByDescending(p => p.Priority)
            .FirstOrDefault();

        if (exactMatch != null)
            return exactMatch;

        // 2. Category profile
        if (!string.IsNullOrWhiteSpace(context.Category))
        {
            var categoryMatch = availableProfiles
                .Where(p => !p.IsDefault && !p.Id.Equals("desktop", StringComparison.OrdinalIgnoreCase) && MatchesCategory(p, context))
                .OrderByDescending(p => p.Priority)
                .FirstOrDefault();

            if (categoryMatch != null)
                return CreateContextualProfile(categoryMatch, context);
        }

        // 3. Desktop profile ONLY when the Windows desktop / shell is actually foreground
        if (IsDesktopOrShell(context))
        {
            var desktopProfile = availableProfiles
                .FirstOrDefault(p => p.Id.Equals("desktop", StringComparison.OrdinalIgnoreCase))
                ?? availableProfiles.FirstOrDefault(p => p.IsDefault);

            if (desktopProfile != null)
                return desktopProfile;

            return CreateDefaultDesktopProfile();
        }

        // 4. Editable fallback profile for non-desktop applications
        var fallbackProfile = availableProfiles
            .Where(p => p.IsDefault && !p.Id.Equals("desktop", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Priority)
            .FirstOrDefault();

        if (fallbackProfile != null)
            return CreateContextualProfile(fallbackProfile, context);

        // 5. Backward-compatible fallback when no editable app fallback exists
        return CreateGenericProfile(context);
    }

    private static ProfileDefinition CreateContextualProfile(
        ProfileDefinition source,
        ForegroundAppInfo context)
    {
        var displayName = !string.IsNullOrWhiteSpace(context.FriendlyAppName)
            ? context.FriendlyAppName
            : !string.IsNullOrWhiteSpace(context.ProcessName)
                ? FormatProcessName(context.ProcessName)
                : source.DisplayName;

        return new ProfileDefinition
        {
            Id = source.Id,
            DisplayName = displayName,
            Priority = source.Priority,
            IsDefault = source.IsDefault,
            Match = source.Match,
            Items = source.Items,
            Pages = source.Pages,
            FixedLeft = source.FixedLeft,
            FixedRight = source.FixedRight,
            Theme = source.Theme,
            Appearance = source.Appearance
        };
    }

    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    public static bool IsDesktopOrShell(ForegroundAppInfo context)
    {
        if (context.Hwnd == IntPtr.Zero)
            return true;

        if (string.IsNullOrWhiteSpace(context.ProcessName))
            return true;

        var proc = NormalizeProcess(context.ProcessName);
        if (proc.Equals("explorer", StringComparison.OrdinalIgnoreCase))
        {
            // Only actual shell/desktop window classes count as Desktop.
            // Normal File Explorer windows (CabinetWClass, ExploreWClass) and unknown explorer windows
            // must NOT resolve to Desktop.
            if (!string.IsNullOrWhiteSpace(context.WindowClass) &&
                ShellWindowClasses.Contains(context.WindowClass.Trim()))
            {
                return true;
            }

            // If WindowClass is empty, only match if title is explicitly "Program Manager"
            if (string.IsNullOrWhiteSpace(context.WindowClass) &&
                string.Equals(context.WindowTitle, "Program Manager", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Unknown explorer shell-like edge cases prefer Generic over incorrectly selecting Desktop
            return false;
        }

        return false;
    }

    private static bool MatchesExact(ProfileDefinition profile, ForegroundAppInfo context)
    {
        var match = profile.Match;
        if (match == null || (match.ProcessNames.Count == 0 && match.ExecutablePaths.Count == 0))
            return false;

        var procName = NormalizeProcess(context.ProcessName);
        var exeName = !string.IsNullOrWhiteSpace(context.ExecutablePath)
            ? NormalizeProcess(Path.GetFileName(context.ExecutablePath))
            : "";

        var processOk = match.ProcessNames.Any(p =>
        {
            var norm = NormalizeProcess(p);
            return norm.Equals(procName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(exeName) && norm.Equals(exeName, StringComparison.OrdinalIgnoreCase));
        });

        var pathOk = !string.IsNullOrWhiteSpace(context.ExecutablePath) &&
                     match.ExecutablePaths.Any(path =>
                         NormalizeExecutablePath(path).Equals(
                             NormalizeExecutablePath(context.ExecutablePath),
                             StringComparison.OrdinalIgnoreCase));

        if (!processOk && !pathOk)
            return false;

        if (!string.IsNullOrWhiteSpace(match.WindowTitleContains))
        {
            if (string.IsNullOrEmpty(context.WindowTitle) ||
                !context.WindowTitle.Contains(match.WindowTitleContains, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeExecutablePath(string path)
    {
        var trimmed = path.Trim();
        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }

    private static bool MatchesCategory(ProfileDefinition profile, ForegroundAppInfo context)
    {
        var match = profile.Match;
        if (match == null || string.IsNullOrWhiteSpace(context.Category))
            return false;

        var cat = context.Category.Trim();

        var categoryOk = false;
        if (!string.IsNullOrWhiteSpace(match.Category))
        {
            categoryOk = match.Category.Trim().Equals(cat, StringComparison.OrdinalIgnoreCase);
        }

        if (!categoryOk && match.Categories.Count > 0)
        {
            categoryOk = match.Categories.Any(c => c.Trim().Equals(cat, StringComparison.OrdinalIgnoreCase));
        }

        if (!categoryOk)
            return false;

        if (!string.IsNullOrWhiteSpace(match.WindowTitleContains))
        {
            if (string.IsNullOrEmpty(context.WindowTitle) ||
                !context.WindowTitle.Contains(match.WindowTitleContains, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static ProfileDefinition CreateGenericProfile(ForegroundAppInfo context)
    {
        var procName = NormalizeProcess(context.ProcessName);
        var displayName = !string.IsNullOrWhiteSpace(context.FriendlyAppName)
            ? context.FriendlyAppName
            : !string.IsNullOrWhiteSpace(procName)
                ? (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                    ? "File Explorer"
                    : FormatProcessName(procName))
                : "Unknown";

        var procKey = !string.IsNullOrWhiteSpace(context.ProcessName)
            ? NormalizeProcess(context.ProcessName).ToLowerInvariant()
            : "unknown";

        var profileId = $"generic:{procKey}";

        return new ProfileDefinition
        {
            Id = profileId,
            DisplayName = displayName,
            Priority = 0,
            IsDefault = false,
            Theme = new ThemeDefinition
            {
                Background = "#FF0E0E11",
                TileBackground = "#FF222228",
                Accent = "#FF7C5CFC",
                PrimaryText = "#E6F5F5F7",
                SecondaryText = "#80F5F5F7"
            },
            Pages = new List<PageDefinition>
            {
                new()
                {
                    Id = "main",
                    DisplayName = "Main",
                    Items = new List<ControlDefinition>
                    {
                        // Dynamic flexible-width Media widget in center (collapses when unavailable)
                        new()
                        {
                            Type = "media",
                            Label = "Media",
                            Width = 0,
                            CollapseWhenUnavailable = true,
                            AutoCollapse = true
                        },
                        // Master volume slider anchored on right via SystemItems
                        CreateVolumeControl(),
                        // Mute button anchored on right via SystemItems
                        CreateMuteControl()
                    }
                },
                CreateSystemPage(),
                CreateMixerPage()
            }
        };
    }

    public static ProfileDefinition CreateDefaultDesktopProfile()
    {
        return new ProfileDefinition
        {
            Id = "desktop",
            DisplayName = "Desktop",
            Priority = 0,
            IsDefault = true,
            Theme = new ThemeDefinition
            {
                Background = "#FF0E0E11",
                TileBackground = "#FF222228",
                Accent = "#FF7C5CFC",
                PrimaryText = "#E6F5F5F7",
                SecondaryText = "#80F5F5F7"
            },
            Pages = new List<PageDefinition>
            {
                new()
                {
                    Id = "main",
                    DisplayName = "Main",
                    Items = new List<ControlDefinition>
                    {
                        new()
                        {
                            Type = "button",
                            Label = "Explorer",
                            Width = 84,
                            Action = new ActionDefinition { Type = "launch", Value = "explorer.exe" }
                        },
                        new()
                        {
                            Type = "button",
                            Label = "Browser",
                            Width = 80,
                            Action = new ActionDefinition { Type = "launch", Value = "https://www.google.com" }
                        },
                        new()
                        {
                            Type = "spacer",
                            Width = 6
                        },
                        new()
                        {
                            Type = "media",
                            Label = "Media",
                            Width = 0
                        },
                        CreateVolumeControl(),
                        CreateMuteControl()
                    }
                },
                CreateSystemPage(),
                CreateMixerPage()
            }
        };
    }

    private static ControlDefinition CreateVolumeControl() => new()
    {
        Type = "slider",
        Label = "MASTER VOL",
        Width = 180,
        Min = 0,
        Max = 100,
        Action = new ActionDefinition { Type = "volume" },
        System = true
    };

    private static ControlDefinition CreateMuteControl() => new()
    {
        Type = "button",
        Label = "Mute",
        Width = 60,
        Action = new ActionDefinition { Type = "media", Value = "mute" },
        System = true
    };

    private static PageDefinition CreateSystemPage() => new()
    {
        Id = "system",
        DisplayName = "System",
        Items = new List<ControlDefinition>
        {
            new()
            {
                Type = "dynamiclist",
                Provider = "hardware",
                EmptyBehavior = "collapse",
                MaxItems = 6,
                Width = 0
            },
            CreateVolumeControl(),
            CreateMuteControl()
        }
    };

    private static PageDefinition CreateMixerPage() => new()
    {
        Id = "mixer",
        DisplayName = "Mixer",
        Items = new List<ControlDefinition>
        {
            new()
            {
                Type = "dynamiclist",
                Provider = "audioSessions",
                EmptyBehavior = "default",
                Width = 0
            },
            CreateVolumeControl(),
            CreateMuteControl()
        }
    };

    private static string FormatProcessName(string processName)
    {
        var cleaned = NormalizeProcess(processName);
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Unknown";

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
}
