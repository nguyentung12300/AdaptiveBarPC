using System;
using System.IO;
using System.Linq;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using Xunit;

namespace AdaptiveBarPC.Tests;

public sealed class AppShellRedesignTests
{
    private static string GetMainWindowXaml()
        => File.ReadAllText(GetMainWindowXamlPath());

    private static string GetMainWindowCodeBehind()
    {
        var xamlPath = GetMainWindowXamlPath();
        return File.ReadAllText(xamlPath + ".cs");
    }

    private static string GetMainWindowXamlPath()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        var path = Path.Combine(dir, "src", "AdaptiveBarPC.App", "MainWindow.xaml");
        Assert.True(File.Exists(path), $"MainWindow.xaml not found at {path}");
        return path;
    }

    [Fact]
    public void AppShell_MainWindowXaml_UsesRedesignedFluidHardwareSurfaceGeometry()
    {
        var xaml = GetMainWindowXaml();

        // Window height 72 DIP
        Assert.Contains("Height=\"72\"", xaml);

        // Root border: appearance-driven shell geometry/stroke, clean clipping
        Assert.Contains("CornerRadius=\"{DynamicResource ShellCornerRadius}\"", xaml);
        Assert.Contains("BorderThickness=\"1\"", xaml);
        Assert.Contains("BorderBrush=\"{DynamicResource ShellBorderBrush}\"", xaml);
        Assert.Contains("ClipToBounds=\"True\"", xaml);
        // Keep the normal shell padding; only the emoji picker makes room for 56 DIP targets.
        Assert.Contains("<Setter Property=\"Margin\" Value=\"8,7\"/>", xaml);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsEmojiPickerOpen}\" Value=\"True\">", xaml);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"8,2\"/>", xaml);
    }

    [Fact]
    public void AppShell_MainWindowXaml_DefinesVectorPageIndicatorsAndContextPanel()
    {
        var xaml = GetMainWindowXaml();

        // Left context cluster is non-hit-testable to preserve touch gestures
        Assert.Contains("IsHitTestVisible=\"False\"", xaml);

        // Vector page indicator binds to PageDots
        Assert.Contains("ItemsSource=\"{Binding PageDots}\"", xaml);

        // Uses semantic tokens
        Assert.Contains("AccentPrimary", xaml);
        Assert.Contains("TextMuted", xaml);
    }

    [Fact]
    public void AppShell_MainWindowXaml_NavigationButtonsUseSegoeFluentGlyphsAndNonActivatingFlags()
    {
        var xaml = GetMainWindowXaml();

        // Segoe Fluent Icons: ChevronLeft (\uE76B) and ChevronRight (\uE76C)
        Assert.Contains("&#xE76B;", xaml);
        Assert.Contains("&#xE76C;", xaml);

        // Explicit non-activation and keyboard focus avoidance
        Assert.Contains("Focusable=\"False\"", xaml);
        Assert.Contains("IsTabStop=\"False\"", xaml);
    }

    [Fact]
    public void AppShell_ItemStripSwipe_ContinuesWithNativeInertiaAfterTouchRelease()
    {
        var xaml = GetMainWindowXaml();
        var codeBehind = GetMainWindowCodeBehind();

        Assert.Contains("ManipulationInertiaStarting=\"RootBorder_OnManipulationInertiaStarting\"", xaml);
        Assert.Contains("e.TranslationBehavior.DesiredDeceleration", codeBehind);
    }

    [Fact]
    public void AppShell_AppConfig_DefaultBarHeightIs72()
    {
        var config = new AppConfig();
        Assert.Equal(72, config.BarHeightPixels);
    }

    [Fact]
    public void MainViewModel_SinglePageProfile_PageDotsAndRightControls()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Write a single-page default profile
            var singlePageJson = """
            {
              "id": "single",
              "displayName": "Single Page",
              "priority": 100,
              "isDefault": true,
              "pages": [
                { "id": "p1", "displayName": "Page 1", "items": [{ "type": "button", "label": "B1" }] }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(tempDir, "00-single.json"), singlePageJson);

            var logger = new AppLogger(tempDir);
            var config = new AppConfig();
            var foreground = new ForegroundAppService();
            var profiles = new ProfileService(tempDir, logger);
            foreground.Dispose();
            var audio = new WindowsAudioProvider();
            var actions = new ActionExecutor(config, logger);
            var mediaService = new MediaSessionService(logger);

            using var vm = new MainViewModel(config, foreground, profiles, audio, actions, mediaService, logger);
            vm.ApplyForegroundApp(new ForegroundAppInfo(IntPtr.Zero, "", "", null, "Desktop", null));

            // Single page: PageDots is empty, HasMultiplePages is false
            Assert.False(vm.HasMultiplePages);
            Assert.Empty(vm.PageDots);

            // No system items and single page -> HasRightControls is false
            Assert.False(vm.HasSystemItems);
            Assert.False(vm.HasRightControls);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void MainViewModel_MultiPageProfile_PageDotsTracking()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Write a multi-page profile with 3 pages
            var multiPageJson = """
            {
              "id": "multitest",
              "displayName": "Multi Test",
              "priority": 100,
              "isDefault": true,
              "pages": [
                { "id": "p1", "displayName": "Page 1", "items": [{ "type": "button", "label": "B1" }] },
                { "id": "p2", "displayName": "Page 2", "items": [{ "type": "button", "label": "B2" }] },
                { "id": "p3", "displayName": "Page 3", "items": [{ "type": "button", "label": "B3" }] }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(tempDir, "01-multi.json"), multiPageJson);

            var logger = new AppLogger(tempDir);
            var config = new AppConfig();
            var foreground = new ForegroundAppService();
            var profiles = new ProfileService(tempDir, logger);
            foreground.Dispose();
            var audio = new WindowsAudioProvider();
            var actions = new ActionExecutor(config, logger);
            var mediaService = new MediaSessionService(logger);

            using var vm = new MainViewModel(config, foreground, profiles, audio, actions, mediaService, logger);

            Assert.True(vm.HasMultiplePages);
            Assert.True(vm.HasRightControls);
            Assert.Equal(3, vm.PageDots.Count);

            // Page 0 active
            Assert.True(vm.PageDots[0].IsActive);
            Assert.False(vm.PageDots[1].IsActive);
            Assert.False(vm.PageDots[2].IsActive);

            // Navigate to page 1
            vm.NextPage();
            Assert.False(vm.PageDots[0].IsActive);
            Assert.True(vm.PageDots[1].IsActive);
            Assert.False(vm.PageDots[2].IsActive);

            // Navigate to page 2
            vm.NextPage();
            Assert.False(vm.PageDots[0].IsActive);
            Assert.False(vm.PageDots[1].IsActive);
            Assert.True(vm.PageDots[2].IsActive);

            // Navigate back to page 1
            vm.PreviousPage();
            Assert.False(vm.PageDots[0].IsActive);
            Assert.True(vm.PageDots[1].IsActive);
            Assert.False(vm.PageDots[2].IsActive);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void MainViewModel_PageSwitch_KeepsFixedWidgetInstancesMounted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var json = """
            {
              "id": "fixed-test",
              "displayName": "Fixed Test",
              "priority": 100,
              "match": { "processNames": ["fixedtest"] },
              "fixedLeft": [{ "id": "volume", "type": "button", "label": "Volume" }],
              "fixedRight": [{ "id": "mute", "type": "button", "label": "Mute" }],
              "pages": [
                { "id": "p1", "items": [{ "id": "page-1", "type": "button", "label": "Page 1" }] },
                { "id": "p2", "items": [{ "id": "page-2", "type": "button", "label": "Page 2" }] }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(tempDir, "00-fixed.json"), json);
            var appearancePath = Path.Combine(tempDir, "appearance.json");
            GlobalAppearanceStore.Save(new GlobalAppearanceSettings
            {
                FixedLeft = [new ControlDefinition { Id = "global-left", Type = "button", Label = "Global Left" }],
                FixedRight = [new ControlDefinition { Id = "global-right", Type = "button", Label = "Global Right" }]
            }, appearancePath);

            var logger = new AppLogger(tempDir);
            var config = new AppConfig();
            var foreground = new ForegroundAppService();
            var profiles = new ProfileService(tempDir, logger);
            foreground.Dispose();
            var audio = new WindowsAudioProvider();
            var actions = new ActionExecutor(config, logger);
            var mediaService = new MediaSessionService(logger);
            using var vm = new MainViewModel(
                config, foreground, profiles, audio, actions, mediaService, logger,
                appearanceFilePath: appearancePath);
            vm.ApplyForegroundApp(new ForegroundAppInfo(
                (IntPtr)0x9001, "fixedtest.exe", "Fixed Test", @"C:\Apps\fixedtest.exe", "Fixed Test", null));

            Assert.Equal(["global-left", "volume"], vm.FixedLeftItems.Select(x => x.Id));
            Assert.Equal(["mute", "global-right"], vm.FixedRightItems.Select(x => x.Id));
            var left = vm.FixedLeftItems.ToArray();
            var right = vm.FixedRightItems.ToArray();
            var firstPage = Assert.Single(vm.Items);

            vm.NextPage();

            Assert.Same(left[0], vm.FixedLeftItems[0]);
            Assert.Same(left[1], vm.FixedLeftItems[1]);
            Assert.Same(right[0], vm.FixedRightItems[0]);
            Assert.Same(right[1], vm.FixedRightItems[1]);
            Assert.NotSame(firstPage, Assert.Single(vm.Items));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
