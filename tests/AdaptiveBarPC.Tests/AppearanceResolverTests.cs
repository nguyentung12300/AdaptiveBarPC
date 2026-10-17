using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using System.Windows;
using System.Windows.Media;

namespace AdaptiveBarPC.Tests;

public class AppearanceResolverTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void RgbMotion_RequiresBothGlobalAndAppearanceSwitches(bool globalMotion, bool themeMotion, bool expected)
    {
        RunInSta(() =>
        {
            var resolved = AppearanceResolver.Resolve(new ThemeDefinition(),
                new AppearanceDefinition { Preset = "rgb", MotionEnabled = themeMotion }, globalMotion);
            var resources = new ResourceDictionary();
            AppearanceResolver.ApplyToResources(resources, resolved);
            Assert.Equal(expected, resources["AppearanceMotionActive"]);
            Assert.Equal(expected, ((Brush)resources["ActiveAccentBrush"]).RelativeTransform.HasAnimatedProperties);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Resolve_InvalidOrMissingPreset_FallsBackToFluid(string? preset)
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = preset });

        Assert.Equal(AppearanceResolver.Fluid, resolved.Preset);
    }

    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    [InlineData("pixel")]
    public void Resolve_KnownPreset_IsPreserved(string preset)
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = preset.ToUpperInvariant() });

        Assert.Equal(preset, resolved.Preset);
    }

    [Fact]
    public void Resolve_AppearanceOverridesAccentAndResolvesSecondaryMotionAndIntensity()
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition { Accent = "#FF111111" },
            new AppearanceDefinition
            {
                Accent = "#FF222222",
                SecondaryAccent = "#FF333333",
                Intensity = 2,
                MotionEnabled = false
            });

        Assert.Equal("#FF222222", resolved.AccentPrimary);
        Assert.Equal("#FF333333", resolved.AccentSecondary);
        Assert.Equal(1, resolved.Intensity);
        Assert.False(resolved.MotionEnabled);
    }

    [Fact]
    public void Resolve_WithoutAppearance_PreservesLegacyThemeValues()
    {
        var theme = new ThemeDefinition
        {
            Background = "#FF010203",
            TileBackground = "#FF040506",
            Accent = "#FF070809",
            PrimaryText = "#FF101112",
            SecondaryText = "#FF131415"
        };

        var resolved = AppearanceResolver.Resolve(theme, null);

        Assert.Equal(theme.Background, resolved.SurfaceBase);
        Assert.Equal(theme.TileBackground, resolved.SurfaceControl);
        Assert.Equal(theme.Accent, resolved.AccentPrimary);
        Assert.Equal(theme.Accent, resolved.AccentSecondary);
        Assert.Equal(theme.PrimaryText, resolved.TextPrimary);
        Assert.Equal(theme.SecondaryText, resolved.TextSecondary);
        Assert.True(resolved.MotionEnabled);
    }

    [Fact]
    public void ApplyToResources_FluidEffectTokensPreserveBaseline()
    {
        var resolved = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "fluid" });
        var resources = new ResourceDictionary();

        AppearanceResolver.ApplyToResources(resources, resolved);

        Assert.Same(resources["AccentPrimary"], resources["ActiveAccentBrush"]);
        Assert.Same(resources["StrokeSubtle"], resources["ControlBorderBrush"]);
        Assert.Same(resources["StrokeSubtle"], resources["ShellBorderBrush"]);
        Assert.Same(resources["StrokeSubtle"], resources["MediaFrameBrush"]);
        Assert.Equal(new CornerRadius(14), resources["ShellCornerRadius"]);
        Assert.False((bool)resources["AppearanceMotionActive"]);
        Assert.IsType<SolidColorBrush>(resources["ActiveAccentBrush"]);
    }

    [Fact]
    public void ApplyToResources_OledKeepsChromeQuiet()
    {
        var resolved = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "oled", Intensity = 1 });
        var resources = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(resources, resolved);

        var control = Assert.IsType<SolidColorBrush>(resources["ControlBorderBrush"]);
        var shell = Assert.IsType<SolidColorBrush>(resources["ShellBorderBrush"]);
        Assert.True(control.Color.A < 48);
        Assert.True(shell.Color.A < control.Color.A);
        Assert.Same(resources["AccentPrimary"], resources["ActiveAccentBrush"]);
        Assert.False((bool)resources["AppearanceMotionActive"]);
    }

    [Fact]
    public void ApplyToResources_CyberUsesBothAccentsForEdges()
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "cyber", Accent = "#FF112233", SecondaryAccent = "#FFCC3366" });
        var resources = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(resources, resolved);

        var active = Assert.IsType<LinearGradientBrush>(resources["ActiveAccentBrush"]);
        Assert.Contains(active.GradientStops, stop => stop.Color.R == 17 && stop.Color.G == 34 && stop.Color.B == 51);
        Assert.Contains(active.GradientStops, stop => stop.Color.R == 204 && stop.Color.G == 51 && stop.Color.B == 102);
        Assert.IsType<LinearGradientBrush>(resources["ShellBorderBrush"]);
        Assert.IsType<LinearGradientBrush>(resources["MediaFrameBrush"]);
        Assert.False((bool)resources["AppearanceMotionActive"]);
    }

    [Fact]
    public void ApplyToResources_RgbMotionUsesOneSharedAnimatedAccentBrush()
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "rgb", MotionEnabled = true, Intensity = 1 });
        var resources = new ResourceDictionary();

        AppearanceResolver.ApplyToResources(resources, resolved);

        var accent = Assert.IsType<LinearGradientBrush>(resources["ActiveAccentBrush"]);
        var rotate = Assert.IsType<RotateTransform>(accent.RelativeTransform);
        Assert.True(rotate.HasAnimatedProperties);
        Assert.Same(accent, resources["ShellBorderBrush"]);
        Assert.Same(accent, resources["MediaFrameBrush"]);
        Assert.True((bool)resources["AppearanceMotionActive"]);
    }

    [Fact]
    public void ApplyToResources_RgbMotionDisabledIsStatic()
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "rgb", MotionEnabled = false, Intensity = 1 });
        var resources = new ResourceDictionary();

        AppearanceResolver.ApplyToResources(resources, resolved);

        var accent = Assert.IsType<LinearGradientBrush>(resources["ActiveAccentBrush"]);
        Assert.False(accent.HasAnimatedProperties);
        Assert.False((bool)resources["AppearanceMotionActive"]);
    }

    [Fact]
    public void ApplyToResources_RgbZeroIntensityUsesBaselineSemantics()
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "rgb", MotionEnabled = true, Intensity = 0 });
        var resources = new ResourceDictionary();

        AppearanceResolver.ApplyToResources(resources, resolved);

        Assert.Same(resources["AccentPrimary"], resources["ActiveAccentBrush"]);
        Assert.Same(resources["StrokeSubtle"], resources["ShellBorderBrush"]);
        Assert.Same(resources["StrokeSubtle"], resources["MediaFrameBrush"]);
        Assert.False((bool)resources["AppearanceMotionActive"]);
    }

    [Fact]
    public void ApplyToResources_RgbIntensityControlsSharedGradientAlpha()
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "rgb", MotionEnabled = false, Intensity = 0.5 });
        var resources = new ResourceDictionary();

        AppearanceResolver.ApplyToResources(resources, resolved);

        var accent = Assert.IsType<LinearGradientBrush>(resources["ActiveAccentBrush"]);
        Assert.Equal(127, accent.GradientStops[0].Color.A);
        Assert.Same(accent, resources["ShellBorderBrush"]);
        Assert.Same(accent, resources["MediaFrameBrush"]);
    }

    [Fact]
    public void ApplyToResources_SwitchingAwayFromRgbStopsOldAnimationAndResetsSemantics()
    {
        var resources = new ResourceDictionary();
        var rgb = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "rgb", MotionEnabled = true });
        AppearanceResolver.ApplyToResources(resources, rgb);
        var animated = Assert.IsType<LinearGradientBrush>(resources["ActiveAccentBrush"]);
        var rotate = Assert.IsType<RotateTransform>(animated.RelativeTransform);
        Assert.True(rotate.HasAnimatedProperties);

        AppearanceResolver.ApplyToResources(
            resources,
            AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "pixel" }));

        Assert.False(rotate.HasAnimatedProperties);
        Assert.False((bool)resources["AppearanceMotionActive"]);
        Assert.Equal(new CornerRadius(0), resources["ShellCornerRadius"]);
        Assert.IsType<SolidColorBrush>(resources["ActiveAccentBrush"]);

        AppearanceResolver.ApplyToResources(
            resources,
            AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "fluid" }));

        Assert.False((bool)resources["IsPixelPreset"]);
        Assert.Equal(new CornerRadius(14), resources["ShellCornerRadius"]);
        Assert.IsType<SolidColorBrush>(resources["ActiveAccentBrush"]);
    }
}
