using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class PixelAppearanceTests
{
    [Fact]
    public void MediaButtons_FollowLiveCustomColorsInPreviewResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var surface = new StackPanel { Orientation = Orientation.Horizontal };
                surface.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/AdaptiveBarPC;component/Resources/ThemeTokens.xaml", UriKind.Relative)
                });
                surface.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/AdaptiveBarPC;component/Resources/BarControls.xaml", UriKind.Relative)
                });
                var previous = new LongPressButton
                {
                    Style = (Style)surface.FindResource("MediaTransportButtonStyle"),
                    Content = new TextBlock { Text = "Previous" }
                };
                var play = new LongPressButton
                {
                    Style = (Style)surface.FindResource("MediaPlayPauseButtonStyle"),
                    Content = new TextBlock { Text = "Play" }
                };
                surface.Children.Add(previous);
                surface.Children.Add(play);
                foreach (var (surfaceColor, textColor, accentColor) in new[]
                {
                    ("#FF123456", "#FFABCDEF", "#FFAA3322"),
                    ("#FF654321", "#FFFEDCBA", "#FF22AA33")
                })
                {
                    AppearanceResolver.ApplyToResources(surface.Resources, AppearanceResolver.Resolve(
                        new ThemeDefinition { TileBackground = surfaceColor, SecondaryText = textColor },
                        new AppearanceDefinition { Preset = "pixel", Accent = accentColor }));
                    surface.Measure(new Size(200, 60));
                    surface.Arrange(new Rect(0, 0, 200, 60));
                    surface.UpdateLayout();
                    var previousFace = (Border)previous.Template.FindName("FaceBorder", previous);
                    var playFace = (Border)play.Template.FindName("FaceBorder", play);
                    Assert.Equal(previousFace.RenderSize, playFace.RenderSize);
                    Assert.Equal(previousFace.TranslatePoint(new Point(), previous), playFace.TranslatePoint(new Point(), play));
                    Assert.Equal(previous.Margin, play.Margin);
                    foreach (var button in new[] { previous, play })
                    {
                        var expectedFace = (Color)ColorConverter.ConvertFromString(surfaceColor);
                        var expectedIcon = (Color)ColorConverter.ConvertFromString(button == play ? accentColor : textColor);
                        var face = Assert.IsType<Border>(button.Template.FindName("FaceBorder", button));
                        Assert.Equal(expectedFace, Assert.IsType<SolidColorBrush>(face.Background).Color);
                        Assert.Equal(expectedIcon, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
                        Assert.Equal(expectedIcon, Assert.IsType<SolidColorBrush>(((TextBlock)button.Content).Foreground).Color);
                        Assert.False(button.Focusable);
                        Assert.False(button.IsTabStop);
                    }
                }
                AppearanceResolver.ApplyToResources(surface.Resources, AppearanceResolver.Resolve(
                    new ThemeDefinition(), new AppearanceDefinition { Preset = "fluid" }));
                surface.UpdateLayout();
                Assert.Equal(0, Assert.IsType<SolidColorBrush>(previous.Background).Color.A);
                Assert.Same(surface.Resources["AccentSubtle"], play.Background);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Resolve_FluidPreset_RemainsSmoothAndRounded()
    {
        var resolved = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "fluid" });

        Assert.Equal("fluid", resolved.Preset);
        Assert.False(resolved.IsPixel);
        Assert.Equal(8.0, resolved.ControlCornerRadius);
        Assert.Equal(12.0, resolved.SurfaceCornerRadius);
        Assert.Equal(BitmapScalingMode.Linear, resolved.IconScalingMode);
    }

    [Fact]
    public void Resolve_PixelPreset_ResolvesZeroRadiusAndPixelTokens()
    {
        var resolved = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "pixel" });

        Assert.Equal("pixel", resolved.Preset);
        Assert.True(resolved.IsPixel);
        Assert.Equal(0.0, resolved.ControlCornerRadius);
        Assert.Equal(0.0, resolved.SurfaceCornerRadius);
        Assert.Equal("#FF000000", resolved.PixelOutline);
        Assert.Equal("#45FFFFFF", resolved.PixelHighlight);
        Assert.Equal("#C0FF0055", resolved.PixelShadow);
        Assert.Equal(BitmapScalingMode.NearestNeighbor, resolved.IconScalingMode);
    }

    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    public void NonPixelPresets_DoNotReceivePixelSegmentedOrNearestNeighborMode(string preset)
    {
        var resolved = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = preset });

        Assert.False(resolved.IsPixel);
        Assert.Equal(BitmapScalingMode.Linear, resolved.IconScalingMode);

        var dict = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(dict, resolved);

        Assert.False((bool)dict["IsPixelPreset"]);
        Assert.Equal(BitmapScalingMode.Linear, dict["IconBitmapScalingMode"]);
        Assert.IsType<SolidColorBrush>(dict["SliderTrackForegroundBrush"]);
        Assert.Equal(new Thickness(0), (Thickness)dict["ButtonPressMargin"]);
        Assert.Equal(Visibility.Collapsed, (Visibility)dict["ToggleLedVisibility"]);
    }

    [Fact]
    public void PixelPreset_AppliesSegmentedBrushesAndPixelTokensToResources()
    {
        var resolved = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "pixel" });
        var dict = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(dict, resolved);

        Assert.True((bool)dict["IsPixelPreset"]);
        Assert.Equal(BitmapScalingMode.NearestNeighbor, dict["IconBitmapScalingMode"]);
        Assert.Equal(new CornerRadius(0), (CornerRadius)dict["ControlCornerRadius"]);
        Assert.Equal(new CornerRadius(0), (CornerRadius)dict["SurfaceCornerRadius"]);
        Assert.Equal(new CornerRadius(0), (CornerRadius)dict["ShellCornerRadius"]);
        Assert.Equal(new CornerRadius(0), (CornerRadius)dict["SliderThumbCornerRadius"]);
        Assert.Equal(new CornerRadius(0), (CornerRadius)dict["SliderTrackCornerRadius"]);
        Assert.Equal(8.0, (double)dict["SliderTrackHeight"]);
        Assert.Equal(8.0, (double)dict["ProgressBarHeight"]);
        Assert.Equal(new Thickness(0, 1.5, 0, 0), (Thickness)dict["ButtonPressMargin"]);
        Assert.Equal(Visibility.Visible, (Visibility)dict["ToggleLedVisibility"]);
        Assert.IsType<SolidColorBrush>(dict["ControlBorderBrush"]);
        Assert.IsType<SolidColorBrush>(dict["ShellBorderBrush"]);
        Assert.IsType<SolidColorBrush>(dict["MediaFrameBrush"]);
        Assert.False((bool)dict["AppearanceMotionActive"]);

        // Segmented brushes for HUD track
        var bgTrack = Assert.IsType<DrawingBrush>(dict["SliderTrackBackgroundBrush"]);
        Assert.Equal(TileMode.Tile, bgTrack.TileMode);
        Assert.Equal(BrushMappingMode.Absolute, bgTrack.ViewportUnits);

        var fgTrack = Assert.IsType<DrawingBrush>(dict["SliderTrackForegroundBrush"]);
        Assert.Equal(TileMode.Tile, fgTrack.TileMode);
        Assert.Equal(BrushMappingMode.Absolute, fgTrack.ViewportUnits);
    }

    [Fact]
    public void SecondaryAccent_ContributesToMeaningfulResolvedSemanticPath_InPixelPreset()
    {
        // 1. Initial Pixel appearance: Primary Cyan (#00E5FF), Secondary Magenta (#FF0055)
        var pixel1 = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "pixel", Accent = "#FF00E5FF", SecondaryAccent = "#FFFF0055" });

        var dict1 = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(dict1, pixel1);

        var thumbBrush1 = Assert.IsType<SolidColorBrush>(dict1["SliderThumbAccentBrush"]);
        var ledBrush1 = Assert.IsType<SolidColorBrush>(dict1["ToggleLedActiveBrush"]);
        var sensorBrush1 = Assert.IsType<SolidColorBrush>(dict1["SensorIconForeground"]);
        var shadowBrush1 = Assert.IsType<SolidColorBrush>(dict1["PixelShadowBrush"]);

        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x55), thumbBrush1.Color);
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x55), ledBrush1.Color);
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x55), sensorBrush1.Color);
        Assert.Equal(0xC0, shadowBrush1.Color.A);
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x55), Color.FromRgb(shadowBrush1.Color.R, shadowBrush1.Color.G, shadowBrush1.Color.B));

        // 2. Change Secondary Accent to Yellow (#FFFF00) while Primary Accent stays Cyan (#00E5FF)
        var pixel2 = AppearanceResolver.Resolve(
            new ThemeDefinition(),
            new AppearanceDefinition { Preset = "pixel", Accent = "#FF00E5FF", SecondaryAccent = "#FFFFFF00" });

        var dict2 = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(dict2, pixel2);

        var thumbBrush2 = Assert.IsType<SolidColorBrush>(dict2["SliderThumbAccentBrush"]);
        var ledBrush2 = Assert.IsType<SolidColorBrush>(dict2["ToggleLedActiveBrush"]);
        var sensorBrush2 = Assert.IsType<SolidColorBrush>(dict2["SensorIconForeground"]);
        var shadowBrush2 = Assert.IsType<SolidColorBrush>(dict2["PixelShadowBrush"]);
        var primaryBrush2 = Assert.IsType<SolidColorBrush>(dict2["AccentPrimary"]);

        // Primary Accent must remain Cyan
        Assert.Equal(Color.FromRgb(0x00, 0xE5, 0xFF), primaryBrush2.Color);

        // Secondary Accent elements must have visibly changed to Yellow
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0x00), thumbBrush2.Color);
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0x00), ledBrush2.Color);
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0x00), sensorBrush2.Color);
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(shadowBrush2.Color.R, shadowBrush2.Color.G, shadowBrush2.Color.B));
    }

    [Fact]
    public void PaletteMapping_Background_Surface_Text_MapToExpectedResources()
    {
        var theme = new ThemeDefinition
        {
            Background = "#FF101112",
            TileBackground = "#FF202122",
            PrimaryText = "#FFE0E1E2",
            SecondaryText = "#FF909192"
        };
        var appearance = new AppearanceDefinition { Preset = "pixel" };

        var resolved = AppearanceResolver.Resolve(theme, appearance);
        var dict = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(dict, resolved);

        var bgBrush = Assert.IsType<SolidColorBrush>(dict["SurfaceBase"]);
        var barBg = Assert.IsType<SolidColorBrush>(dict["BarBackground"]);
        var surfBrush = Assert.IsType<SolidColorBrush>(dict["SurfaceControl"]);
        var tileBg = Assert.IsType<SolidColorBrush>(dict["TileBackground"]);
        var priTxt = Assert.IsType<SolidColorBrush>(dict["TextPrimary"]);
        var secTxt = Assert.IsType<SolidColorBrush>(dict["TextSecondary"]);

        Assert.Equal(Color.FromRgb(0x10, 0x11, 0x12), bgBrush.Color);
        Assert.Equal(Color.FromRgb(0x10, 0x11, 0x12), barBg.Color);
        Assert.Equal(Color.FromRgb(0x20, 0x21, 0x22), surfBrush.Color);
        Assert.Equal(Color.FromRgb(0x20, 0x21, 0x22), tileBg.Color);
        Assert.Equal(Color.FromRgb(0xE0, 0xE1, 0xE2), priTxt.Color);
        Assert.Equal(Color.FromRgb(0x90, 0x91, 0x92), secTxt.Color);
    }

    [Fact]
    public void PixelTypography_ResolvesRetroFontAndUppercaseCommandLabels()
    {
        var pixel = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "pixel" });
        var dictPixel = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(dictPixel, pixel);

        var pixelFont = Assert.IsType<System.Windows.Media.FontFamily>(dictPixel["BarFontFamily"]);
        Assert.Contains("VT323", pixelFont.Source);
        Assert.Equal(FontWeights.Normal, dictPixel["BarFontWeight"]);
        var pixelIconFont = Assert.IsType<FontFamily>(dictPixel["IconFontFamily"]);
        Assert.Contains("pixelart-icons-font", pixelIconFont.Source);

        var fluid = AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition { Preset = "fluid" });
        var dictFluid = new ResourceDictionary();
        AppearanceResolver.ApplyToResources(dictFluid, fluid);

        var fluidFont = Assert.IsType<System.Windows.Media.FontFamily>(dictFluid["BarFontFamily"]);
        Assert.Contains("Segoe UI", fluidFont.Source);
        Assert.Equal(FontWeights.SemiBold, dictFluid["BarFontWeight"]);

        // Verify CommandLabelConverter behavior
        var converter = new AdaptiveBarPC.Resources.CommandLabelConverter();

        // Pixel mode with short command labels -> Uppercase
        Assert.Equal("PLAY", converter.Convert(["Play", true], typeof(string), null!, null!));
        Assert.Equal("MUTE", converter.Convert(["Mute", true], typeof(string), null!, null!));
        Assert.Equal("CPU", converter.Convert(["Cpu", true], typeof(string), null!, null!));
        Assert.Equal("EXPLORER", converter.Convert(["Explorer", true], typeof(string), null!, null!));

        // Pixel mode with long dynamic metadata (> 14 chars) -> Preserved mixed-case
        Assert.Equal("Bohemian Rhapsody", converter.Convert(["Bohemian Rhapsody", true], typeof(string), null!, null!));
        Assert.Equal("Queen - Greatest Hits", converter.Convert(["Queen - Greatest Hits", true], typeof(string), null!, null!));

        // Fluid mode (isPixel = false) -> Preserved mixed-case
        Assert.Equal("Play", converter.Convert(["Play", false], typeof(string), null!, null!));
        Assert.Equal("Mute", converter.Convert(["Mute", false], typeof(string), null!, null!));
    }

    [Fact]
    public void PixelIcons_UsePixelarticonsWhileOtherThemesKeepFluentGlyphs()
    {
        var converter = new AdaptiveBarPC.Resources.PixelGlyphConverter();

        Assert.Equal("\uECD3", converter.Convert(["\uE768", true], typeof(string), null!, null!));
        Assert.Equal("\uE768", converter.Convert(["\uE768", false], typeof(string), null!, null!));
    }

    [Fact]
    public void SecondaryTextColor_AlsoDrivesMutedMetadata()
    {
        var resolved = AppearanceResolver.Resolve(
            new ThemeDefinition { SecondaryText = "#FF12AB34" },
            new AppearanceDefinition { Preset = "pixel" });
        var resources = new ResourceDictionary();

        AppearanceResolver.ApplyToResources(resources, resolved);

        Assert.Same(resources["SecondaryText"], resources["TextMuted"]);
        Assert.Equal(Color.FromRgb(0x12, 0xAB, 0x34), Assert.IsType<SolidColorBrush>(resources["TextMuted"]).Color);
    }
}
