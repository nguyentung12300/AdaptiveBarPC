using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using static AdaptiveBarPC.Tests.AuditRegressionTests;

namespace AdaptiveBarPC.Tests;

public class StateAuditRegressionTests
{
    internal const string AnimatedGif = "R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAAgABAAAIBQABAAgIACH5BAEKAAEALAAAAAACAAEAgQD/AAAAAAAAAAAAAAgFAAEACAgAOw==";

    [Fact]
    public async Task GifFrames_SupportSingleTransparentFrame()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AdaptiveBar-transparent-{Guid.NewGuid():N}.gif");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String("R0lGODlhAQABAIEAAAAAAAAAAAAAAAAAACH5BAEAAAAALAAAAAABAAEAAAgEAAEEBAA7"));
        try
        {
            var loaded = await Task.Run(() => GifImageLoader.Load(path, CancellationToken.None));
            Assert.NotNull(loaded);
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(Assert.Single(loaded.Value.Animation.KeyFrames.Cast<ObjectKeyFrame>()).Value);
            var pixel = new byte[4];
            bitmap.CopyPixels(pixel, 4, 0);
            Assert.Equal(0, pixel[3]);
        }
        finally { File.Delete(path); }
    }

    // 4x1 GIFs with a one-pixel subframe, different disposal modes and 70/130/210 ms delays.
    [Theory]
    [InlineData(2, "R0lGODlhBAABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEBwAAACwAAAAABAABAAAIBgABCAQQEAAh+QQJDQACACwBAAAAAQABAIH/AAAA/wAAAAAAAAAIBAADBAQAIfkEBRUAAAAsAQAAAAMAAQCBAAAA/wAAAAD/AAAACAYAAQQQEBAAOw==")]
    [InlineData(3, "R0lGODlhBAABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEBwAAACwAAAAABAABAAAIBgABCAQQEAAh+QQNDQACACwBAAAAAQABAIH/AAAA/wAAAAAAAAAIBAADBAQAIfkEBRUAAgAsAQAAAAMAAQCB/wAAAAD/AAAAAAAACAYAAQgIEBAAOw==")]
    public async Task GifFrames_AreCompositedFrozenAndTimed_WithoutHoldingTheFile(int disposal, string data)
    {
        var path = Path.Combine(Path.GetTempPath(), $"AdaptiveBar-frames-{Guid.NewGuid():N}.gif");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(data));
        try
        {
            var loaded = await Task.Run(() => GifImageLoader.Load(path, CancellationToken.None));
            Assert.NotNull(loaded);
            var (animation, size) = loaded.Value;
            File.Delete(path);
            Assert.True(animation.IsFrozen);
            Assert.Equal(new Size(4, 1), size);
            Assert.Equal(3, animation.KeyFrames.Count);
            Assert.Equal(new[] { 0d, 70d, 200d }, animation.KeyFrames.Cast<ObjectKeyFrame>().Select(k => k.KeyTime.TimeSpan.TotalMilliseconds));
            Assert.Equal(410, animation.Duration.TimeSpan.TotalMilliseconds);
            var pixels = animation.KeyFrames.Cast<ObjectKeyFrame>().Select(k =>
            {
                var bitmap = Assert.IsAssignableFrom<BitmapSource>(k.Value);
                Assert.True(bitmap.IsFrozen);
                Assert.Equal(4, bitmap.PixelWidth);
                Assert.Equal(1, bitmap.PixelHeight);
                var buffer = new byte[16];
                bitmap.CopyPixels(buffer, 16, 0);
                return buffer;
            }).ToArray();
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[0][4..8]);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[1][4..8]);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[2][0..4]);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels[2][12..16]);
            if (disposal == 3) Assert.Equal(pixels[0][4..8], pixels[2][4..8]);
            else Assert.NotEqual(pixels[1][4..8], pixels[2][4..8]);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    [InlineData("pixel")]
    public void MultiState_RefreshesGlyphAndText_WhenCyclingAndReplacingStates(string preset) => RunInSta(() =>
    {
        var model = new MultiStateItemViewModel
        {
            Width = 110,
            States = [new() { Label = "Play", Glyph = "E768" }, new() { Label = "Pause", Glyph = "E769" }, new() { Label = "Text only" }]
        };
        var calls = 0;
        model.CycleCommand = new DelegateCommand(() => { calls++; model.CurrentIndex = (model.CurrentIndex + 1) % model.States.Count; });
        var content = new ContentControl { Content = model };
        var window = Host(content);
        try
        {
            AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new(), new() { Preset = preset }, false));
            Pump();
            var button = Descendants(content).OfType<LongPressButton>().Single();
            var peer = UIElementAutomationPeer.CreatePeerForElement(button);
            var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
            var glyph = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "StateGlyph");
            var label = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "StateLabel");
            var play = glyph.Text;
            invoke.Invoke();
            Pump();
            Assert.Equal("Pause", peer.GetName());
            Assert.NotEqual(play, glyph.Text);
            invoke.Invoke();
            Pump();
            Assert.Equal(Visibility.Collapsed, glyph.Visibility);
            Assert.Equal(Visibility.Visible, label.Visibility);
            Assert.Equal(preset == "pixel" ? "TEXT ONLY" : "Text only", label.Text);
            model.States = [new(), new(), new() { Label = "Replaced", Glyph = "E768" }];
            Pump();
            Assert.Equal(play, glyph.Text);
            Assert.Equal(Visibility.Visible, glyph.Visibility);
            Assert.Equal(Visibility.Collapsed, label.Visibility);
            Assert.Equal("Replaced", peer.GetName());
            model.States = [];
            Pump();
            Assert.Equal(Visibility.Collapsed, glyph.Visibility);
            Assert.Equal(Visibility.Visible, label.Visibility);
            Assert.Equal(2, calls);
            Assert.False(button.IsKeyboardFocused);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    [InlineData("pixel")]
    public void Sensor_PreservesReadoutAtLegacyWidthsAndChangingValues(string preset) => RunInSta(() =>
    {
        var model = new SensorItemViewModel { Width = 48, Label = "CPU", Metric = "cpu_load", Value = 100, Unit = "%", Format = "{0:F0}" };
        var content = new ContentControl { Content = model, HorizontalAlignment = HorizontalAlignment.Left };
        var window = Host(content);
        try
        {
            AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new(), new() { Preset = preset }, false));
            foreach (var width in new[] { 48, 82, 96 })
            foreach (var (value, unit) in new[] { (100d, "%"), (12500d, " RPM"), (0d, "°C") })
            {
                model.Width = width;
                model.Value = value;
                model.Unit = unit;
                Pump();
                window.UpdateLayout();
                var readout = Descendants(content).OfType<TextBlock>().Single(t => t.GetBindingExpression(TextBlock.TextProperty)?.ResolvedSourcePropertyName == "ValueText");
                Assert.Equal(model.ValueText, readout.Text);
                Assert.True(readout.ActualWidth > 0);
                for (Visual? ancestor = readout; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor) as Visual)
                {
                    if (VisualTreeHelper.GetClip(ancestor) is { } clip)
                        Assert.True(clip.Bounds.Contains(readout.TransformToAncestor(ancestor).TransformBounds(new Rect(readout.RenderSize))), $"{preset}, {width}, {readout.Text}: clipped by {ancestor.GetType().Name}");
                    if (ancestor == content) break;
                }
                Assert.Equal(width, model.Width); // Rendering must not rewrite profile data.
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Gif_LoadYields_RejectsStaleResults_AndHandlesErrorsDisableAndUnload() => RunInSta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"AdaptiveBar-state-{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(path, Convert.FromBase64String(AnimatedGif));
        var view = new DecorativeMediaView { Width = 120, Height = 56, MotionEnabled = false, ShowDiagnostics = true };
        var window = Host(view);
        try
        {
            var image = Descendants(view).OfType<Image>().Single();
            view.Source = path;
            var pending = view.MediaLoadTask;
            Assert.False(pending.IsCompleted);
            var inputRan = false;
            view.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => inputRan = true));
            WaitFor(pending);
            Assert.True(inputRan);
            Assert.True(Assert.IsAssignableFrom<BitmapSource>(image.Source).IsFrozen);
            var clock = Assert.IsType<AnimationClock>(view.GifClock);
            WaitFor(Task.Delay(50));
            Assert.True(clock.IsPaused);
            Assert.Equal(RepeatBehavior.Forever, clock.Timeline.RepeatBehavior);
            view.Loop = false;
            clock = Assert.IsType<AnimationClock>(view.GifClock);
            Assert.Equal(new RepeatBehavior(1), clock.Timeline.RepeatBehavior);
            WaitFor(Task.Delay(50));
            Assert.True(clock.IsPaused);
            view.MotionEnabled = true;
            WaitFor(Task.Delay(50));
            Assert.False(clock.IsPaused);
            view.Visibility = Visibility.Hidden;
            WaitFor(Task.Delay(50));
            Assert.True(clock.IsPaused);
            view.Visibility = Visibility.Visible;
            WaitFor(Task.Delay(50));
            Assert.False(clock.IsPaused);
            WaitFor(Task.Delay(220));
            Assert.Equal(ClockState.Filling, clock.CurrentState); // A non-looping GIF ends on its last frame.

            view.Source = "";
            view.Source = path;
            pending = view.MediaLoadTask;
            view.Source = path + ".missing.gif";
            WaitFor(Task.WhenAll(pending, view.MediaLoadTask));
            Assert.Null(image.Source);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "Missing asset");

            File.WriteAllText(path, "invalid gif");
            view.Source = path;
            WaitFor(view.MediaLoadTask);
            Assert.Null(image.Source);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "Unable to decode");
            File.WriteAllBytes(path, Convert.FromBase64String(AnimatedGif));
            view.Source = "";
            view.Source = path;
            pending = view.MediaLoadTask;
            view.MediaEnabled = false;
            WaitFor(pending);
            Assert.Null(image.Source);
            view.MediaEnabled = true;
            pending = view.MediaLoadTask;
            window.Close();
            WaitFor(pending);
            Assert.Null(image.Source);
        }
        finally { window.Close(); File.Delete(path); }
    });
}
