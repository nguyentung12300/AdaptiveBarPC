using System.IO;
using System.Text.Json;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public class ProfileResolverTests
{
    private readonly ProfileResolver _resolver = new();
    private readonly List<ProfileDefinition> _profiles;

    public ProfileResolverTests()
    {
        var baseDir = AppContext.BaseDirectory;
        var profilesDir = Path.Combine(baseDir, "Profiles");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        _profiles = new List<ProfileDefinition>();
        if (Directory.Exists(profilesDir))
        {
            foreach (var file in Directory.EnumerateFiles(profilesDir, "*.json").OrderBy(f => f))
            {
                var text = File.ReadAllText(file);
                var p = JsonSerializer.Deserialize<ProfileDefinition>(text, options);
                if (p != null)
                    _profiles.Add(p);
            }
        }

        // Ensure we at least have desktop and browser in tests
        if (!_profiles.Any(p => p.Id == "desktop"))
            _profiles.Add(ProfileResolver.CreateDefaultDesktopProfile());

        if (!_profiles.Any(p => p.Id == "browser"))
        {
            _profiles.Add(new ProfileDefinition
            {
                Id = "browser",
                DisplayName = "Browser",
                Priority = 50,
                Match = new MatchDefinition
                {
                    ProcessNames = new List<string> { "chrome", "msedge", "brave", "browser", "opera" },
                    Category = "browser"
                }
            });
        }

        if (!_profiles.Any(p => p.Id == "development"))
        {
            _profiles.Add(new ProfileDefinition
            {
                Id = "development",
                DisplayName = "Development",
                Priority = 20,
                Match = new MatchDefinition { Category = "development" },
                Pages = new List<PageDefinition>
                {
                    new()
                    {
                        Id = "main",
                        Items = new List<ControlDefinition>
                        {
                            new() { Type = "button", Label = "Terminal", Width = 84 },
                            new() { Type = "button", Label = "Command Palette", Width = 136 },
                            new() { Type = "media", Label = "Media", Width = 0, CollapseWhenUnavailable = true },
                            new() { Type = "slider", Label = "MASTER VOL", Width = 180, System = true, Action = new ActionDefinition { Type = "volume" } },
                            new() { Type = "button", Label = "Mute", Width = 60, System = true, Action = new ActionDefinition { Type = "media", Value = "mute" } }
                        }
                    }
                }
            });
        }

        if (!_profiles.Any(p => p.Id == "communication"))
        {
            _profiles.Add(new ProfileDefinition
            {
                Id = "communication",
                DisplayName = "Communication",
                Priority = 20,
                Match = new MatchDefinition { Category = "communication" },
                Pages = new List<PageDefinition>
                {
                    new()
                    {
                        Id = "main",
                        Items = new List<ControlDefinition>
                        {
                            new() { Type = "media", Label = "Media", Width = 0, CollapseWhenUnavailable = true },
                            new() { Type = "slider", Label = "MASTER VOL", Width = 180, System = true, Action = new ActionDefinition { Type = "volume" } },
                            new() { Type = "button", Label = "Mute", Width = 60, System = true, Action = new ActionDefinition { Type = "media", Value = "mute" } }
                        }
                    }
                }
            });
        }
    }

    [Fact]
    public void Explorer_With_Progman_Resolves_Desktop()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x1001,
            "explorer.exe",
            "Program Manager",
            @"C:\Windows\explorer.exe",
            "Desktop",
            null,
            "Progman");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.Equal("desktop", profile.Id);
    }

    [Fact]
    public void Explorer_With_WorkerW_Resolves_Desktop()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x1002,
            "explorer.exe",
            "",
            @"C:\Windows\explorer.exe",
            "Desktop",
            null,
            "WorkerW");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.Equal("desktop", profile.Id);
    }

    [Fact]
    public void Explorer_With_CabinetWClass_DoesNotResolveDesktop_UsesOtherApps()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x1003,
            "explorer.exe",
            "Documents",
            @"C:\Windows\explorer.exe",
            "File Explorer",
            null,
            "CabinetWClass");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.NotEqual("desktop", profile.Id);
        Assert.Equal("other", profile.Id);
        Assert.Equal("File Explorer", profile.DisplayName);
    }

    [Fact]
    public void Explorer_With_ExploreWClass_DoesNotResolveDesktop_UsesOtherApps()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x1004,
            "explorer.exe",
            "Downloads",
            @"C:\Windows\explorer.exe",
            "File Explorer",
            null,
            "ExploreWClass");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.NotEqual("desktop", profile.Id);
        Assert.Equal("other", profile.Id);
        Assert.Equal("File Explorer", profile.DisplayName);
    }

    [Fact]
    public void Explorer_With_UnknownShellLikeEdgeCase_PrefersOtherAppsOverDesktop()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x1005,
            "explorer.exe",
            "Custom Shell View",
            @"C:\Windows\explorer.exe",
            null,
            null,
            "CustomExplorerClass");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.NotEqual("desktop", profile.Id);
        Assert.Equal("other", profile.Id);
        Assert.Equal("Explorer", profile.DisplayName);
    }

    [Fact]
    public void Desktop_Resolves_DesktopProfile_WhenDesktopOrShell()
    {
        var desktopContext1 = new ForegroundAppInfo(
            (IntPtr)0x1234,
            "explorer",
            "Program Manager",
            null,
            "Desktop",
            null,
            "Progman");

        var profile1 = _resolver.Resolve(desktopContext1, _profiles);
        Assert.Equal("desktop", profile1.Id);

        // Hwnd Zero (no active window / clicked desktop background)
        var desktopContext2 = new ForegroundAppInfo(
            IntPtr.Zero,
            "",
            "",
            null,
            "Desktop",
            null);

        var profile2 = _resolver.Resolve(desktopContext2, _profiles);
        Assert.Equal("desktop", profile2.Id);
    }

    [Fact]
    public void Chrome_And_Edge_Resolve_BrowserProfile_ViaExactMatch()
    {
        var chromeContext = new ForegroundAppInfo(
            (IntPtr)0x2001,
            "chrome.exe",
            "Google Chrome",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            "Google Chrome",
            "browser");

        var chromeProfile = _resolver.Resolve(chromeContext, _profiles);
        Assert.Equal("browser", chromeProfile.Id);

        var edgeContext = new ForegroundAppInfo(
            (IntPtr)0x2002,
            "msedge",
            "Microsoft Edge",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "Microsoft Edge",
            "browser");

        var edgeProfile = _resolver.Resolve(edgeContext, _profiles);
        Assert.Equal("browser", edgeProfile.Id);
    }

    [Fact]
    public void Firefox_Resolves_BrowserProfile_ViaCategory()
    {
        // Firefox is not listed in processNames of 10-browser.json, but has Category="browser"
        var firefoxContext = new ForegroundAppInfo(
            (IntPtr)0x2003,
            "firefox.exe",
            "Mozilla Firefox",
            @"C:\Program Files\Mozilla Firefox\firefox.exe",
            "Firefox",
            "browser");

        var profile = _resolver.Resolve(firefoxContext, _profiles);
        Assert.Equal("browser", profile.Id);
    }

    [Fact]
    public void Code_Resolves_DevelopmentProfile_WithFriendlyName()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x8001,
            "Code.exe",
            "index.ts - project - Visual Studio Code",
            @"C:\Program Files\Microsoft VS Code\Code.exe",
            "Visual Studio Code",
            "development");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.Equal("development", profile.Id);
        Assert.Equal("Visual Studio Code", profile.DisplayName);
    }

    [Fact]
    public void Devenv_Resolves_DevelopmentProfile_WithFriendlyName()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x8002,
            "devenv.exe",
            "Solution - Microsoft Visual Studio",
            @"C:\Program Files\Microsoft Visual Studio\devenv.exe",
            "Visual Studio",
            "development");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.Equal("development", profile.Id);
        Assert.Equal("Visual Studio", profile.DisplayName);
    }

    [Fact]
    public void Rider_Resolves_DevelopmentProfile_WithFriendlyName()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x8003,
            "rider64.exe",
            "Solution - JetBrains Rider",
            @"C:\Program Files\JetBrains\Rider\rider64.exe",
            "JetBrains Rider",
            "development");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.Equal("development", profile.Id);
        Assert.Equal("JetBrains Rider", profile.DisplayName);
    }

    [Fact]
    public void Discord_Resolves_CommunicationProfile_WithFriendlyName()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x8004,
            "Discord.exe",
            "Discord",
            @"C:\Users\User\AppData\Local\Discord\Discord.exe",
            "Discord",
            "communication");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.Equal("communication", profile.Id);
        Assert.Equal("Discord", profile.DisplayName);
    }

    [Fact]
    public void Slack_Resolves_CommunicationProfile_WithFriendlyName()
    {
        var context = new ForegroundAppInfo(
            (IntPtr)0x8005,
            "Slack.exe",
            "Workspace - Slack",
            @"C:\Users\User\AppData\Local\slack\slack.exe",
            "Slack",
            "communication");

        var profile = _resolver.Resolve(context, _profiles);
        Assert.Equal("communication", profile.Id);
        Assert.Equal("Slack", profile.DisplayName);
    }

    [Fact]
    public void Teams_And_MsTeams_Resolve_CommunicationProfile()
    {
        var context1 = new ForegroundAppInfo(
            (IntPtr)0x8006, "Teams.exe", "Microsoft Teams", null, "Microsoft Teams", "communication");
        Assert.Equal("communication", _resolver.Resolve(context1, _profiles).Id);

        var context2 = new ForegroundAppInfo(
            (IntPtr)0x8007, "ms-teams.exe", "Microsoft Teams", null, "Microsoft Teams", "communication");
        Assert.Equal("communication", _resolver.Resolve(context2, _profiles).Id);
    }

    [Fact]
    public void ExactProfile_Overrides_CategoryProfile()
    {
        // Custom exact profile for discord with higher priority
        var customProfiles = new List<ProfileDefinition>(_profiles)
        {
            new()
            {
                Id = "discord-custom",
                DisplayName = "Discord Custom",
                Priority = 80,
                Match = new MatchDefinition { ProcessNames = new List<string> { "discord" } }
            }
        };

        var discord = new ForegroundAppInfo(
            (IntPtr)0x8008, "discord.exe", "Discord", null, "Discord", "communication");

        var profile = _resolver.Resolve(discord, customProfiles);
        Assert.Equal("discord-custom", profile.Id);
        Assert.Equal("Discord Custom", profile.DisplayName);
    }

    [Fact]
    public void ExactProfile_CanMatchExecutablePath_WithoutProcessNameMatch()
    {
        var customProfiles = new List<ProfileDefinition>(_profiles)
        {
            new()
            {
                Id = "path-custom",
                DisplayName = "Path Custom",
                Priority = 90,
                Match = new MatchDefinition
                {
                    ExecutablePaths = [@"C:\Apps\Acme\Acme.exe"]
                }
            }
        };

        var context = new ForegroundAppInfo(
            (IntPtr)0x8010,
            "renamed-process.exe",
            "Acme",
            @"c:\apps\acme\acme.exe",
            "Acme",
            null);

        Assert.Equal("path-custom", _resolver.Resolve(context, customProfiles).Id);
    }

    [Fact]
    public void DevelopmentProfile_Structure_HasRequiredControls()
    {
        var devProfile = _profiles.FirstOrDefault(p => p.Id == "development");
        Assert.NotNull(devProfile);
        Assert.NotEmpty(devProfile.Pages);

        var page = devProfile.Pages[0];

        // Flexible media widget exists and collapses when unavailable
        var media = page.Items.FirstOrDefault(i => i.Type == "media");
        Assert.NotNull(media);
        Assert.Equal(0, media.Width);
        Assert.True(media.CollapseWhenUnavailable);

        // System volume slider exists
        var volume = page.Items.FirstOrDefault(i => i.Type == "slider" && i.System);
        Assert.NotNull(volume);
        Assert.Equal("MASTER VOL", volume.Label);

        // System mute button exists
        var mute = page.Items.FirstOrDefault(i => i.Type == "button" && i.System);
        Assert.NotNull(mute);
        Assert.Equal("Mute", mute.Label);
    }

    [Fact]
    public void CommunicationProfile_Structure_HasRequiredControls()
    {
        var commProfile = _profiles.FirstOrDefault(p => p.Id == "communication");
        Assert.NotNull(commProfile);
        Assert.NotEmpty(commProfile.Pages);

        var page = commProfile.Pages[0];

        // Flexible media widget exists and collapses when unavailable
        var media = page.Items.FirstOrDefault(i => i.Type == "media");
        Assert.NotNull(media);
        Assert.Equal(0, media.Width);
        Assert.True(media.CollapseWhenUnavailable);

        // System volume slider exists
        var volume = page.Items.FirstOrDefault(i => i.Type == "slider" && i.System);
        Assert.NotNull(volume);
        Assert.Equal("MASTER VOL", volume.Label);

        // System mute button exists
        var mute = page.Items.FirstOrDefault(i => i.Type == "button" && i.System);
        Assert.NotNull(mute);
        Assert.Equal("Mute", mute.Label);
    }

    [Fact]
    public void AppMetadataService_DetectsCategoriesConservatively()
    {
        AppMetadataService.ClearCache();

        Assert.Equal("development", AppMetadataService.Resolve("Code.exe", null).Category);
        Assert.Equal("development", AppMetadataService.Resolve("devenv.exe", null).Category);
        Assert.Equal("development", AppMetadataService.Resolve("rider64.exe", null).Category);
        Assert.Equal("development", AppMetadataService.Resolve("idea64.exe", null).Category);
        Assert.Equal("development", AppMetadataService.Resolve("pycharm64.exe", null).Category);
        Assert.Equal("development", AppMetadataService.Resolve("webstorm64.exe", null).Category);
        Assert.Equal("development", AppMetadataService.Resolve("antigravity.exe", null).Category);

        Assert.Equal("communication", AppMetadataService.Resolve("Discord.exe", null).Category);
        Assert.Equal("communication", AppMetadataService.Resolve("Teams.exe", null).Category);
        Assert.Equal("communication", AppMetadataService.Resolve("ms-teams.exe", null).Category);
        Assert.Equal("communication", AppMetadataService.Resolve("Slack.exe", null).Category);
        Assert.Equal("communication", AppMetadataService.Resolve("Telegram.exe", null).Category);
        Assert.Equal("communication", AppMetadataService.Resolve("WhatsApp.exe", null).Category);

        Assert.Equal("browser", AppMetadataService.Resolve("chrome.exe", null).Category);
        Assert.Equal("browser", AppMetadataService.Resolve("firefox.exe", null).Category);

        Assert.Null(AppMetadataService.Resolve("notepad.exe", null).Category);
        Assert.Null(AppMetadataService.Resolve("calc.exe", null).Category);
        Assert.Null(AppMetadataService.Resolve("mystery_app.exe", null).Category);
    }

    [Fact]
    public void Discord_WithoutCategory_UsesOtherAppsProfile()
    {
        var discordContext = new ForegroundAppInfo(
            (IntPtr)0x3001,
            "discord",
            "Discord",
            @"C:\Users\User\AppData\Local\Discord\app-1.0.9000\Discord.exe",
            "Discord",
            null);

        var profile = _resolver.Resolve(discordContext, _profiles);
        Assert.Equal("other", profile.Id);
        Assert.Equal("Discord", profile.DisplayName);
    }

    [Fact]
    public void Notepad_UsesOtherAppsProfile()
    {
        var notepadContext = new ForegroundAppInfo(
            (IntPtr)0x3002,
            "notepad.exe",
            "Untitled - Notepad",
            @"C:\Windows\System32\notepad.exe",
            "Notepad",
            null);

        var profile = _resolver.Resolve(notepadContext, _profiles);
        Assert.Equal("other", profile.Id);
        Assert.Equal("Notepad", profile.DisplayName);
    }

    [Fact]
    public void Photoshop_UsesOtherAppsProfile()
    {
        var psContext = new ForegroundAppInfo(
            (IntPtr)0x3003,
            "Photoshop.exe",
            "Adobe Photoshop 2024",
            @"C:\Program Files\Adobe\Adobe Photoshop 2024\Photoshop.exe",
            "Adobe Photoshop",
            null);

        var profile = _resolver.Resolve(psContext, _profiles);
        Assert.Equal("other", profile.Id);
        Assert.Equal("Adobe Photoshop", profile.DisplayName);
    }

    [Fact]
    public void UnknownApp_DoesNotFallBackToDesktop_UsesOtherApps()
    {
        var unknownContext = new ForegroundAppInfo(
            (IntPtr)0x4001,
            "unknown.exe",
            "Mystery Window",
            @"C:\SomeFolder\unknown.exe",
            "Unknown",
            null);

        var profile = _resolver.Resolve(unknownContext, _profiles);

        // MUST NOT fall back to desktop!
        Assert.NotEqual("desktop", profile.Id);
        Assert.Equal("other", profile.Id);
        Assert.Equal("Unknown", profile.DisplayName);
    }

    [Fact]
    public void UnmatchedApp_UsesEditableNonDesktopDefaultProfile()
    {
        var profiles = _profiles
            .Where(p => !p.Id.Equals("other", StringComparison.OrdinalIgnoreCase))
            .ToList();
        profiles.Add(
            new ProfileDefinition
            {
                Id = "other",
                DisplayName = "Other Apps",
                IsDefault = true,
                Pages = [new() { Id = "main" }]
            });
        var context = new ForegroundAppInfo(
            (IntPtr)0x4002,
            "unassigned.exe",
            "Unassigned",
            @"C:\Apps\Unassigned\unassigned.exe",
            "Unassigned",
            null);

        var profile = _resolver.Resolve(context, profiles);

        Assert.Equal("other", profile.Id);
        Assert.Equal("Unassigned", profile.DisplayName);
        Assert.Equal("Other Apps", profiles.Single(p => p.Id == "other").DisplayName);
    }

    [Fact]
    public void UnmatchedApp_WithoutNonDesktopDefault_KeepsGeneratedGenericFallback()
    {
        var profiles = _profiles
            .Where(p => !p.Id.Equals("other", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var context = new ForegroundAppInfo(
            (IntPtr)0x4003,
            "legacy-app.exe",
            "Legacy App",
            @"C:\Apps\Legacy\legacy-app.exe",
            "Legacy App",
            null);

        var profile = _resolver.Resolve(context, profiles);

        Assert.Equal("generic:legacy-app", profile.Id);
        Assert.Equal("Legacy App", profile.DisplayName);
    }

    [Fact]
    public void SwitchSequence_Chrome_Discord_Desktop_Notepad_ResolvesAccurately()
    {
        var chrome = new ForegroundAppInfo(
            (IntPtr)0x5001, "chrome", "Chrome", null, "Google Chrome", "browser");
        var discord = new ForegroundAppInfo(
            (IntPtr)0x5002, "discord", "Discord", null, "Discord", "communication");
        var desktop = new ForegroundAppInfo(
            (IntPtr)0x5003, "explorer", "Program Manager", null, "Desktop", null, "Progman");
        var notepad = new ForegroundAppInfo(
            (IntPtr)0x5004, "notepad", "Notes", null, "Notepad", null);

        Assert.Equal("browser", _resolver.Resolve(chrome, _profiles).Id);
        Assert.Equal("communication", _resolver.Resolve(discord, _profiles).Id);
        Assert.Equal("Discord", _resolver.Resolve(discord, _profiles).DisplayName);
        Assert.Equal("desktop", _resolver.Resolve(desktop, _profiles).Id);
        Assert.Equal("other", _resolver.Resolve(notepad, _profiles).Id);
    }

    [Fact]
    public void OtherAppsProfile_HasCollapsingMedia_And_SystemControls()
    {
        var app = new ForegroundAppInfo(
            (IntPtr)0x6001, "mytestapp", "My Test App", null, "My Test App", null);

        var profile = _resolver.Resolve(app, _profiles);
        Assert.Equal("other", profile.Id);
        Assert.Equal(3, profile.Pages.Count);
        Assert.Equal("main", profile.Pages[0].Id);
        Assert.Equal("system", profile.Pages[1].Id);
        Assert.Equal("mixer", profile.Pages[2].Id);

        var page = profile.Pages[0];

        // Center media widget
        var mediaItem = page.Items.FirstOrDefault(i => i.Type == "media");
        Assert.NotNull(mediaItem);
        Assert.Equal(0, mediaItem.Width);
        Assert.True(mediaItem.CollapseWhenUnavailable);

        // Master volume slider in system items
        var volSlider = page.Items.FirstOrDefault(i => i.Type == "slider" && i.System);
        Assert.NotNull(volSlider);
        Assert.Equal("MASTER VOL", volSlider.Label);
        Assert.Equal("volume", volSlider.Action?.Type);

        // Mute button in system items
        var muteButton = page.Items.FirstOrDefault(i => i.Type == "button" && i.System);
        Assert.NotNull(muteButton);
        Assert.Equal("Mute", muteButton.Label);
        Assert.Equal("mute", muteButton.Action?.Value);
    }

    [Fact]
    public void AppMetadataService_CachesMetadata_NoRepeatedDiskHits()
    {
        AppMetadataService.ClearCache();

        var (friendly1, cat1) = AppMetadataService.Resolve("chrome.exe", null);
        Assert.Equal("Chrome", friendly1);
        Assert.Equal("browser", cat1);

        // Second call should hit in-memory cache
        var (friendly2, cat2) = AppMetadataService.Resolve("chrome.exe", null);
        Assert.Equal(friendly1, friendly2);
        Assert.Equal(cat1, cat2);
    }

    [Fact]
    public void ContextEquality_DetectsSameEffectiveContext()
    {
        var ctx1 = new ForegroundAppInfo(
            (IntPtr)0x7001, "notepad", "Doc1.txt", @"C:\notepad.exe", "Notepad", null);
        var ctx2 = new ForegroundAppInfo(
            (IntPtr)0x7001, "notepad", "Doc1.txt", @"C:\notepad.exe", "Notepad", null);
        var ctx3 = new ForegroundAppInfo(
            (IntPtr)0x7002, "notepad", "Doc2.txt", @"C:\notepad.exe", "Notepad", null);

        Assert.True(ctx1.IsSameEffectiveContext(ctx2));
        Assert.False(ctx1.IsSameEffectiveContext(ctx3));
    }

    [Fact]
    public void MainViewModel_SwitchSequence_Chrome_Discord_Desktop_Notepad_UpdatesProfilesCorrectly()
    {
        RunInSta(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarVMTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var logger = new AppLogger(tempDir);
                var config = new AppConfig { PollIntervalMs = 5000 };
                using var foreground = new ForegroundAppService(logger);
                using var profiles = new ProfileService(Path.Combine(AppContext.BaseDirectory, "Profiles"), logger);
                using var audio = new WindowsAudioProvider();
                var actions = new ActionExecutor(config, logger, audio);
                var mediaSession = new MediaSessionService(logger);

                using var vm = new MainViewModel(config, foreground, profiles, audio, actions, mediaSession, logger);

                // 1. Chrome -> Browser profile
                var chrome = new ForegroundAppInfo((IntPtr)0x5001, "chrome", "Chrome", null, "Google Chrome", "browser");
                vm.ApplyForegroundApp(chrome);
                Assert.Equal("Browser", vm.ActiveProfileName);
                Assert.NotEmpty(vm.Items);
                Assert.NotEmpty(vm.SystemItems);

                // 2. Discord -> editable Other Apps fallback
                var discord = new ForegroundAppInfo((IntPtr)0x5002, "discord", "Discord", null, "Discord", null);
                vm.ApplyForegroundApp(discord);
                Assert.Equal("Discord", vm.ActiveProfileName);
                Assert.NotEmpty(vm.Items);
                Assert.NotEmpty(vm.SystemItems);

                // 3. Desktop -> Desktop profile
                var desktop = new ForegroundAppInfo((IntPtr)0x5003, "explorer", "Program Manager", null, "Desktop", null, "Progman");
                vm.ApplyForegroundApp(desktop);
                Assert.Equal("Desktop", vm.ActiveProfileName);
                Assert.NotEmpty(vm.Items);
                Assert.NotEmpty(vm.SystemItems);

                // 4. Notepad -> editable Other Apps fallback
                var notepad = new ForegroundAppInfo((IntPtr)0x5004, "notepad", "Notes", null, "Notepad", null);
                vm.ApplyForegroundApp(notepad);
                Assert.Equal("Notepad", vm.ActiveProfileName);
                Assert.NotEmpty(vm.Items);
                Assert.NotEmpty(vm.SystemItems);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });
    }

}
