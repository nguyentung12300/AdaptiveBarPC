using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using static AdaptiveBarPC.Tests.AuditRegressionTests;

namespace AdaptiveBarPC.Tests;

public class ReauditRegressionTests
{
    [Fact]
    public void MixerMute_ReportsAppAndState_WithSingleNonFocusingDispatch() => RunInSta(() =>
    {
        var calls = 0;
        var audio = new AudioSessionItemViewModel("Spotify", 230, 0, 100, 72, false, null, _ => calls++);
        var content = new ContentControl { Content = audio };
        var window = Host(content);
        try
        {
            var mute = Descendants(content).OfType<LongPressButton>().Single();
            var peer = UIElementAutomationPeer.CreatePeerForElement(mute);
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
            Assert.Contains("Spotify", peer.GetName());
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            toggle.Toggle();
            Pump();
            Assert.Equal(1, calls);
            Assert.True(audio.IsMuted);
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            audio.IsMuted = false; // Same state notification used by provider readback.
            audio.Label = "VLC";
            Pump();
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            Assert.Contains("VLC", peer.GetName());
            Assert.Equal(1, calls);
            Assert.False(mute.Focusable);
            Assert.False(mute.IsKeyboardFocused);
            mute.IsEnabled = false;
            Assert.Throws<ElementNotEnabledException>(() => toggle.Toggle());
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    [InlineData("pixel")]
    public void Sliders_KeepFullTouchSurface_AtNarrowWidthsAndLongLabels(string preset) => RunInSta(() =>
    {
        var content = new ContentControl { Height = 56, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var window = Host(content);
        try
        {
            AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new(), new() { Preset = preset }, false));
            foreach (var width in new[] { 480, 80, 180, 230 })
            foreach (var model in new object[] {
                new SliderItemViewModel(new string('L', 160), width, 0, 100, 65, null) { IconKind = "none" },
                new AudioSessionItemViewModel(new string('A', 160), width, 0, 100, 72, true, null, null) })
            {
                content.Content = model;
                window.UpdateLayout();
                var slider = Descendants(content).OfType<TouchSlider>().Single();
                Assert.True(slider.ActualWidth >= 72, $"{preset}, {model.GetType().Name}, width={width}: track={slider.ActualWidth}");
                foreach (var target in Descendants(content).OfType<FrameworkElement>().Where(e => e is TouchSlider or LongPressButton))
                {
                    Assert.True(target.ActualHeight >= 56);
                    for (Visual? ancestor = target; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor) as Visual)
                    {
                        if (VisualTreeHelper.GetClip(ancestor) is { } clip)
                            Assert.True(clip.Bounds.Contains(target.TransformToAncestor(ancestor).TransformBounds(new Rect(target.RenderSize))), $"{preset}, {target.GetType().Name} clipped by {ancestor.GetType().Name}: {clip.Bounds}");
                        if (ancestor == content) break;
                    }
                    foreach (var y in new[] { 0.5, target.ActualHeight - 0.5 })
                    {
                        var hit = content.InputHitTest(target.TranslatePoint(new Point(target.ActualWidth / 2, y), content)) as DependencyObject;
                        while (hit is not null && hit != target) hit = VisualTreeHelper.GetParent(hit);
                        Assert.Same(target, hit);
                    }
                }
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PreviewColors_FollowLocalPresetAndCustomTokens_NotWorkbenchBrushes() => RunInSta(() =>
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        var slider = new ContentControl { Content = new SliderItemViewModel("Volume", 180, 0, 100, 65, null) };
        var button = new ContentControl { Content = new ButtonItemViewModel { Label = "Action", Width = 84, Command = new DelegateCommand(() => { }) } };
        strip.Children.Add(slider);
        strip.Children.Add(button);
        var preview = new Border { Child = strip };
        var window = Host(preview);
        window.DataContext = new { PrimaryTextBrush = Brushes.Red, SecondaryTextBrush = Brushes.Red, AccentBrush = Brushes.Red };
        try
        {
            foreach (var preset in new[] { "fluid", "pixel", "oled", "cyber", "rgb", "fluid", "custom" })
            {
                var theme = preset == "custom" ? new ThemeDefinition { PrimaryText = "#FFEEDDCC", SecondaryText = "#FFAABBCC", Accent = "#FF44CCAA" } : new ThemeDefinition();
                AppearanceResolver.ApplyToResources(preview.Resources, AppearanceResolver.Resolve(theme, new() { Preset = preset }, false));
                window.UpdateLayout();
                Pump();
                var number = Descendants(slider).OfType<TextBlock>().Single(t => t.GetBindingExpression(TextBlock.TextProperty)?.ResolvedSourcePropertyName == "ValuePercentText");
                Assert.Same(preview.Resources["TextSecondary"], number.Foreground);
                Assert.Same(preview.Resources["AccentPrimary"], Descendants(slider).OfType<TouchSlider>().Single().Foreground);
                Assert.Same(preview.Resources["TextPrimary"], Descendants(button).OfType<LongPressButton>().Single().Foreground);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void MediaTransport_TracksSessionCapabilities_AndDisablesWhenAbsent() => RunInSta(() =>
    {
        var logFolder = Path.Combine(Path.GetTempPath(), "AdaptiveBar-capabilities-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new MediaSessionService(new AppLogger(logFolder)); // No live session manager is started.
            using var media = new MediaItemViewModel(service, 360);
            var content = new ContentControl { Content = media };
            var window = Host(content);
            try
            {
                foreach (var flags in new[] { (true, false, false), (true, true, false), (true, false, true), (true, true, true), (false, true, true) })
                {
                    var state = new MediaSessionState(flags.Item1, "test", "Track", "Artist", false, true, true, flags.Item2, flags.Item3, TimeSpan.Zero, TimeSpan.FromMinutes(3));
                    typeof(MediaItemViewModel).GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(media, [state]);
                    window.UpdateLayout();
                    Assert.Equal(flags.Item1 && flags.Item2, media.NextCommand.CanExecute(null));
                    Assert.Equal(flags.Item1 && flags.Item3, media.PreviousCommand.CanExecute(null));
                    foreach (var (name, expected) in new[] { ("Next track", flags.Item1 && flags.Item2), ("Previous track", flags.Item1 && flags.Item3) })
                    {
                        var button = Descendants(content).OfType<LongPressButton>().Single(b => AutomationProperties.GetName(b) == name);
                        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
                        Assert.Equal(expected, button.IsEnabled);
                        if (!expected) Assert.Throws<ElementNotEnabledException>(() => ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke());
                    }
                }
            }
            finally { window.Close(); }
        }
        finally
        {
            File.Delete(Path.Combine(logFolder, "adaptivebar.log"));
            Directory.Delete(logFolder);
        }
    });

    [Fact]
    public void NarrowSliderProfile_RemainsValidWithoutRewritingWidth()
    {
        var result = new ProfileDocumentSerializer().Load("""
            { "id": "legacy-slider", "items": [{ "type": "slider", "label": "Volume", "width": 80 }] }
            """);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(80, Assert.Single(result.Document!.Items).Width);
    }
}
