using System.IO;
using System.Reflection;
using System.Windows.Media;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public class ThemeFallbackTests
{

    [Fact]
    public void ApplyTheme_WithBaseTokensOnly_DerivesExpectedSurfacesAndAccentSubtle()
    {
        RunInSta(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBar_ThemeTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var logger = new AppLogger(tempDir);
                var config = new AppConfig();
                var foreground = new ForegroundAppService();
                var profiles = new ProfileService(tempDir, logger);
                foreground.Dispose();
                profiles.Dispose();
                var audio = new WindowsAudioProvider();
                var actions = new ActionExecutor(config, logger);
                var mediaService = new MediaSessionService(logger);

                using var vm = new MainViewModel(config, foreground, profiles, audio, actions, mediaService, logger);

                // Minimal theme with only the 5 base tokens (similar to 10-browser.json)
                var theme = new ThemeDefinition
                {
                    Background = "#FF0D1117",
                    TileBackground = "#FF1D2733",
                    Accent = "#FF55A7FF",
                    PrimaryText = "#E6F7FAFC",
                    SecondaryText = "#8091A4B7"
                };

                vm.ApplyTheme(theme);

                // 1. ControlSurface falls back to TileBackground, NOT DimGray
                var controlSurface = Assert.IsType<SolidColorBrush>(vm.ControlSurfaceBrush);
                Assert.Equal(Color.FromRgb(0x1D, 0x27, 0x33), controlSurface.Color);
                Assert.NotEqual(Brushes.DimGray.Color, controlSurface.Color);

                // 2. ControlWell is derived as a darker tint of ControlSurface (-10), NOT DimGray
                var controlWell = Assert.IsType<SolidColorBrush>(vm.ControlWellBrush);
                Assert.Equal(Color.FromRgb(0x13, 0x1D, 0x29), controlWell.Color);
                Assert.NotEqual(Brushes.DimGray.Color, controlWell.Color);

                // 3. ControlHover is derived as a lighter tint (+12), NOT DimGray
                var controlHover = Assert.IsType<SolidColorBrush>(vm.ControlHoverBrush);
                Assert.Equal(Color.FromRgb(0x29, 0x33, 0x3F), controlHover.Color);
                Assert.NotEqual(Brushes.DimGray.Color, controlHover.Color);

                // 4. ControlPressed is derived as a lighter tint (+24), NOT DimGray
                var controlPressed = Assert.IsType<SolidColorBrush>(vm.ControlPressedBrush);
                Assert.Equal(Color.FromRgb(0x35, 0x3F, 0x4B), controlPressed.Color);
                Assert.NotEqual(Brushes.DimGray.Color, controlPressed.Color);

                // 5. AccentSubtle is derived from Accent with 14% alpha (0x24), NOT MediumPurple
                var accentSubtle = Assert.IsType<SolidColorBrush>(vm.AccentSubtleBrush);
                Assert.Equal(Color.FromArgb(0x24, 0x55, 0xA7, 0xFF), accentSubtle.Color);
                Assert.NotEqual(Brushes.MediumPurple.Color, accentSubtle.Color);

                // 6. TextMuted and Divider are derived modern defaults, NOT flat DimGray
                var textMuted = Assert.IsType<SolidColorBrush>(vm.TextMutedBrush);
                Assert.Equal(Color.FromRgb(0x71, 0x71, 0x7A), textMuted.Color);

                var divider = Assert.IsType<SolidColorBrush>(vm.DividerBrush);
                Assert.Equal(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), divider.Color);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        });
    }

    [Fact]
    public void ProfileSwitch_DoesNotReplaceGlobalAppearance()
    {
        RunInSta(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBar_GlobalThemeTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var appearancePath = Path.Combine(tempDir, "appearance.json");
                GlobalAppearanceStore.Save(new GlobalAppearanceSettings
                {
                    Theme = new ThemeDefinition { Accent = "#FF123456" }
                }, appearancePath);

                var logger = new AppLogger(tempDir);
                var config = new AppConfig { PollIntervalMs = 5000 };
                using var foreground = new ForegroundAppService(logger);
                using var profiles = new ProfileService(Path.Combine(AppContext.BaseDirectory, "Profiles"), logger);
                using var audio = new WindowsAudioProvider();
                var actions = new ActionExecutor(config, logger, audio);
                var media = new MediaSessionService(logger);
                using var vm = new MainViewModel(config, foreground, profiles, audio, actions, media, logger, appearanceFilePath: appearancePath);

                var expected = Color.FromRgb(0x12, 0x34, 0x56);
                vm.ApplyForegroundApp(new ForegroundAppInfo((IntPtr)0x5001, "chrome", "Chrome", null, "Google Chrome", "browser"));
                Assert.Equal(expected, Assert.IsType<SolidColorBrush>(vm.AccentBrush).Color);

                vm.ApplyForegroundApp(new ForegroundAppInfo((IntPtr)0x5002, "discord", "Discord", null, "Discord", null));
                Assert.Equal(expected, Assert.IsType<SolidColorBrush>(vm.AccentBrush).Color);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        });
    }

    [Fact]
    public void ThemeDefinition_SchemaRemainsUnchanged_HasOnlyCanonicalProfileFields()
    {
        var properties = typeof(ThemeDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var expectedProperties = new[]
        {
            nameof(ThemeDefinition.Accent),
            nameof(ThemeDefinition.Background),
            nameof(ThemeDefinition.ExtensionData),
            nameof(ThemeDefinition.PrimaryText),
            nameof(ThemeDefinition.SecondaryText),
            nameof(ThemeDefinition.TileBackground)
        }.OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(expectedProperties, properties);
    }

    [Fact]
    public void ThemeTokens_LoadsValidResourceDictionary_AndContainsCoreTokens()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var tokenPath = Path.Combine(dir.FullName, "src", "AdaptiveBarPC.App", "Resources", "ThemeTokens.xaml");
        Assert.True(File.Exists(tokenPath), $"ThemeTokens.xaml not found at {tokenPath}");

        using var stream = File.OpenRead(tokenPath);
        var dict = Assert.IsType<System.Windows.ResourceDictionary>(System.Windows.Markup.XamlReader.Load(stream));
        Assert.NotNull(dict);

        Assert.True(dict.Contains("SurfaceBase"));
        Assert.True(dict.Contains("SurfaceContainer"));
        Assert.True(dict.Contains("SurfaceControl"));
        Assert.True(dict.Contains("AccentPrimary"));
        Assert.True(dict.Contains("AccentSubtle"));
        Assert.True(dict.Contains("TextPrimary"));
        Assert.True(dict.Contains("TextSecondary"));
        Assert.True(dict.Contains("BarBackground"));
        Assert.True(dict.Contains("ControlWell"));
        Assert.True(dict.Contains("ControlSurface"));
    }

    [Fact]
    public void Designer_AppXaml_ReferencesSharedThemeTokensThroughProjectRef()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var designerAppPath = Path.Combine(dir.FullName, "src", "AdaptiveBarPC.Designer", "App.xaml");
        Assert.True(File.Exists(designerAppPath), $"Designer App.xaml not found at {designerAppPath}");

        var xaml = File.ReadAllText(designerAppPath);
        Assert.Contains("/AdaptiveBarPC;component/Resources/ThemeTokens.xaml", xaml);
        Assert.Contains("/AdaptiveBarPC;component/Resources/BarControls.xaml", xaml);

        // Ensure duplicated brush definitions were removed from Designer App.xaml
        Assert.DoesNotContain("<SolidColorBrush x:Key=\"BarBackground\"", xaml);
        Assert.DoesNotContain("<SolidColorBrush x:Key=\"ControlWell\"", xaml);
        Assert.DoesNotContain("<SolidColorBrush x:Key=\"AccentBrush\"", xaml);
    }
}
