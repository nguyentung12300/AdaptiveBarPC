using System;
using System.Collections.Generic;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.ViewModels;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class IconFirstPresentationTests
{
    [Theory]
    [InlineData("\uE710", "\uE710")]
    [InlineData("E710", "\uE710")]
    [InlineData("0xE710", "\uE710")]
    [InlineData(@"\uE710", "\uE710")]
    [InlineData("&#xE710;", "\uE710")]
    [InlineData("  E767  ", "\uE767")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void GlyphHelper_ResolvesVariousFormats(string? input, string expected)
    {
        var resolved = GlyphHelper.Resolve(input);
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ButtonItemViewModel_WithIcon_RendersIconFirst()
    {
        var button = new ButtonItemViewModel
        {
            Label = "Calculator",
            Icon = "E71D",
            IconKind = "glyph"
        };

        Assert.True(button.HasIcon);
        Assert.Equal("\uE71D", button.DisplayGlyph);
        Assert.Equal("Calculator", button.Label);
    }

    [Theory]
    [InlineData(null, "glyph")]
    [InlineData("", "glyph")]
    [InlineData("   ", "glyph")]
    [InlineData("E710", "none")]
    public void ButtonItemViewModel_WithoutValidIcon_FallsBackToLabel(string? icon, string? kind)
    {
        var button = new ButtonItemViewModel
        {
            Label = "Notepad",
            Icon = icon,
            IconKind = kind
        };

        Assert.False(button.HasIcon);
        Assert.Equal("Notepad", button.Label);
    }

    [Fact]
    public void ToggleItemViewModel_IconFirstAndStateBinding()
    {
        var toggle = new ToggleItemViewModel
        {
            Label = "Mute",
            Icon = "E74F",
            IconKind = "glyph",
            IsChecked = false
        };

        Assert.True(toggle.HasIcon);
        Assert.Equal("\uE74F", toggle.DisplayGlyph);
        Assert.Equal("Mute", toggle.DisplayLabel);

        toggle.IsChecked = true;
        Assert.True(toggle.IsChecked);
        Assert.True(toggle.HasIcon);
        Assert.Equal("\uE74F", toggle.DisplayGlyph);
    }

    [Fact]
    public void ToggleItemViewModel_WithoutIcon_FallsBackToLabel()
    {
        var toggle = new ToggleItemViewModel
        {
            Label = "Bluetooth",
            OnLabel = "BT On",
            OffLabel = "BT Off",
            IsChecked = false
        };

        Assert.False(toggle.HasIcon);
        Assert.Equal("BT Off", toggle.DisplayLabel);

        toggle.IsChecked = true;
        Assert.Equal("BT On", toggle.DisplayLabel);
    }

    [Fact]
    public void SliderItemViewModel_DerivesSemanticGlyphs()
    {
        var volSlider = new SliderItemViewModel("Volume", 180, 0, 100, 75, null);
        Assert.Equal("\uE767", volSlider.DisplayGlyph);

        volSlider.Value = 0;
        Assert.Equal("\uE74F", volSlider.DisplayGlyph);

        var brightSlider = new SliderItemViewModel("Screen Brightness", 180, 0, 100, 50, null);
        Assert.Equal("\uE706", brightSlider.DisplayGlyph);

        var micSlider = new SliderItemViewModel("Mic Gain", 180, 0, 100, 50, null);
        Assert.Equal("\uE720", micSlider.DisplayGlyph);
    }

    [Fact]
    public void SliderItemViewModel_CustomIconOverridesSemantic()
    {
        var customSlider = new SliderItemViewModel("Volume", 180, 0, 100, 50, null)
        {
            Icon = "E7F4",
            IconKind = "glyph"
        };

        Assert.True(customSlider.HasIcon);
        Assert.Equal("\uE7F4", customSlider.DisplayGlyph);
    }

    [Fact]
    public void ExpandableSliderItemViewModel_DerivesDefaultIcons()
    {
        var volExp = new ExpandableSliderItemViewModel("Sound Volume", 46, 0, 100, 50, null, null, true);
        Assert.Equal("\uE767", volExp.Icon);

        var brightExp = new ExpandableSliderItemViewModel("Display Brightness", 46, 0, 100, 50, null, null, true);
        Assert.Equal("\uE706", brightExp.Icon);

        var micExp = new ExpandableSliderItemViewModel("Microphone", 46, 0, 100, 50, null, null, true);
        Assert.Equal("\uE720", micExp.Icon);

        var customExp = new ExpandableSliderItemViewModel("Custom", 46, 0, 100, 50, null, "E790", true);
        Assert.Equal("\uE790", customExp.Icon);
    }

    [Fact]
    public void SensorItemViewModel_DerivesMetricGlyphs()
    {
        var cpu = new SensorItemViewModel { Metric = "cpu", Label = "CPU Load", Value = 42, Unit = "%" };
        Assert.Equal("\uE950", cpu.Glyph);
        Assert.Equal("42%", cpu.ValueText);

        var temp = new SensorItemViewModel { Metric = "temp", Label = "CPU Temp", Value = 67, Unit = "°C" };
        Assert.Equal("\uE7A3", temp.Glyph);
        Assert.Equal("67°C", temp.ValueText);

        var gpu = new SensorItemViewModel { Metric = "gpu", Label = "GPU Usage", Value = 80, Unit = "%" };
        Assert.Equal("\uE7F4", gpu.Glyph);

        var ram = new SensorItemViewModel { Metric = "ram", Label = "RAM Usage", Value = 55, Unit = "%" };
        Assert.Equal("\uE964", ram.Glyph);

        var fan = new SensorItemViewModel { Metric = "fan", Label = "Fan Speed", Value = 1800, Unit = "RPM" };
        Assert.Equal("\uE9D9", fan.Glyph);

        var batt = new SensorItemViewModel { Metric = "battery", Label = "Battery", Value = 95, Unit = "%" };
        Assert.Equal("\uE83F", batt.Glyph);
    }

    [Fact]
    public void SensorItemViewModel_ConfiguredIconOverridesDefault()
    {
        var sensor = new SensorItemViewModel
        {
            Metric = "cpu",
            Label = "CPU",
            Icon = "E710",
            IconKind = "glyph"
        };

        Assert.True(sensor.HasIcon);
        Assert.Equal("\uE710", sensor.Glyph);
    }

    [Fact]
    public void AudioSessionItemViewModel_DerivesAppGlyphs()
    {
        var spotify = new AudioSessionItemViewModel("Spotify.exe", 180, 0, 100, 70, false, null, null);
        Assert.Equal("\uE8D6", spotify.AppGlyph);

        var chrome = new AudioSessionItemViewModel("Google Chrome", 180, 0, 100, 60, false, null, null);
        Assert.Equal("\uE774", chrome.AppGlyph);

        var discord = new AudioSessionItemViewModel("Discord", 180, 0, 100, 80, false, null, null);
        Assert.Equal("\uE720", discord.AppGlyph);

        var game = new AudioSessionItemViewModel("Cyberpunk2077 Game", 180, 0, 100, 90, false, null, null);
        Assert.Equal("\uE7FC", game.AppGlyph);

        var system = new AudioSessionItemViewModel("System Sounds", 180, 0, 100, 50, false, null, null);
        Assert.Equal("\uE770", system.AppGlyph);

        var unknown = new AudioSessionItemViewModel("ToolApp", 180, 0, 100, 50, false, null, null);
        Assert.Equal("\uE71D", unknown.AppGlyph);
    }

    [Fact]
    public void MultiStateItemViewModel_GlyphAndLabelFallback()
    {
        var states = new List<WidgetStateDefinition>
        {
            new() { Id = "s1", Label = "State 1", Glyph = "E710" },
            new() { Id = "s2", Label = "State 2", Glyph = null }
        };

        var multi = new MultiStateItemViewModel
        {
            States = states,
            CurrentIndex = 0
        };

        Assert.True(multi.HasGlyph);
        Assert.Equal("\uE710", multi.DisplayGlyph);
        Assert.Equal("State 1", multi.DisplayLabel);

        multi.CurrentIndex = 1;
        Assert.False(multi.HasGlyph);
        Assert.Equal("State 2", multi.DisplayLabel);
    }
}
