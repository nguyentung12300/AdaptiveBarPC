using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public class AuditRegressionTests
{
    [Fact]
    public void AutomationSeek_CommitsWithoutPointer_AndDragCommitsOnRelease() => RunInSta(() =>
    {
        using var media = MediaItemViewModel.CreatePreview(isPlaying: false);
        var content = new ContentControl { Content = media };
        var window = Host(content);
        try
        {
            var slider = Descendants(content).OfType<TouchSlider>().Single();
            var range = Assert.IsAssignableFrom<IRangeValueProvider>(UIElementAutomationPeer.CreatePeerForElement(slider).GetPattern(PatternInterface.RangeValue));
            range.SetValue(75);
            Assert.Equal(75, media.ProgressPercent, 3);
            typeof(MediaItemViewModel).GetMethod("UpdatePosition", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(media, null);
            Assert.Equal(75, media.ProgressPercent, 3);
            slider.IsInteracting = true;
            slider.Value = 25;
            Assert.Equal(75, ((TimeSpan)typeof(MediaItemViewModel).GetField("_basePosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(media)!).TotalSeconds / media.Duration.TotalSeconds * 100, 3);
            slider.IsInteracting = false;
            typeof(MediaItemViewModel).GetMethod("UpdatePosition", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(media, null);
            Assert.Equal(25, media.ProgressPercent, 3);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ToggleAutomation_ExposesState_InvokesOnce_AndNeverFocuses() => RunInSta(() =>
    {
        var calls = 0;
        var toggle = new ToggleItemViewModel { Label = "Mute" };
        toggle.ToggleCommand = new DelegateCommand(() => { calls++; toggle.IsChecked = !toggle.IsChecked; });
        var content = new ContentControl { Content = toggle };
        var window = Host(content);
        try
        {
            var button = Descendants(content).OfType<LongPressButton>().Single();
            var peer = UIElementAutomationPeer.CreatePeerForElement(button);
            var pattern = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
            Assert.Equal(System.Windows.Automation.ToggleState.Off, pattern.ToggleState);
            pattern.Toggle();
            Pump();
            Assert.Equal(1, calls);
            Assert.Equal(System.Windows.Automation.ToggleState.On, pattern.ToggleState);
            Assert.False(button.Focusable);
            Assert.False(button.IsKeyboardFocused);
            button.IsEnabled = false;
            Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => pattern.Toggle());
            Assert.Equal(1, calls);
            Assert.Null(UIElementAutomationPeer.CreatePeerForElement(new LongPressButton()).GetPattern(PatternInterface.Toggle));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void DecorativeTemplate_UsesEffectiveMotionPolicy() => RunInSta(() =>
    {
        var content = new ContentControl { Content = new DecorativeMediaItemViewModel { Width = 120 } };
        var window = Host(content);
        try
        {
            window.DataContext = new { EnableAnimations = false, BackgroundMediaMotionEnabled = false };
            Pump();
            var view = Descendants(content).OfType<DecorativeMediaView>().Single();
            Assert.False(view.MotionEnabled);
            window.DataContext = new { EnableAnimations = true, BackgroundMediaMotionEnabled = true };
            Pump();
            Assert.True(view.MotionEnabled);
            window.DataContext = new { EnableAnimations = true, BackgroundMediaMotionEnabled = false };
            Pump();
            Assert.False(view.MotionEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void MediaLayout_PreservesLegacyProfileWidth()
    {
        var result = new ProfileDocumentSerializer().Load("""
            { "id": "legacy-media", "items": [{ "type": "media", "width": 250 }] }
            """);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(250, Assert.Single(result.Document!.Items).Width);
    }

    [Theory]
    [InlineData(250)]
    [InlineData(360)]
    [InlineData(520)]
    public void MediaLayout_PreservesTouchSeekWithLongMetadata(double width) => RunInSta(() =>
    {
        using var media = MediaItemViewModel.CreatePreview(width, new string('T', 160), new string('A', 160));
        var content = new ContentControl { Content = media, HorizontalAlignment = HorizontalAlignment.Left };
        var window = Host(content);
        try
        {
            var slider = Descendants(content).OfType<TouchSlider>().Single();
            Assert.True(slider.ActualWidth >= 56, $"Seek width: {slider.ActualWidth}");
            Assert.True(slider.ActualHeight >= 56, $"Seek height: {slider.ActualHeight}");
            Assert.All(Descendants(content).OfType<LongPressButton>(), b => Assert.True(b.ActualHeight >= 56));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("fluid")]
    [InlineData("pixel")]
    public void MediaLayout_TransportAndSeekAreUnclippedWithin56Dip(string preset) => RunInSta(() =>
    {
        using var media = MediaItemViewModel.CreatePreview(width: 360);
        var content = new ContentControl { Content = media, Height = 56, VerticalAlignment = VerticalAlignment.Center };
        var window = Host(content);
        try
        {
            AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new(), new() { Preset = preset }, false));
            window.UpdateLayout();
            var transport = Descendants(content).OfType<LongPressButton>().ToArray();
            Assert.Equal(3, transport.Length);
            for (var i = 1; i < transport.Length; i++)
            {
                var previousBounds = transport[i - 1].TransformToAncestor(content).TransformBounds(new Rect(transport[i - 1].RenderSize));
                var currentBounds = transport[i].TransformToAncestor(content).TransformBounds(new Rect(transport[i].RenderSize));
                Assert.Equal(previousBounds.Right, currentBounds.Left, 6);
                Assert.Equal(previousBounds.Top, currentBounds.Top, 6);
                Assert.Equal(previousBounds.Bottom, currentBounds.Bottom, 6);
            }
            if (preset == "pixel")
            {
                // A gap between independently painted frames exposes this diagnostic background.
                var panel = (StackPanel)VisualTreeHelper.GetParent(transport[0]);
                panel.Background = Brushes.Lime;
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2, 4 })
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth * scale),
                        (int)Math.Ceiling(panel.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    var drawing = new DrawingVisual();
                    using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(panel), null, new Rect(panel.RenderSize));
                    bitmap.Render(drawing);
                    foreach (var button in transport.Skip(1))
                    {
                        var edge = button.TranslatePoint(new Point(), panel).X * scale;
                        for (var x = (int)Math.Floor(edge) - 1; x <= (int)Math.Ceiling(edge); x++)
                        {
                            var pixel = new byte[4];
                            bitmap.CopyPixels(new Int32Rect(x, bitmap.PixelHeight / 2, 1, 1), pixel, 4, 0);
                            Assert.False(pixel[0] == 0 && pixel[1] == 255 && pixel[2] == 0,
                                $"Exposed background at transport seam, scale={scale}, x={x}");
                        }
                    }
                }
            }
            foreach (var target in Descendants(content).OfType<FrameworkElement>().Where(e => e is LongPressButton or TouchSlider))
            {
                Assert.True(target.ActualHeight >= 56);
                for (Visual? ancestor = target; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor) as Visual)
                {
                    if (VisualTreeHelper.GetClip(ancestor) is { } clip)
                    {
                        var bounds = target.TransformToAncestor(ancestor).TransformBounds(new Rect(target.RenderSize));
                        Assert.True(clip.Bounds.Contains(bounds), $"{preset}: {target.GetType().Name} {bounds} clipped by {ancestor.GetType().Name} {clip.Bounds}");
                    }
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
        finally { window.Close(); }
    });

    internal static Window Host(UIElement content)
    {
        var window = new Window { Content = content, Width = 900, Height = 80, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        foreach (var resource in new[] { "ThemeTokens", "BarControls" })
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AdaptiveBarPC;component/Resources/{resource}.xaml", UriKind.Relative) });
        AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new ThemeDefinition(), new AppearanceDefinition(), false));
        window.Show();
        window.UpdateLayout();
        Pump();
        return window;
    }
    internal static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    internal static void WaitFor(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "Image load timed out.");
            Pump();
            Thread.Yield();
        }
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void StaticMedia_LoadYieldsToInput_RejectsStaleResults_AndHandlesUnloadAndErrors() => RunInSta(() =>
    {
        var paths = Enumerable.Range(0, 2).Select(_ => Path.Combine(Path.GetTempPath(), $"AdaptiveBar-audit-{Guid.NewGuid():N}.png")).ToArray();
        for (var i = 0; i < paths.Length; i++)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(new WriteableBitmap(200 * (i + 1), 100, 96, 96, PixelFormats.Bgra32, null)));
            using var stream = File.Create(paths[i]);
            encoder.Save(stream);
        }
        var view = new DecorativeMediaView { Width = 120, Height = 56, Fit = "cover", ShowDiagnostics = true };
        var window = Host(view);
        try
        {
            var image = Descendants(view).OfType<Image>().Single();
            view.Source = paths[0];
            var first = view.MediaLoadTask;
            Assert.False(first.IsCompleted); // Assignment returns before UI apply; it cannot synchronously decode/apply.
            var inputRan = false;
            view.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => inputRan = true));
            view.Source = paths[1];
            WaitFor(Task.WhenAll(first, view.MediaLoadTask));
            Assert.True(inputRan);
            Assert.True(Assert.IsAssignableFrom<BitmapSource>(image.Source).IsFrozen);
            Assert.Equal(400, image.Width);

            view.Source = paths[0] + ".missing.png";
            WaitFor(view.MediaLoadTask);
            Assert.Null(image.Source);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "Missing asset");
            File.WriteAllText(paths[0], "invalid image");
            view.Source = paths[0];
            WaitFor(view.MediaLoadTask);
            Assert.Contains(Descendants(view).OfType<TextBlock>(), t => t.Text == "Unable to decode");

            view.Source = paths[1];
            var pending = view.MediaLoadTask;
            window.Close();
            WaitFor(pending);
            Assert.Null(image.Source);
        }
        finally
        {
            window.Close();
            foreach (var path in paths) File.Delete(path);
        }
    });

    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    [InlineData("pixel")]
    public void CheckedToggle_HasReadableText_IncludingHoverAndPress(string preset) => RunInSta(() =>
    {
        var content = new ContentControl { Content = new ToggleItemViewModel { Label = "Mute", IsChecked = true } };
        var window = Host(content);
        try
        {
            AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new(), new() { Preset = preset }, false));
            window.UpdateLayout();
            var button = Descendants(content).OfType<LongPressButton>().Single();
            var foreground = ((SolidColorBrush)button.Foreground).Color;
            var backdrop = ((SolidColorBrush)window.Resources["SurfaceBase"]).Color;
            var face = (Border)button.Template.FindName("FaceBorder", button);
            var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var pressedKey = (DependencyPropertyKey)typeof(System.Windows.Controls.Primitives.ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (var (hover, pressed) in new[] { (false, false), (true, false), (true, true) })
            {
                button.SetValue(hoverKey, hover);
                button.SetValue(pressedKey, pressed);
                var background = Composite(((SolidColorBrush)face.Background).Color, backdrop);
                var contrast = Contrast(Composite(foreground, background), background);
                Assert.True(contrast >= 4.5, $"{preset} contrast {contrast:F2}");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void DesignerPrimaryButtonPalette_HasReadableTextInAllEnabledStates()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AdaptiveBarPC.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var xaml = XDocument.Load(Path.Combine(root.FullName, "src/AdaptiveBarPC.Designer/App.xaml"));
        var tokens = XDocument.Load(Path.Combine(root.FullName, "src/AdaptiveBarPC.App/Resources/ThemeTokens.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Color ReadColor(XDocument doc, string key) => (Color)ColorConverter.ConvertFromString(doc.Descendants().Single(e => (string?)e.Attribute(x + "Key") == key).Attribute("Color")!.Value);
        var text = ReadColor(tokens, "PrimaryText");
        foreach (var key in new[] { "EditorPrimaryBackground", "EditorPrimaryHover", "EditorPrimaryPressed" })
            Assert.True(Contrast(text, ReadColor(xaml, key)) >= 4.5, key);
    }

    private static Color Composite(Color a, Color b) => Color.FromRgb((byte)Math.Round((a.R * a.A + b.R * (255 - a.A)) / 255d), (byte)Math.Round((a.G * a.A + b.G * (255 - a.A)) / 255d), (byte)Math.Round((a.B * a.A + b.B * (255 - a.A)) / 255d));
    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            var rgb = new[] { c.R, c.G, c.B }.Select(v => v / 255d).Select(v => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4)).ToArray();
            return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
        }
        return (Math.Max(Luminance(a), Luminance(b)) + 0.05) / (Math.Min(Luminance(a), Luminance(b)) + 0.05);
    }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
