using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Resources;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using static AdaptiveBarPC.Tests.AuditRegressionTests;

namespace AdaptiveBarPC.Tests;

public class InteractionAuditRegressionTests
{
    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    [InlineData("pixel")]
    public void ExpandableTemplates_ExposeOnlyTheFunctionalRangeProvider(string preset) => RunInSta(() =>
    {
        var content = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left };
        var window = Host(content);
        try
        {
            AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new(), new() { Preset = preset }, false));
            var writes = new List<double>();
            foreach (var model in new SliderItemViewModel[]
            {
                new MasterVolumeItemViewModel("Volume", 56, 0, 100, 50, writes.Add),
                new ExpandableSliderItemViewModel("Brightness", 56, 0, 100, 50, writes.Add, null, true)
            })
            {
                content.Content = model;
                Pump();
                var input = Descendants(content).OfType<ExpandableTouchSlider>().Single();
                input.EnableAnimations = false;
                for (var expanded = 0; expanded < 2; expanded++)
                {
                    var peer = UIElementAutomationPeer.CreatePeerForElement(window);
                    peer.ResetChildrenCache();
                    var ranges = Peers(peer).Where(p => p.IsControlElement() && p.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider).ToArray();
                    var rangePeer = Assert.Single(ranges);
                    Assert.Equal(model is MasterVolumeItemViewModel ? "Master volume" : model.Label, rangePeer.GetName());
                    writes.Clear();
                    Assert.IsAssignableFrom<IRangeValueProvider>(rangePeer.GetPattern(PatternInterface.RangeValue)).SetValue(60 + expanded * 10);
                    Assert.Equal(60 + expanded * 10, model.Value);
                    Assert.Equal(new[] { model.Value }, writes);
                    var decoration = Descendants(content).OfType<Slider>().Single(s => s != input);
                    var decorativePeer = UIElementAutomationPeer.CreatePeerForElement(decoration);
                    Assert.False(decorativePeer.IsControlElement());
                    Assert.False(decorativePeer.IsContentElement());
                    Assert.Null(decorativePeer.GetPattern(PatternInterface.RangeValue));
                    Assert.Empty(decorativePeer.GetChildren() ?? []);
                    Assert.Equal(model.Value, decoration.Value);
                    Assert.False(input.IsKeyboardFocused);
                    if (expanded == 0)
                    {
                        var finger = new TestTouchDevice(1001, new Point(20, 28));
                        Touch(input, finger, UIElement.TouchDownEvent);
                        Touch(input, finger, UIElement.TouchUpEvent);
                        Pump();
                        Assert.True(input.IsExpanded);
                    }
                }
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("fluid")]
    [InlineData("oled")]
    [InlineData("cyber")]
    [InlineData("rgb")]
    [InlineData("pixel")]
    public void VolumeAndSensorGlyphs_RefreshWithoutRecreatingTheModel(string preset) => RunInSta(() =>
    {
        var content = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left };
        var window = Host(content);
        try
        {
            AppearanceResolver.ApplyToResources(window.Resources, AppearanceResolver.Resolve(new(), new() { Preset = preset }, false));
            foreach (var model in new SliderItemViewModel[]
            {
                new SliderItemViewModel("Volume", 180, 0, 100, 80, null),
                new MasterVolumeItemViewModel("Volume", 56, 0, 100, 80, null)
            })
            {
                content.Content = model;
                Pump();
                var glyph = Descendants(content).OfType<TextBlock>().Single(t => t.Name == (model is MasterVolumeItemViewModel ? "CollapsedVolumeGlyph" : "GlyphIcon"));
                foreach (var (value, expected) in new[] { (0, "\uE74F"), (20, "\uE993"), (50, "\uE994"), (80, "\uE767") })
                {
                    model.Value = value;
                    Pump();
                    Assert.Equal(Themed(expected, preset), glyph.Text);
                }
                model.Label = "Brightness";
                Pump();
                Assert.Equal(Themed("\uE706", preset), glyph.Text);
                model.Label = "Microphone";
                Pump();
                Assert.Equal(Themed("\uE720", preset), glyph.Text);
                model.Icon = "E713";
                model.Value = 0;
                Pump();
                Assert.Equal(Themed("\uE713", preset), glyph.Text);
            }

            var sensor = new SensorItemViewModel { Label = "Load", Metric = "cpu_load", Value = 40 };
            content.Content = sensor;
            Pump();
            var pixelIcon = Descendants(content).OfType<System.Windows.Shapes.Path>().Single(t => t.Name == "PixelSensorIcon");
            sensor.Metric = "gpu_load";
            Pump();
            if (preset == "pixel")
            {
                Assert.Equal(Visibility.Visible, pixelIcon.Visibility);
                Assert.Same(window.FindResource("PixelGpuIcon"), pixelIcon.Data);
            }
            else Assert.Equal("\uE7F4", Descendants(content).OfType<TextBlock>().Single(t => t.Name == "GlyphIcon").Text);
            sensor.Metric = "";
            sensor.Label = "RAM";
            Pump();
            if (preset == "pixel") Assert.Same(window.FindResource("PixelMemoryIcon"), pixelIcon.Data);
            else Assert.Equal("\uE964", Descendants(content).OfType<TextBlock>().Single(t => t.Name == "GlyphIcon").Text);
            sensor.Icon = "E713";
            Pump();
            Assert.Equal(Themed("\uE713", preset), Descendants(content).OfType<TextBlock>().Single(t => t.Name == "GlyphIcon").Text);
            Assert.Equal(Visibility.Collapsed, pixelIcon.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void IconVisibilityAndFallback_RefreshWhenConfigurationChanges() => RunInSta(() =>
    {
        var content = new ContentControl();
        var window = Host(content);
        try
        {
            content.ContentTemplate = (DataTemplate)window.FindResource("BarIconPresentationTemplate");
            foreach (dynamic model in new ObservableBarItemViewModel[]
            {
                new ButtonItemViewModel { Label = "Before", Icon = "E713" },
                new ToggleItemViewModel { Label = "Before", Icon = "E713" },
                new SliderItemViewModel("Before", 180, 0, 100, 80, null) { Icon = "E713" },
                new SensorItemViewModel { Label = "Before", Icon = "E713" }
            })
            {
                content.Content = model;
                Pump();
                var glyph = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "GlyphIcon");
                var fallback = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "FallbackLabel");
                Assert.Equal(Visibility.Visible, glyph.Visibility);
                model.IconKind = "asset"; // An unavailable asset falls back to the current label.
                model.Label = "After";
                Pump();
                Assert.Equal(Visibility.Collapsed, glyph.Visibility);
                Assert.Equal(Visibility.Visible, fallback.Visibility);
                Assert.Equal("After", fallback.Text);
                model.IconKind = "glyph";
                model.Icon = "E768";
                Pump();
                Assert.Equal(Visibility.Visible, glyph.Visibility);
                Assert.Equal("\uE768", glyph.Text);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TouchSlider_KeepsFirstContact_WhileOtherControlsRemainIndependent() => RunInSta(() =>
    {
        var firstSlider = new TouchSlider { Width = 200, Height = 56, Maximum = 100 };
        var otherSlider = new TouchSlider { Width = 200, Height = 56, Maximum = 100 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Children = { firstSlider, otherSlider } };
        var window = Host(panel);
        var first = new TestTouchDevice(2001, new Point(40, 28));
        var second = new TestTouchDevice(2002, new Point(160, 28));
        try
        {
            Touch(firstSlider, first, UIElement.TouchDownEvent);
            Assert.Equal(20, firstSlider.Value);
            Touch(firstSlider, second, UIElement.TouchDownEvent);
            Touch(firstSlider, second, UIElement.TouchUpEvent);
            Assert.Equal(20, firstSlider.Value);
            Assert.True(firstSlider.IsInteracting);
            Touch(otherSlider, second, UIElement.TouchDownEvent);
            Assert.Equal(80, otherSlider.Value);
            first.Position = new Point(100, 28);
            Touch(firstSlider, first, UIElement.TouchMoveEvent);
            Assert.Equal(50, firstSlider.Value);
            firstSlider.ReleaseTouchCapture(first);
            Assert.False(firstSlider.IsInteracting);
            Assert.True(otherSlider.IsInteracting);
            Touch(otherSlider, second, UIElement.TouchUpEvent);
            Assert.False(otherSlider.IsInteracting);
            Touch(firstSlider, first, UIElement.TouchDownEvent);
            Assert.True(firstSlider.IsInteracting);
            Touch(firstSlider, first, UIElement.TouchUpEvent);
            Assert.False(firstSlider.IsInteracting);
        }
        finally { firstSlider.ReleaseAllTouchCaptures(); otherSlider.ReleaseAllTouchCaptures(); window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unload_EndsBoundInteraction_FlushesAndAllowsFreshTouch(bool expandable) => RunInSta(() =>
    {
        var writes = new List<double>();
        SliderItemViewModel model = expandable
            ? new MasterVolumeItemViewModel("Volume", 56, 0, 100, 50, writes.Add)
            : new SliderItemViewModel("Volume", 200, 0, 100, 50, writes.Add);
        var content = new ContentControl { Content = model, HorizontalAlignment = HorizontalAlignment.Left };
        var window = Host(content);
        var input = Descendants(content).OfType<Slider>().Single(s => s is TouchSlider or ExpandableTouchSlider);
        if (input is ExpandableTouchSlider expanded) expanded.EnableAnimations = false;
        var finger = new TestTouchDevice(3001, new Point(20, 28));
        var interactingProperty = expandable ? ExpandableTouchSlider.IsInteractingProperty : TouchSlider.IsInteractingProperty;
        try
        {
            Touch(input, finger, UIElement.TouchDownEvent);
            Assert.True(model.IsInteracting);
            Assert.Same(input, finger.Captured);
            var second = new TestTouchDevice(3002, new Point(160, 28));
            var before = input.Value;
            Touch(input, second, UIElement.TouchDownEvent);
            Touch(input, second, UIElement.TouchUpEvent);
            Assert.Equal(before, input.Value);
            Assert.True(model.IsInteracting);
            input.ReleaseTouchCapture(finger);
            Assert.False(model.IsInteracting);
            writes.Clear();
            Touch(input, finger, UIElement.TouchDownEvent);
            model.Value = 73;
            window.Content = null;
            Pump();
            Assert.False(input.IsLoaded);
            Assert.False((bool)input.GetValue(interactingProperty));
            Assert.False(model.IsInteracting);
            Assert.NotSame(input, finger.Captured);
            Assert.Equal(new[] { 73d }, writes);
            window.Content = content;
            Pump();
            Assert.True(input.IsLoaded);
            if (input is ExpandableTouchSlider reloaded) Assert.False(reloaded.IsExpanded);
            Assert.NotNull(input.GetBindingExpression(interactingProperty));
            Touch(input, finger, UIElement.TouchDownEvent);
            Assert.True(model.IsInteracting);
            Touch(input, finger, UIElement.TouchUpEvent);
            Assert.False(model.IsInteracting);
            Assert.False(input.IsKeyboardFocused);
        }
        finally { input.ReleaseAllTouchCaptures(); window.Close(); }
    });

    private static string Themed(string glyph, string preset) =>
        (string)new PixelGlyphConverter().Convert([glyph, preset == "pixel"], typeof(string), null!, CultureInfo.InvariantCulture);

    private static IEnumerable<AutomationPeer> Peers(AutomationPeer root)
    {
        yield return root;
        foreach (var child in root.GetChildren() ?? [])
            foreach (var nested in Peers(child)) yield return nested;
    }

    private static void Touch(UIElement target, TouchDevice device, RoutedEvent routedEvent) =>
        target.RaiseEvent(new TouchEventArgs(device, 0) { RoutedEvent = routedEvent, Source = target });

    private sealed class TestTouchDevice(int id, Point position) : TouchDevice(id)
    {
        public Point Position { get; set; } = position;
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo) => new();
        public override TouchPoint GetTouchPoint(IInputElement relativeTo) => new(this, Position, new Rect(Position, new Size(1, 1)), TouchAction.Down);
    }
}
