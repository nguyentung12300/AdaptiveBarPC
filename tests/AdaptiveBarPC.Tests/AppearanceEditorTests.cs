using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using Xunit;

namespace AdaptiveBarPC.Tests;

public sealed class AppearanceEditorTests
{
    [Fact]
    public void SavedTheme_RestoresVisualsAndPreservesWidgetsAndInputTools()
    {
        using var folder = new TempProfiles(CreateProfile("saved-theme"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;
        vm.GlobalAppearance.FixedLeft.Add(new ControlDefinition { Id = "fixed-left" });
        vm.GlobalAppearance.FixedRight.Add(new ControlDefinition { Id = "fixed-right" });
        vm.AddInputItem(WidgetTypes.TextSlot);
        vm.SelectedWidgetEditor!.InputContent = "Private text stays out of theme files";
        var inputItem = Assert.Single(vm.InputItems);
        var profile = vm.Document;
        editor.SelectPreset("pixel");
        editor.AccentText = "#FF12AB34";
        editor.SecondaryAccentText = "#FFCC8844";
        editor.BackgroundText = "#FF102030";
        editor.SurfaceText = "#FF203040";
        editor.PrimaryTextText = "#FFCCDDEE";
        editor.SecondaryTextText = "#FF8899AA";
        editor.Intensity = 0.6;
        editor.MotionEnabled = false;
        editor.BackgroundMediaSource = "Assets/media/custom.gif";
        editor.BackgroundMediaFit = "contain";
        editor.BackgroundMediaLoop = false;
        editor.BackgroundMediaOffsetX = 120;
        editor.BackgroundMediaOffsetY = -20;
        editor.BackgroundMediaOpacity = 0.5;
        editor.BackgroundMediaZoom = 1.7;
        var themePath = Path.Combine(folder.DirectoryPath, "themes", "custom.adaptivebar-theme.json");

        Assert.True(vm.SaveTheme(themePath));
        var json = File.ReadAllText(themePath);
        Assert.Contains("\"format\": \"adaptivebar-theme\"", json);
        var legacyThemePath = Path.Combine(folder.DirectoryPath, "themes", "legacy.flexbar-theme.json");
        File.WriteAllText(legacyThemePath, json.Replace("adaptivebar-theme", "flexbar-theme", StringComparison.Ordinal));
        Assert.Equal("pixel", GlobalAppearanceStore.LoadTheme(legacyThemePath).Appearance?.Preset);
        Assert.DoesNotContain("inputItems", json);
        Assert.DoesNotContain("fixedLeft", json);
        Assert.DoesNotContain("fixedRight", json);
        Assert.DoesNotContain("Private text", json);
        editor.SelectPreset("fluid");
        editor.SurfaceText = "#FF445566";
        editor.BackgroundMediaSource = "";
        editor.BackgroundMediaZoom = 0.3;
        var updates = 0;
        vm.AppearanceChanged += _ => updates++;

        Assert.True(vm.LoadTheme(themePath));
        vm.FlushPendingAppearanceSave();

        Assert.Equal(1, updates);
        Assert.Same(profile, vm.Document);
        Assert.Same(inputItem, Assert.Single(vm.InputItems));
        Assert.False(vm.IsDirty);
        var reloaded = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var restored = reloaded.AppearanceEditor;
        Assert.Equal("pixel", restored.SelectedPresetId);
        Assert.Equal("#FF12AB34", restored.Accent);
        Assert.Equal("#FFCC8844", restored.SecondaryAccent);
        Assert.Equal("#FF102030", restored.Background);
        Assert.Equal("#FF203040", restored.Surface);
        Assert.Equal("#FFCCDDEE", restored.PrimaryText);
        Assert.Equal("#FF8899AA", restored.SecondaryText);
        Assert.Equal(0.6, restored.Intensity);
        Assert.False(restored.MotionEnabled);
        Assert.Equal("Assets/media/custom.gif", restored.BackgroundMediaSource);
        Assert.Equal("contain", restored.BackgroundMediaFit);
        Assert.False(restored.BackgroundMediaLoop);
        Assert.Equal(120, restored.BackgroundMediaOffsetX);
        Assert.Equal(-20, restored.BackgroundMediaOffsetY);
        Assert.Equal(0.5, restored.BackgroundMediaOpacity);
        Assert.Equal(1.7, restored.BackgroundMediaZoom);
        Assert.Equal("fixed-left", Assert.Single(reloaded.GlobalAppearance.FixedLeft).Id);
        Assert.Equal("fixed-right", Assert.Single(reloaded.GlobalAppearance.FixedRight).Id);
        Assert.Equal(inputItem.Action!.Value, Assert.Single(reloaded.InputItems).Action!.Value);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"theme\":{}}")]
    [InlineData("{\"format\":\"adaptivebar-theme\",\"version\":2,\"theme\":{}}")]
    [InlineData("{\"format\":\"adaptivebar-theme\",\"version\":1,\"theme\":null}")]
    [InlineData("{\"format\":\"adaptivebar-theme\",\"version\":1,\"theme\":{},\"appearance\":{\"intensity\":\"invalid\"}}")]
    public void InvalidSavedTheme_DoesNotReplaceCurrentAppearance(string json)
    {
        using var folder = new TempProfiles(CreateProfile("invalid-theme"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        vm.AppearanceEditor.SurfaceText = "#FF204060";
        var before = File.ReadAllText(folder.AppearancePath);
        var themePath = Path.Combine(folder.DirectoryPath, "invalid.adaptivebar-theme.json");
        File.WriteAllText(themePath, json);

        Assert.False(vm.LoadTheme(themePath));

        Assert.Equal(before, File.ReadAllText(folder.AppearancePath));
        Assert.Equal("#FF204060", vm.AppearanceEditor.Surface);
        Assert.Contains("Theme load failed:", vm.StatusMessage);
    }

    [Fact]
    public void OldProfile_NullAppearance_DisplaysEffectiveFluidState()
    {
        using var folder = new TempProfiles(CreateProfile("legacy-null-app", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.NotNull(vm.AppearanceEditor);
        Assert.Equal(AppearanceResolver.Fluid, vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("Fluid", vm.AppearanceEditor.SelectedPresetName);
        Assert.Equal(1.0, vm.AppearanceEditor.Intensity);
        Assert.Equal(100, vm.AppearanceEditor.IntensityPercent);
        Assert.True(vm.AppearanceEditor.MotionEnabled);
        Assert.Equal("On", vm.AppearanceEditor.MotionLabel);
        Assert.False(vm.AppearanceEditor.HasCustomAccent);
        Assert.False(vm.AppearanceEditor.HasCustomSecondaryAccent);
        Assert.Equal("#FF7C5CFC", vm.AppearanceEditor.Accent);
        Assert.Null(vm.Document?.Appearance);
    }

    [Fact]
    public void OpeningAppearanceEditor_DoesNotCreateRedundantAppearanceDefinition_AndDoesNotDirty()
    {
        using var folder = new TempProfiles(CreateProfile("legacy-open", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.False(vm.IsDirty);
        Assert.Null(vm.Document?.Appearance);

        // Switch to Appearance tab
        vm.SelectedRightTabIndex = 1;
        Assert.False(vm.IsDirty);
        Assert.Null(vm.Document?.Appearance);

        // Execute OpenAppearanceTabCommand
        vm.OpenAppearanceTabCommand.Execute(null);
        Assert.Equal(1, vm.SelectedRightTabIndex);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.Document?.Appearance);
    }

    [Fact]
    public void SelectingPreset_PersistsCanonicalIdentifier()
    {
        using var folder = new TempProfiles(CreateProfile("preset-test", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        vm.AppearanceEditor.SelectPreset(AppearanceResolver.Oled);
        Assert.Equal("oled", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("oled", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Preset);

        vm.AppearanceEditor.SelectPreset("CYBER");
        Assert.Equal("cyber", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("cyber", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Preset);

        vm.AppearanceEditor.SelectPreset("Pixel");
        Assert.Equal("pixel", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("pixel", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Preset);

        vm.AppearanceEditor.SelectPreset("RGB");
        Assert.Equal("rgb", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("rgb", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Preset);
    }

    [Fact]
    public void PresetChange_SavesGlobalAppearanceWithoutDirtyingProfile()
    {
        using var folder = new TempProfiles(CreateProfile("dirty-preset", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.False(vm.IsDirty);

        vm.AppearanceEditor.SelectPreset(AppearanceResolver.Cyber);
        Assert.False(vm.IsDirty);
        Assert.Equal("cyber", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Preset);
    }

    [Fact]
    public void SelectingSamePreset_DoesNotCreateFalseDirtyChange()
    {
        using var folder = new TempProfiles(CreateProfile("same-preset", new AppearanceDefinition { Preset = "oled" }));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.False(vm.IsDirty);
        Assert.Equal("oled", vm.AppearanceEditor.SelectedPresetId);

        // Select same preset
        vm.AppearanceEditor.SelectPreset("oled");
        Assert.False(vm.IsDirty);

        // Case-insensitive check
        vm.AppearanceEditor.SelectPreset("OLED");
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void PrimaryAccentEdit_UpdatesGlobalAppearanceWithoutDirtyingProfile()
    {
        using var folder = new TempProfiles(CreateProfile("accent-edit", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.False(vm.IsDirty);
        vm.AppearanceEditor.AccentText = "#3B82F6";

        Assert.False(vm.IsDirty);
        Assert.True(vm.AppearanceEditor.HasCustomAccent);
        Assert.Equal("#FF3B82F6", vm.AppearanceEditor.Accent);
        Assert.Equal("#FF3B82F6", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Accent);
    }

    [Fact]
    public void PrimaryAccentReset_RestoresThemeAccentAndInheritance()
    {
        var profile = CreateProfile("accent-reset", new AppearanceDefinition { Accent = "#FF123456" });
        profile.Theme = new ThemeDefinition { Accent = "#FF998877" };

        using var folder = new TempProfiles(profile);
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.True(vm.AppearanceEditor.HasCustomAccent);
        Assert.Equal("#FF123456", vm.AppearanceEditor.Accent);

        vm.AppearanceEditor.ResetAccentCommand.Execute(null);

        Assert.False(vm.AppearanceEditor.HasCustomAccent);
        Assert.Null(GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Accent);
        Assert.Equal("#FF998877", vm.AppearanceEditor.Accent);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SecondaryAccentEdit_UpdatesGlobalAppearanceWithoutDirtyingProfile()
    {
        using var folder = new TempProfiles(CreateProfile("sec-accent-edit", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.False(vm.IsDirty);
        vm.AppearanceEditor.SecondaryAccentText = "#FF007F";

        Assert.False(vm.IsDirty);
        Assert.True(vm.AppearanceEditor.HasCustomSecondaryAccent);
        Assert.Equal("#FFFF007F", vm.AppearanceEditor.SecondaryAccent);
        Assert.Equal("#FFFF007F", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.SecondaryAccent);
    }

    [Fact]
    public void SecondaryAccentReset_RestoresAutoFallback()
    {
        var profile = CreateProfile("sec-accent-reset", new AppearanceDefinition
        {
            Accent = "#FF00F0FF",
            SecondaryAccent = "#FFFF007F"
        });

        using var folder = new TempProfiles(profile);
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.True(vm.AppearanceEditor.HasCustomSecondaryAccent);
        Assert.Equal("#FFFF007F", vm.AppearanceEditor.SecondaryAccent);

        vm.AppearanceEditor.ResetSecondaryAccentCommand.Execute(null);

        Assert.False(vm.AppearanceEditor.HasCustomSecondaryAccent);
        Assert.Null(GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.SecondaryAccent);
        Assert.Equal("#FF00F0FF", vm.AppearanceEditor.SecondaryAccent);
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 100)]
    [InlineData(0.7, 70)]
    [InlineData(0.42, 42)]
    public void Intensity_Values_Zero_One_Middle(double val, int expectedPercent)
    {
        // Use a profile with cyber preset so Appearance definition is kept even when intensity is 1.0
        using var folder = new TempProfiles(CreateProfile($"intensity-{expectedPercent}", new AppearanceDefinition { Preset = "cyber", Intensity = 0.5 }));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        vm.AppearanceEditor.Intensity = val;

        Assert.Equal(val, vm.AppearanceEditor.Intensity);
        Assert.Equal(expectedPercent, vm.AppearanceEditor.IntensityPercent);
        Assert.Equal(val, GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Intensity);
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void InvalidIntensity_SafelyClamped(double input, double expectedClamped)
    {
        using var folder = new TempProfiles(CreateProfile("intensity-clamp", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        vm.AppearanceEditor.Intensity = input;
        Assert.Equal(expectedClamped, vm.AppearanceEditor.Intensity);
    }

    [Fact]
    public void MotionEnabled_ToggleUpdatesAppearanceAndLabel()
    {
        using var folder = new TempProfiles(CreateProfile("motion-test", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.True(vm.AppearanceEditor.MotionEnabled);
        Assert.Equal("On", vm.AppearanceEditor.MotionLabel);

        vm.AppearanceEditor.MotionEnabled = false;

        Assert.False(vm.AppearanceEditor.MotionEnabled);
        Assert.Equal("Off", vm.AppearanceEditor.MotionLabel);
        Assert.False(GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.MotionEnabled);
        Assert.False(vm.IsDirty);

        vm.AppearanceEditor.MotionEnabled = true;
        Assert.True(vm.AppearanceEditor.MotionEnabled);
        Assert.Equal("On", vm.AppearanceEditor.MotionLabel);
        Assert.True(GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.MotionEnabled);
    }

    [Theory]
    [InlineData("not-a-color")]
    [InlineData("#12")]
    [InlineData("#GG0000")]
    [InlineData("1234567890")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidColorInput_DoesNotCrash_AndDoesNotPersistInvalidState(string? invalidInput)
    {
        using var folder = new TempProfiles(CreateProfile("color-invalid", new AppearanceDefinition { Accent = "#FF112233" }));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        var initialAccent = vm.AppearanceEditor.Accent;
        Assert.Equal("#FF112233", initialAccent);

        // Setting invalid text should not crash
        vm.AppearanceEditor.AccentText = invalidInput!;

        // Last valid accent remains intact
        Assert.Equal("#FF112233", vm.AppearanceEditor.Accent);
        Assert.Equal("#FF112233", GlobalAppearanceStore.Load(folder.AppearancePath).Appearance?.Accent);
    }

    [Fact]
    public void ProfileSwitching_KeepsGlobalAppearance()
    {
        var profileA = CreateProfile("prof-a", new AppearanceDefinition
        {
            Preset = "cyber",
            Accent = "#FF00F0FF",
            SecondaryAccent = "#FFFF007F",
            Intensity = 0.8,
            MotionEnabled = false
        });

        var profileB = CreateProfile("prof-b", new AppearanceDefinition
        {
            Preset = "oled",
            Accent = "#FFFFFFFF",
            SecondaryAccent = "#FF888899",
            Intensity = 1.0,
            MotionEnabled = true
        });

        using var folder = new TempProfiles(profileA, profileB);
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        // Load A
        var entryA = vm.Profiles.First(p => p.Id == "prof-a");
        var entryB = vm.Profiles.First(p => p.Id == "prof-b");

        vm.LoadProfile(entryA);
        Assert.Equal("cyber", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("#FF00F0FF", vm.AppearanceEditor.Accent);
        Assert.Equal("#FFFF007F", vm.AppearanceEditor.SecondaryAccent);
        Assert.Equal(0.8, vm.AppearanceEditor.Intensity);
        Assert.False(vm.AppearanceEditor.MotionEnabled);

        // Switch to B: appearance remains global.
        vm.LoadProfile(entryB);
        Assert.Equal("cyber", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("#FF00F0FF", vm.AppearanceEditor.Accent);
        Assert.Equal("#FFFF007F", vm.AppearanceEditor.SecondaryAccent);
        Assert.Equal(0.8, vm.AppearanceEditor.Intensity);
        Assert.False(vm.AppearanceEditor.MotionEnabled);

        // Switch back to A
        vm.LoadProfile(entryA);
        Assert.Equal("cyber", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal("#FF00F0FF", vm.AppearanceEditor.Accent);
        Assert.Equal("#FFFF007F", vm.AppearanceEditor.SecondaryAccent);
        Assert.Equal(0.8, vm.AppearanceEditor.Intensity);
        Assert.False(vm.AppearanceEditor.MotionEnabled);
    }

    [Fact]
    public void AppearanceRoundTrip_GlobalFileReloadsAcrossDesignerInstances()
    {
        using var folder = new TempProfiles(CreateProfile("roundtrip", appearance: null));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        vm.AppearanceEditor.SelectPreset(AppearanceResolver.Pixel);
        vm.AppearanceEditor.AccentText = "#38EF7D";
        vm.AppearanceEditor.SecondaryAccentText = "#11998E";
        vm.AppearanceEditor.Intensity = 0.85;
        vm.AppearanceEditor.MotionEnabled = false;

        Assert.False(vm.IsDirty);

        var reloaded = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        Assert.Equal("pixel", reloaded.AppearanceEditor.SelectedPresetId);
        Assert.Equal("#FF38EF7D", reloaded.AppearanceEditor.Accent);
        Assert.Equal("#FF11998E", reloaded.AppearanceEditor.SecondaryAccent);
        Assert.Equal(0.85, reloaded.AppearanceEditor.Intensity);
        Assert.False(reloaded.AppearanceEditor.MotionEnabled);
    }

    [Fact]
    public void BackgroundMedia_RoundTripsThroughGlobalAppearance()
    {
        using var folder = new TempProfiles(CreateProfile("background-media"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;
        var paletteRefreshes = 0;
        vm.AppearanceChanged += _ => paletteRefreshes++;

        editor.BackgroundMediaSource = "Assets/media/background.gif";
        editor.BackgroundMediaFit = "cover";
        editor.BackgroundMediaLoop = false;
        editor.BackgroundMediaOpacity = 0.65;
        editor.BackgroundMediaOffsetX = 120;
        editor.BackgroundMediaOffsetY = -8;
        editor.BackgroundMediaZoom = 1.4;

        Assert.Equal(0, GlobalAppearanceStore.Load(folder.AppearancePath).BackgroundMedia.OffsetX);
        Assert.Equal(0, paletteRefreshes);
        vm.FlushPendingAppearanceSave();

        var reloaded = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath).AppearanceEditor;
        Assert.Equal("Assets/media/background.gif", reloaded.BackgroundMediaSource);
        Assert.Equal("cover", reloaded.BackgroundMediaFit);
        Assert.False(reloaded.BackgroundMediaLoop);
        Assert.Equal(0.65, reloaded.BackgroundMediaOpacity);
        Assert.Equal(120, reloaded.BackgroundMediaOffsetX);
        Assert.Equal(-8, reloaded.BackgroundMediaOffsetY);
        Assert.Equal(1.4, reloaded.BackgroundMediaZoom);
    }

    [Fact]
    public void BackgroundMedia_PositionResetCommandsRestoreEachAxis()
    {
        using var folder = new TempProfiles(CreateProfile("background-media-reset"));
        var editor = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath).AppearanceEditor;
        editor.BackgroundMediaOffsetX = 120;
        editor.BackgroundMediaOffsetY = -32;

        editor.ResetBackgroundMediaHorizontalCommand.Execute(null);
        Assert.Equal(0, editor.BackgroundMediaOffsetX);
        Assert.Equal(-32, editor.BackgroundMediaOffsetY);

        editor.ResetBackgroundMediaVerticalCommand.Execute(null);
        Assert.Equal(0, editor.BackgroundMediaOffsetY);
    }

    [Fact]
    public void DesignerPreviewAndRuntimeResolver_ProduceEquivalentResolvedAppearance()
    {
        var theme = new ThemeDefinition { Accent = "#FF7C5CFC" };
        var appearance = new AppearanceDefinition
        {
            Preset = "pixel",
            Accent = "#FF38EF7D",
            SecondaryAccent = "#FF11998E",
            Intensity = 0.9,
            MotionEnabled = true
        };

        var runtimeResolved = AppearanceResolver.Resolve(theme, appearance);

        // Simulate Designer notification
        ResolvedAppearance? designerCaptured = null;
        using var folder = new TempProfiles(CreateProfile("resolver-eq", appearance));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        vm.AppearanceChanged += r => designerCaptured = r;
        vm.NotifyAppearanceChanged();

        Assert.NotNull(designerCaptured);
        Assert.Equal(runtimeResolved.Preset, designerCaptured.Preset);
        Assert.Equal(runtimeResolved.SurfaceBase, designerCaptured.SurfaceBase);
        Assert.Equal(runtimeResolved.AccentPrimary, designerCaptured.AccentPrimary);
        Assert.Equal(runtimeResolved.AccentSecondary, designerCaptured.AccentSecondary);
        Assert.Equal(runtimeResolved.ControlCornerRadius, designerCaptured.ControlCornerRadius);
        Assert.Equal(runtimeResolved.SurfaceCornerRadius, designerCaptured.SurfaceCornerRadius);
        Assert.Equal(runtimeResolved.Intensity, designerCaptured.Intensity);
        Assert.Equal(runtimeResolved.MotionEnabled, designerCaptured.MotionEnabled);
    }

    [Fact]
    public void ResetAppearance_ClearsGlobalOverridesWithoutChangingLegacyProfile()
    {
        using var folder = new TempProfiles(CreateProfile("reset-clean", new AppearanceDefinition
        {
            Preset = "cyber",
            Accent = "#FF00F0FF",
            SecondaryAccent = "#FFFF007F"
        }));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.NotNull(vm.Document?.Appearance);

        vm.AppearanceEditor.ResetAppearanceCommand.Execute(null);

        Assert.NotNull(vm.Document?.Appearance);
        Assert.Null(GlobalAppearanceStore.Load(folder.AppearancePath).Appearance);
        Assert.Equal(AppearanceResolver.Fluid, vm.AppearanceEditor.SelectedPresetId);
        Assert.False(vm.AppearanceEditor.HasCustomAccent);
        Assert.False(vm.AppearanceEditor.HasCustomSecondaryAccent);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void AssetAndGlyphIcons_RemainValidWithAppearanceChanges()
    {
        var profile = CreateProfile("icons-preset-switch", appearance: null);
        profile.Pages[0].Items.Add(new ControlDefinition
        {
            Id = "glyph-btn",
            Type = WidgetTypes.Button,
            Label = "Mute",
            Icon = new IconDefinition { Kind = "glyph", Value = "\uE74F" }
        });
        profile.Pages[0].Items.Add(new ControlDefinition
        {
            Id = "asset-btn",
            Type = WidgetTypes.Button,
            Label = "Custom",
            Icon = new IconDefinition { Kind = "asset", Value = "icons/app.png" }
        });

        using var folder = new TempProfiles(profile);
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);

        Assert.Equal(2, vm.PreviewItems.Count);

        // Switch presets
        vm.AppearanceEditor.SelectPreset(AppearanceResolver.Pixel);
        Assert.Equal(2, vm.PreviewItems.Count);

        vm.AppearanceEditor.SelectPreset(AppearanceResolver.Oled);
        Assert.Equal(2, vm.PreviewItems.Count);

        vm.AppearanceEditor.SelectPreset(AppearanceResolver.Cyber);
        Assert.Equal(2, vm.PreviewItems.Count);
    }

    [Fact]
    public void DesignerStartup_XamlSmokeTest_LoadsWithoutMissingResources()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Scope Designer resources to this test; Application.Current would leave
                // unrelated sensor tests dispatching onto this terminated STA thread.
                using var folder = new TempProfiles(CreateProfile("startup"));
                var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
                var window = new AdaptiveBarPC.Designer.MainWindow(vm, DesignerStudioTests.LoadResources());
                Assert.NotNull(window);
                Assert.NotNull(window.PreviewSurface);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(10000);

        Assert.Null(threadException);
    }

    [Fact]
    public void Palette_UserCanCustomizeAndResetAllFields()
    {
        using var folder = new TempProfiles(CreateProfile("palette-test"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.NotNull(editor);

        // Customize all palette fields
        editor.SetBackgroundCommand.Execute("#FF112233");
        editor.SetSurfaceCommand.Execute("#FF223344");
        editor.SetPrimaryTextCommand.Execute("#FFEEFFFF");
        editor.SetSecondaryTextCommand.Execute("#FFCCDDEE");
        editor.SetAccentCommand.Execute("#FF00E5FF");
        editor.SetSecondaryAccentCommand.Execute("#FFFF0055");

        Assert.True(editor.HasCustomBackground);
        Assert.True(editor.HasCustomSurface);
        Assert.True(editor.HasCustomPrimaryText);
        Assert.True(editor.HasCustomSecondaryText);
        Assert.True(editor.HasCustomAccent);
        Assert.True(editor.HasCustomSecondaryAccent);

        Assert.Equal("#FF112233", editor.Background);
        Assert.Equal("#FF223344", editor.Surface);
        Assert.Equal("#FFEEFFFF", editor.PrimaryText);
        Assert.Equal("#FFCCDDEE", editor.SecondaryText);
        Assert.Equal("#FF00E5FF", editor.Accent);
        Assert.Equal("#FFFF0055", editor.SecondaryAccent);

        // Reset fields
        editor.ResetBackgroundCommand.Execute(null);
        editor.ResetSurfaceCommand.Execute(null);
        editor.ResetPrimaryTextCommand.Execute(null);
        editor.ResetSecondaryTextCommand.Execute(null);
        editor.ResetAccentCommand.Execute(null);
        editor.ResetSecondaryAccentCommand.Execute(null);

        Assert.False(editor.HasCustomBackground);
        Assert.False(editor.HasCustomSurface);
        Assert.False(editor.HasCustomPrimaryText);
        Assert.False(editor.HasCustomSecondaryText);
        Assert.False(editor.HasCustomAccent);
        Assert.False(editor.HasCustomSecondaryAccent);

        // Defaults should match Fluid
        Assert.Equal("#FF0A0A0D", editor.Background);
        Assert.Equal("#FF1C1C24", editor.Surface);
        Assert.Equal("#FFF5F5F7", editor.PrimaryText);
        Assert.Equal("#FFA1A1AA", editor.SecondaryText);
        Assert.Equal("#FF7C5CFC", editor.Accent);
        Assert.Equal("#FF7C5CFC", editor.SecondaryAccent);
    }

    [Fact]
    public void Palette_PresetSwitching_InheritsPresetDefaultsUnlessCustomized()
    {
        using var folder = new TempProfiles(CreateProfile("palette-preset-switch"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        // Customize only Background and Primary Accent
        editor.SetBackgroundCommand.Execute("#FF010203");
        editor.SetAccentCommand.Execute("#FF112233");

        Assert.True(editor.HasCustomBackground);
        Assert.False(editor.HasCustomSurface);

        // Switch preset to Pixel
        editor.SelectPresetCommand.Execute("pixel");

        // Custom Background and Accent must be preserved
        Assert.Equal("#FF010203", editor.Background);
        Assert.Equal("#FF112233", editor.Accent);

        // Non-customized Surface, SecondaryAccent, Text must inherit Pixel defaults
        var pixelDefaults = AppearanceResolver.GetPresetDefaults("pixel");
        Assert.Equal(pixelDefaults.Control, editor.Surface);
        Assert.Equal(pixelDefaults.SecondaryAccent, editor.SecondaryAccent);
        Assert.Equal(pixelDefaults.TextPrimary, editor.PrimaryText);
        Assert.Equal(pixelDefaults.TextSecondary, editor.SecondaryText);
    }

    [Fact]
    public void Palette_SaveAndReopen_PersistsCustomPalette()
    {
        var profile = CreateProfile("palette-persist");
        using var folder = new TempProfiles(profile);

        // 1. First Designer session: customize palette
        {
            var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
            var editor = vm.AppearanceEditor;
            editor.SelectPresetCommand.Execute("pixel");
            editor.SetBackgroundCommand.Execute("#FF090B0D");
            editor.SetSurfaceCommand.Execute("#FF181C20");
            editor.SetPrimaryTextCommand.Execute("#FFF6F0E4");
            editor.SetSecondaryTextCommand.Execute("#FFAA9B83");
            editor.SetAccentCommand.Execute("#FF00F0FF");
            editor.SetSecondaryAccentCommand.Execute("#FFFF007F");

            vm.SaveCommand.Execute(null);
            Assert.False(vm.IsDirty);
        }

        // 2. Reopen Designer: verify persistence
        {
            var vmReopened = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
            var editorReopened = vmReopened.AppearanceEditor;

            Assert.Equal("pixel", editorReopened.SelectedPresetId);
            Assert.Equal("#FF090B0D", editorReopened.Background);
            Assert.Equal("#FF181C20", editorReopened.Surface);
            Assert.Equal("#FFF6F0E4", editorReopened.PrimaryText);
            Assert.Equal("#FFAA9B83", editorReopened.SecondaryText);
            Assert.Equal("#FF00F0FF", editorReopened.Accent);
            Assert.Equal("#FFFF007F", editorReopened.SecondaryAccent);

            Assert.True(editorReopened.HasCustomBackground);
            Assert.True(editorReopened.HasCustomSurface);
            Assert.True(editorReopened.HasCustomPrimaryText);
            Assert.True(editorReopened.HasCustomSecondaryText);
            Assert.True(editorReopened.HasCustomAccent);
            Assert.True(editorReopened.HasCustomSecondaryAccent);
        }
    }

    [Fact]
    public void ColorFlyout_OpeningAndClosing_DoesNotDirtyProfile()
    {
        using var folder = new TempProfiles(CreateProfile("flyout-dirty-check"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.False(vm.IsDirty);

        var editors = new[]
        {
            editor.AccentEditor,
            editor.SecondaryAccentEditor,
            editor.BackgroundEditor,
            editor.SurfaceEditor,
            editor.PrimaryTextEditor,
            editor.SecondaryTextEditor
        };

        foreach (var field in editors)
        {
            // Open flyout
            field.IsFlyoutOpen = true;
            Assert.True(field.IsFlyoutOpen);
            Assert.False(vm.IsDirty);

            // Close flyout
            field.IsFlyoutOpen = false;
            Assert.False(field.IsFlyoutOpen);
            Assert.False(vm.IsDirty);
        }
    }

    [Fact]
    public void ColorFlyout_SelectingActiveColor_DoesNotDirtyProfile_AndClosesFlyout()
    {
        using var folder = new TempProfiles(CreateProfile("active-color-pick"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.False(vm.IsDirty);

        var activeColor = editor.BackgroundEditor.Value;
        editor.BackgroundEditor.IsFlyoutOpen = true;

        // Select the exact same active color
        editor.BackgroundEditor.SelectColorCommand.Execute(activeColor);

        Assert.False(editor.BackgroundEditor.IsFlyoutOpen);
        Assert.False(vm.IsDirty);
        Assert.False(editor.HasCustomBackground);
    }

    [Fact]
    public void ColorFlyout_SelectingNewColor_SavesGlobalAppearance_UpdatesLiveState_AndClosesFlyout()
    {
        using var folder = new TempProfiles(CreateProfile("new-color-pick"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        // 1. Primary Accent
        editor.AccentEditor.IsFlyoutOpen = true;
        editor.AccentEditor.SelectColorCommand.Execute("#FF3B82F6");
        Assert.False(editor.AccentEditor.IsFlyoutOpen);
        Assert.Equal("#FF3B82F6", editor.Accent);
        Assert.True(editor.HasCustomAccent);
        Assert.False(vm.IsDirty);

        // 2. Secondary Accent
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        editor.SecondaryAccentEditor.IsFlyoutOpen = true;
        editor.SecondaryAccentEditor.SelectColorCommand.Execute("#FFFF007F");
        Assert.False(editor.SecondaryAccentEditor.IsFlyoutOpen);
        Assert.Equal("#FFFF007F", editor.SecondaryAccent);
        Assert.True(editor.HasCustomSecondaryAccent);
        Assert.False(vm.IsDirty);

        // 3. Background
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        editor.BackgroundEditor.IsFlyoutOpen = true;
        editor.BackgroundEditor.SelectColorCommand.Execute("#FF0F172A");
        Assert.False(editor.BackgroundEditor.IsFlyoutOpen);
        Assert.Equal("#FF0F172A", editor.Background);
        Assert.True(editor.HasCustomBackground);
        Assert.False(vm.IsDirty);

        // 4. Surface
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        editor.SurfaceEditor.IsFlyoutOpen = true;
        editor.SurfaceEditor.SelectColorCommand.Execute("#FF181C20");
        Assert.False(editor.SurfaceEditor.IsFlyoutOpen);
        Assert.Equal("#FF181C20", editor.Surface);
        Assert.True(editor.HasCustomSurface);
        Assert.False(vm.IsDirty);

        // 5. Primary Text
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        editor.PrimaryTextEditor.IsFlyoutOpen = true;
        editor.PrimaryTextEditor.SelectColorCommand.Execute("#FFFFFFFF");
        Assert.False(editor.PrimaryTextEditor.IsFlyoutOpen);
        Assert.Equal("#FFFFFFFF", editor.PrimaryText);
        Assert.True(editor.HasCustomPrimaryText);
        Assert.False(vm.IsDirty);

        // 6. Secondary Text
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        editor.SecondaryTextEditor.IsFlyoutOpen = true;
        editor.SecondaryTextEditor.SelectColorCommand.Execute("#FFE4E4E7");
        Assert.False(editor.SecondaryTextEditor.IsFlyoutOpen);
        Assert.Equal("#FFE4E4E7", editor.SecondaryText);
        Assert.True(editor.HasCustomSecondaryText);
        Assert.False(vm.IsDirty);

        var saved = GlobalAppearanceStore.Load(folder.AppearancePath);
        Assert.Equal("#FF3B82F6", saved.Appearance?.Accent);
        Assert.Equal("#FFFF007F", saved.Appearance?.SecondaryAccent);
        Assert.Equal("#FF0F172A", saved.Theme.Background);
        Assert.Equal("#FF181C20", saved.Theme.TileBackground);
        Assert.Equal("#FFFFFFFF", saved.Theme.PrimaryText);
        Assert.Equal("#FFE4E4E7", saved.Theme.SecondaryText);
    }

    [Fact]
    public void ColorFlyout_QuickPalette_ContainsCuratedNeutralsAndAccents()
    {
        var colors = QuickColorPalette.Colors;
        Assert.Equal(20, colors.Count);

        // Curated Neutrals
        Assert.Contains(colors, c => c.Hex == "#FF0A0A0D"); // near black
        Assert.Contains(colors, c => c.Hex == "#FF0F172A"); // dark navy
        Assert.Contains(colors, c => c.Hex == "#FF181C20"); // dark gray
        Assert.Contains(colors, c => c.Hex == "#FF71717A"); // mid gray
        Assert.Contains(colors, c => c.Hex == "#FFE4E4E7"); // light gray
        Assert.Contains(colors, c => c.Hex == "#FFFFFFFF"); // white

        // Curated Accents
        Assert.Contains(colors, c => c.Hex == "#FF7C5CFC"); // violet
        Assert.Contains(colors, c => c.Hex == "#FF3B82F6"); // blue
        Assert.Contains(colors, c => c.Hex == "#FF00F0FF"); // cyan
        Assert.Contains(colors, c => c.Hex == "#FF11998E"); // teal
        Assert.Contains(colors, c => c.Hex == "#FF10B981"); // green
        Assert.Contains(colors, c => c.Hex == "#FFF59E0B"); // amber
        Assert.Contains(colors, c => c.Hex == "#FFEF4444"); // red
        Assert.Contains(colors, c => c.Hex == "#FFFF007F"); // pink / magenta
    }

    [Fact]
    public void ColorFlyout_InvalidHexEditing_DoesNotPersistOrDirty()
    {
        using var folder = new TempProfiles(CreateProfile("invalid-hex-edit"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.False(vm.IsDirty);
        var originalValue = editor.BackgroundEditor.Value;
        var originalThemeBg = vm.Document?.Theme.Background;

        // Type invalid hex
        editor.BackgroundEditor.Text = "not-a-color";

        // Must not dirty profile and value/theme must remain unchanged
        Assert.False(vm.IsDirty);
        Assert.Equal(originalValue, editor.BackgroundEditor.Value);
        Assert.Equal(originalThemeBg, vm.Document?.Theme.Background);
    }

    [Fact]
    public void Designer_AppearanceEditor_Instantiation_SmokeTest()
    {
        RunInSta(() =>
        {
            using var folder = new TempProfiles(CreateProfile("smoke-test"));
            var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
            vm.SelectedRightTabIndex = 1;

            var editor = vm.AppearanceEditor;
            Assert.NotNull(editor.AccentEditor);
            Assert.NotNull(editor.SecondaryAccentEditor);
            Assert.NotNull(editor.BackgroundEditor);
            Assert.NotNull(editor.SurfaceEditor);
            Assert.NotNull(editor.PrimaryTextEditor);
            Assert.NotNull(editor.SecondaryTextEditor);

            // Verify commands and initial values
            Assert.False(editor.AccentEditor.IsFlyoutOpen);
            editor.AccentEditor.ToggleFlyoutCommand.Execute(null);
            Assert.True(editor.AccentEditor.IsFlyoutOpen);
            editor.AccentEditor.ToggleFlyoutCommand.Execute(null);
            Assert.False(editor.AccentEditor.IsFlyoutOpen);
        });
    }

    [Fact]
    public void ColorFieldViewModel_RgbEdits_UpdateEffectiveColor_AndSynchronizeHex()
    {
        using var folder = new TempProfiles(CreateProfile("rgb-edit"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.False(vm.IsDirty);

        // Edit RGB on BackgroundEditor: R=40, G=50, B=60 -> #FF28323C
        editor.BackgroundEditor.R = 40;
        editor.BackgroundEditor.G = 50;
        editor.BackgroundEditor.B = 60;

        Assert.False(vm.IsDirty);
        Assert.Equal("#FF28323C", editor.Background);
        Assert.Equal("#FF28323C", editor.BackgroundText);
        Assert.Equal(40, editor.BackgroundEditor.R);
        Assert.Equal(50, editor.BackgroundEditor.G);
        Assert.Equal(60, editor.BackgroundEditor.B);
        Assert.Equal("40", editor.BackgroundEditor.RText);
        Assert.Equal("50", editor.BackgroundEditor.GText);
        Assert.Equal("60", editor.BackgroundEditor.BText);
        Assert.Equal("#FF28323C", GlobalAppearanceStore.Load(folder.AppearancePath).Theme.Background);
    }

    [Fact]
    public void ColorFieldViewModel_HexEdit_SynchronizesRgbAndChannels()
    {
        using var folder = new TempProfiles(CreateProfile("hex-rgb-sync"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        // Set HEX directly on PrimaryText
        editor.PrimaryTextEditor.Text = "#FF123456";

        Assert.False(vm.IsDirty);
        Assert.Equal("#FF123456", editor.PrimaryText);
        Assert.Equal(0x12, editor.PrimaryTextEditor.R);
        Assert.Equal(0x34, editor.PrimaryTextEditor.G);
        Assert.Equal(0x56, editor.PrimaryTextEditor.B);
        Assert.Equal("18", editor.PrimaryTextEditor.RText);
        Assert.Equal("52", editor.PrimaryTextEditor.GText);
        Assert.Equal("86", editor.PrimaryTextEditor.BText);
    }

    [Fact]
    public void ColorFieldViewModel_RgbNumericTextInputs_UpdateColor_AndClamp()
    {
        using var folder = new TempProfiles(CreateProfile("rgb-numeric-clamp"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        // Numeric text entry
        editor.SurfaceEditor.RText = "200";
        Assert.Equal(200, editor.SurfaceEditor.R);
        Assert.Equal("200", editor.SurfaceEditor.RText);

        // Clamping upper bound
        editor.SurfaceEditor.R = 999;
        Assert.Equal(255, editor.SurfaceEditor.R);
        Assert.Equal("255", editor.SurfaceEditor.RText);

        // Clamping lower bound
        editor.SurfaceEditor.G = -50;
        Assert.Equal(0, editor.SurfaceEditor.G);
        Assert.Equal("0", editor.SurfaceEditor.GText);
    }

    [Fact]
    public void ColorFieldViewModel_InvalidRgbInputs_DoNotCrash_OrCorrupt()
    {
        using var folder = new TempProfiles(CreateProfile("invalid-rgb"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        var prevR = editor.BackgroundEditor.R;
        var prevHex = editor.BackgroundEditor.Value;

        // Non-numeric input into RText must not throw and must not alter R
        editor.BackgroundEditor.RText = "not-a-number";
        Assert.Equal(prevR, editor.BackgroundEditor.R);
        Assert.Equal(prevHex, editor.BackgroundEditor.Value);

        // Empty string input into RText must not throw
        editor.BackgroundEditor.RText = "";
        Assert.Equal(prevR, editor.BackgroundEditor.R);
        Assert.Equal(prevHex, editor.BackgroundEditor.Value);
    }

    [Fact]
    public void ColorFieldViewModel_InvalidHexInput_RevertsOnFlyoutClose_DoesNotPersistGarbage()
    {
        using var folder = new TempProfiles(CreateProfile("invalid-hex-revert"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        var prevValue = editor.SecondaryTextEditor.Value;
        editor.SecondaryTextEditor.IsFlyoutOpen = true;

        // Enter invalid hex
        editor.SecondaryTextEditor.Text = "invalid-hex-123";
        Assert.False(vm.IsDirty);
        Assert.Equal(prevValue, editor.SecondaryTextEditor.Value);

        // Closing flyout restores Text to valid Value
        editor.SecondaryTextEditor.IsFlyoutOpen = false;
        Assert.Equal(prevValue, editor.SecondaryTextEditor.Text);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void ColorFieldViewModel_SelectingSameRgb_DoesNotDirtyProfile()
    {
        using var folder = new TempProfiles(CreateProfile("same-rgb-clean"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.False(vm.IsDirty);

        // Setting same R value does not dirty
        var currentR = editor.AccentEditor.R;
        editor.AccentEditor.R = currentR;

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void ColorFieldViewModel_AllSixFields_SupportRgbAndQuickColors()
    {
        using var folder = new TempProfiles(CreateProfile("all-six-fields"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        ColorFieldViewModel[] fields =
        [
            editor.AccentEditor,
            editor.SecondaryAccentEditor,
            editor.BackgroundEditor,
            editor.SurfaceEditor,
            editor.PrimaryTextEditor,
            editor.SecondaryTextEditor
        ];

        foreach (var f in fields)
        {
            Assert.InRange(f.R, 0, 255);
            Assert.InRange(f.G, 0, 255);
            Assert.InRange(f.B, 0, 255);
            Assert.NotNull(f.RText);
            Assert.NotNull(f.GText);
            Assert.NotNull(f.BText);
            Assert.Equal(20, f.QuickColors.Count);
            Assert.NotNull(f.Brush);
            Assert.False(f.IsFlyoutOpen);
        }
    }

    [Fact]
    public void AppearanceLabels_CleanAndTooltip_AreFormattedCorrectly()
    {
        using var folder = new TempProfiles(CreateProfile("label-check"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        // SubLabelClean strips enclosing parentheses for clean 2-line rendering
        Assert.Equal("Bar Base", editor.BackgroundEditor.SubLabelClean);
        Assert.Equal("Background — Bar Base", editor.BackgroundEditor.FullLabelTooltip);

        Assert.Equal("Buttons & Tiles", editor.SurfaceEditor.SubLabelClean);
        Assert.Equal("Surface — Buttons & Tiles", editor.SurfaceEditor.FullLabelTooltip);

        Assert.Equal("Labels & Values", editor.PrimaryTextEditor.SubLabelClean);
        Assert.Equal("Primary Text — Labels & Values", editor.PrimaryTextEditor.FullLabelTooltip);

        Assert.Equal("Muted & Units", editor.SecondaryTextEditor.SubLabelClean);
        Assert.Equal("Secondary Text — Muted & Units", editor.SecondaryTextEditor.FullLabelTooltip);

        // Fields without sublabel have null SubLabelClean and clean tooltip
        Assert.Null(editor.AccentEditor.SubLabelClean);
        Assert.Equal("Primary Accent", editor.AccentEditor.FullLabelTooltip);

        Assert.Null(editor.SecondaryAccentEditor.SubLabelClean);
        Assert.Equal("Secondary Accent", editor.SecondaryAccentEditor.FullLabelTooltip);
    }

    [Fact]
    public void ColorFieldViewModel_HsvEdits_SyncToRgbAndHex()
    {
        using var folder = new TempProfiles(CreateProfile("hsv-sync"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.False(vm.IsDirty);

        // Set Hue=180 (Cyan), S=1.0, V=1.0 -> #FF00FFFF
        editor.BackgroundEditor.Hue = 180.0;
        editor.BackgroundEditor.Saturation = 1.0;
        editor.BackgroundEditor.Brightness = 1.0;

        Assert.False(vm.IsDirty);
        Assert.Equal("#FF00FFFF", editor.Background);
        Assert.Equal("#FF00FFFF", editor.BackgroundText);
        Assert.Equal(0, editor.BackgroundEditor.R);
        Assert.Equal(255, editor.BackgroundEditor.G);
        Assert.Equal(255, editor.BackgroundEditor.B);
    }

    [Fact]
    public void ColorFieldViewModel_RgbEdits_SyncBackToHsvAndHueBrush()
    {
        using var folder = new TempProfiles(CreateProfile("rgb-to-hsv"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        // Set Pure Green via RGB
        editor.AccentEditor.R = 0;
        editor.AccentEditor.G = 255;
        editor.AccentEditor.B = 0;

        Assert.False(vm.IsDirty);
        Assert.Equal(120.0, editor.AccentEditor.Hue);
        Assert.Equal(1.0, editor.AccentEditor.Saturation);
        Assert.Equal(1.0, editor.AccentEditor.Brightness);
        Assert.NotNull(editor.AccentEditor.HueBrush);
    }

    [Fact]
    public void ColorFieldViewModel_HexEdit_SyncsHsv()
    {
        using var folder = new TempProfiles(CreateProfile("hex-to-hsv"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        // Pure Blue via HEX
        editor.SurfaceEditor.Text = "#FF0000FF";

        Assert.False(vm.IsDirty);
        Assert.Equal(240.0, editor.SurfaceEditor.Hue);
        Assert.Equal(1.0, editor.SurfaceEditor.Saturation);
        Assert.Equal(1.0, editor.SurfaceEditor.Brightness);
    }

    [Fact]
    public void ColorFieldViewModel_2DArea_SaturationAndBrightnessClamping()
    {
        using var folder = new TempProfiles(CreateProfile("hsv-clamping"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        editor.PrimaryTextEditor.Saturation = 5.0;
        Assert.Equal(1.0, editor.PrimaryTextEditor.Saturation);

        editor.PrimaryTextEditor.Saturation = -2.0;
        Assert.Equal(0.0, editor.PrimaryTextEditor.Saturation);

        editor.PrimaryTextEditor.Brightness = 10.0;
        Assert.Equal(1.0, editor.PrimaryTextEditor.Brightness);

        editor.PrimaryTextEditor.Brightness = -1.0;
        Assert.Equal(0.0, editor.PrimaryTextEditor.Brightness);

        editor.PrimaryTextEditor.Hue = 500.0;
        Assert.Equal(360.0, editor.PrimaryTextEditor.Hue);

        editor.PrimaryTextEditor.Hue = -50.0;
        Assert.Equal(0.0, editor.PrimaryTextEditor.Hue);
    }

    [Fact]
    public void ColorFieldViewModel_CopyHexCommand_ExecutesWithoutError()
    {
        using var folder = new TempProfiles(CreateProfile("copy-hex"));
        var vm = new DesignerViewModel(folder.DirectoryPath, folder.AppearancePath);
        var editor = vm.AppearanceEditor;

        Assert.NotNull(editor.BackgroundEditor.CopyHexCommand);
        Assert.True(editor.BackgroundEditor.CopyHexCommand.CanExecute(null));
        // Verify command executes cleanly
        RunInSta(() => editor.BackgroundEditor.CopyHexCommand.Execute(null));
    }

    [Fact]
    public void ColorAreaControl_Instantiation_AndPropertyTests()
    {
        RunInSta(() =>
        {
            var control = new ColorAreaControl();
            Assert.NotNull(control);
            Assert.Equal(1.0, control.Saturation);
            Assert.Equal(1.0, control.Brightness);

            control.Saturation = 0.5;
            control.Brightness = 0.8;
            Assert.Equal(0.5, control.Saturation);
            Assert.Equal(0.8, control.Brightness);
        });
    }


    private static ProfileDefinition CreateProfile(string id, AppearanceDefinition? appearance = null) => new()
    {
        Id = id,
        DisplayName = $"Test Profile {id}",
        Appearance = appearance,
        Pages =
        [
            new()
            {
                Id = "page-1",
                DisplayName = "Main Page",
                Items = []
            }
        ]
    };

    private sealed class TempProfiles : IDisposable
    {
        private readonly ProfileDocumentSerializer _serializer = new();

        public TempProfiles(params ProfileDefinition[] profiles)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "AdaptiveBarAppearanceEditorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            AppearancePath = Path.Combine(DirectoryPath, "appearance.json");
            for (var i = 0; i < profiles.Length; i++)
                _serializer.SaveFile(Path.Combine(DirectoryPath, $"{i:D2}-{profiles[i].Id}.json"), profiles[i]);
        }

        public string DirectoryPath { get; }
        public string AppearancePath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only
            }
        }
    }
}
