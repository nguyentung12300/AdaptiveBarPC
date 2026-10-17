using System;
using System.IO;
using Xunit;

namespace AdaptiveBarPC.Tests;

public sealed class BarControlsRedesignTests
{
    private static string GetBarControlsXaml()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        var path = Path.Combine(dir, "src", "AdaptiveBarPC.App", "Resources", "BarControls.xaml");
        Assert.True(File.Exists(path), $"BarControls.xaml not found at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void BarControlsXaml_DefinesFluidHardwareSurfacePrimitives()
    {
        var xaml = GetBarControlsXaml();

        // Standard 46 DIP control styles
        Assert.Contains("x:Key=\"FlexSliderStyle\"", xaml);
        Assert.Contains("x:Key=\"FlexProgressBarStyle\"", xaml);
        Assert.Contains("x:Key=\"MediaTransportButtonStyle\"", xaml);
        Assert.Contains("x:Key=\"MediaPlayPauseButtonStyle\"", xaml);
        Assert.Contains("x:Key=\"FlexPillButtonStyle\"", xaml);
        Assert.Contains("x:Key=\"FlexNavButtonStyle\"", xaml);

        // Uses ThemeTokens semantic brush hierarchy
        Assert.Contains("SurfaceControl", xaml);
        Assert.Contains("SurfaceContainer", xaml);
        Assert.Contains("ControlBorderBrush", xaml);
        Assert.Contains("AccentPrimary", xaml);
    }

    [Fact]
    public void BarControlsXaml_ContainsAllCanonicalWidgetTemplates()
    {
        var xaml = GetBarControlsXaml();

        Assert.Contains("DataType=\"{x:Type vm:ButtonItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:SliderItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:ExpandableSliderItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:MasterVolumeItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:MediaItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:SpacerItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:DynamicListItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:ToggleItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:MultiStateItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:SensorItemViewModel}\"", xaml);
        Assert.Contains("DataType=\"{x:Type vm:AudioSessionItemViewModel}\"", xaml);
    }

    [Fact]
    public void MediaTemplate_ArtworkIsVisualOnlyAndHasFallback()
    {
        var xaml = GetBarControlsXaml();

        Assert.Contains("x:Name=\"ArtworkFrame\"", xaml);
        Assert.Contains("BorderBrush=\"{DynamicResource MediaFrameBrush}\"", xaml);
        Assert.Contains("ImageSource=\"{Binding Artwork}\"", xaml);
        Assert.Contains("Binding=\"{Binding HasArtwork}\"", xaml);
        Assert.Contains("IsHitTestVisible=\"False\"", xaml);
        Assert.Contains("Focusable=\"False\"", xaml);
        Assert.Contains("x:Name=\"ArtworkFallbackGlyph\"", xaml);
    }

    [Fact]
    public void FlexSliderStyle_UsesStandardizedDimensions()
    {
        var xaml = GetBarControlsXaml();

        // 6 DIP capsule track (or SliderTrackHeight token) & 18 DIP thumb
        Assert.True(xaml.Contains("Height=\"6\"") || xaml.Contains("SliderTrackHeight"), "Slider should use standardized 6 DIP track height or SliderTrackHeight token");
        Assert.True(xaml.Contains("CornerRadius=\"3\"") || xaml.Contains("SliderTrackCornerRadius"), "Slider should use standardized 3 DIP corner radius or SliderTrackCornerRadius token");
        Assert.Contains("Width=\"18\" Height=\"18\"", xaml);
    }

    [Fact]
    public void ButtonItemTemplate_DefinesEnhancedSecondaryActionIndicator()
    {
        var xaml = GetBarControlsXaml();

        // Indicator pill bar with AccentPrimary fill
        Assert.Contains("Background=\"{DynamicResource AccentPrimary}\"", xaml);
        Assert.Contains("HasSecondaryAction", xaml);
    }

    [Fact]
    public void ToggleItemTemplate_DefinesAccentActiveFeedback()
    {
        var xaml = GetBarControlsXaml();

        Assert.Contains("Binding=\"{Binding IsChecked}\"", xaml);
        Assert.Contains("AccentSubtle", xaml);
    }

    [Fact]
    public void AudioSessionTemplate_DefinesVisibleStatusErrorMuteFeedback()
    {
        var xaml = GetBarControlsXaml();

        Assert.Contains("Binding=\"{Binding IsMuted}\"", xaml);
        Assert.Contains("StatusError", xaml);
    }

    [Fact]
    public void SensorTemplate_UsesPixelarticonsOnlyForPixelPreset()
    {
        var xaml = GetBarControlsXaml();
        var sensorStart = xaml.IndexOf("DataType=\"{x:Type vm:SensorItemViewModel}\"", StringComparison.Ordinal);
        var sensorEnd = xaml.IndexOf("DataType=\"{x:Type vm:AudioSessionItemViewModel}\"", sensorStart, StringComparison.Ordinal);
        var sensorXaml = xaml[sensorStart..sensorEnd];

        Assert.Contains("x:Key=\"PixelCpuIcon\"", xaml);
        Assert.Contains("x:Key=\"PixelGpuIcon\"", xaml);
        Assert.Contains("x:Key=\"PixelTemperatureIcon\"", xaml);
        Assert.Contains("x:Key=\"PixelMemoryIcon\"", xaml);
        Assert.Contains("x:Name=\"SensorRoot\"", sensorXaml);
        Assert.Contains("x:Name=\"SensorGlyphIcon\"", sensorXaml);
        Assert.Contains("x:Name=\"PixelSensorIcon\"", sensorXaml);
        Assert.Contains("Stretch=\"None\"", sensorXaml);
        Assert.Contains("RenderOptions.EdgeMode=\"Aliased\"", sensorXaml);
        Assert.Contains("Binding=\"{Binding Tag, ElementName=SensorRoot}\" Value=\"True\"", sensorXaml);
    }
}
