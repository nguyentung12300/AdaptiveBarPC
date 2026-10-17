using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

[CollectionDefinition("Designer native window", DisableParallelization = true)]
public sealed class DesignerNativeWindowCollection;

// Native mouse capture is process-wide. Keep this window lifecycle separate from touch tests.
[Collection("Designer native window")]
public sealed class DesignerStudioTests
{
    [Fact]
    public void StudioIcons_UseProfileMetadataAndExplicitWidgetGlyphsWithoutChangingData()
    {
        var converter = new StudioItemConverter();
        var profile = new ProfileEntry("profile.json", "custom-id", "Renamed profile") { Category = "browser" };
        var icon = converter.Convert(profile, typeof(string), "ProfileIcon", CultureInfo.InvariantCulture);
        profile.DisplayName = "A completely different label";
        Assert.Equal(icon, converter.Convert(profile, typeof(string), "ProfileIcon", CultureInfo.InvariantCulture));
        Assert.NotEqual(icon, converter.Convert(new ProfileEntry("default.json", "default", "Default") { IsDefault = true }, typeof(string), "ProfileIcon", CultureInfo.InvariantCulture));
        var widget = new ControlDefinition { Type = WidgetTypes.Button, Action = new ActionDefinition { Type = "launch", Value = "https://example.com" } };
        Assert.Equal(icon, converter.Convert(widget, typeof(string), "Icon", CultureInfo.InvariantCulture));
        widget.Icon = new IconDefinition { Kind = "glyph", Value = "E8B7" };
        Assert.Equal("\uE8B7", converter.Convert(widget, typeof(string), "Icon", CultureInfo.InvariantCulture));
        Assert.Equal("E8B7", widget.Icon.Value);
        Assert.Equal("https://example.com", widget.Action.Value);
    }

    [Fact]
    public void CreateProfile_DoesNotOverwriteOrLoseDraft_AndMatchesTheChosenExecutable()
    {
        using var data = new StudioData();
        var vm = data.Vm;
        var original = vm.SelectedProfile;
        vm.ProfileDisplayName = "Unsaved draft";
        Assert.False(vm.CreateProfileForApplication(data.Executable, DirtyDecision.Cancel));
        Assert.Single(vm.Profiles);
        Assert.Same(original, vm.SelectedProfile);
        Assert.True(vm.IsDirty);
        Assert.False(vm.CreateProfileForApplication("missing.exe", DirtyDecision.Discard));
        Assert.Equal("Unsaved draft", vm.ProfileDisplayName);
        Assert.True(vm.CreateProfileForApplication(data.Executable, DirtyDecision.Save));
        Assert.Equal("Unsaved draft", new ProfileDocumentSerializer().LoadFile(original!.FilePath).Document!.DisplayName);
        var firstPath = vm.SelectedProfile!.FilePath;
        var firstContent = File.ReadAllText(firstPath);
        Assert.Equal("main", Assert.Single(vm.Document!.Pages).Id);
        Assert.Empty(vm.CurrentWidgets);
        Assert.False(vm.Document.IsDefault);
        Assert.Equal(data.Executable, Assert.Single(vm.Document.Match.ExecutablePaths));
        Assert.Equal("Example App", Assert.Single(vm.Document.Match.ProcessNames));
        Assert.False(vm.CanUndo);
        Assert.True(vm.CreateProfileForApplication(data.Executable, DirtyDecision.Discard));
        Assert.NotEqual(firstPath, vm.SelectedProfile!.FilePath);
        Assert.NotEqual(new ProfileDocumentSerializer().Load(firstContent).Document!.Id, vm.ProfileId);
        Assert.Equal(firstContent, File.ReadAllText(firstPath));
        Assert.True(vm.TryReload(DirtyDecision.Discard));
        Assert.Empty(vm.ValidateCurrent());
    }

    [Fact]
    public void CreateProfile_WriteFailureKeepsCurrentDraft()
    {
        using var data = new StudioData();
        var vm = data.Vm;
        var selected = vm.SelectedProfile;
        vm.ProfileDisplayName = "Keep me";
        // A file in place of this fixture's profile directory forces a deterministic write failure.
        Directory.Move(data.Profiles, data.Profiles + "-saved");
        File.WriteAllText(data.Profiles, "occupied");
        Assert.False(vm.CreateProfileForApplication(data.Executable, DirtyDecision.Discard));
        Assert.Same(selected, vm.SelectedProfile);
        Assert.Equal("Keep me", vm.ProfileDisplayName);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanUndo);
        Assert.Contains("Create profile failed", vm.StatusMessage);
    }

    [Fact]
    public void WidthStepper_UsesRegistryBoundsAutoLocaleValidationAndOneUndoPerHold()
    {
        using var data = new StudioData();
        var vm = data.Vm;
        foreach (var definition in WidgetDefinitionRegistry.All.Where(d => d.IsPlaceable))
        {
            var widget = new ControlDefinition { Id = "width", Type = definition.TypeId, Width = definition.MinWidth };
            var editor = new WidgetEditorViewModel(vm, widget);
            editor.StepWidth(-1);
            Assert.Equal(definition.AllowsFlexibleWidth ? 0 : definition.MinWidth, widget.Width);
            editor.WidthText = definition.MaxWidth.ToString(CultureInfo.CurrentCulture);
            editor.StepWidth(1);
            Assert.Equal(definition.MaxWidth, widget.Width);
            Assert.False(editor.CanIncreaseWidth);
            editor.WidthText = "invalid";
            editor.StepWidth(-1);
            Assert.Equal("invalid", editor.WidthText);
            Assert.NotEmpty(editor.WidthError);
        }
        var current = vm.SelectedWidgetEditor!;
        vm.BeginHistoryGesture();
        current.StepWidth(1);
        current.StepWidth(1);
        current.StepWidth(1);
        vm.EndHistoryGesture();
        Assert.Equal(9, vm.SelectedWidget!.Width);
        Assert.True(vm.Undo());
        Assert.Equal(6, vm.SelectedWidget!.Width);
        Assert.False(vm.CanUndo);
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            vm.SelectedWidgetEditor!.WidthText = "6,5";
            vm.SelectedWidgetEditor.StepWidth(1);
            Assert.Equal(7.5, vm.SelectedWidget!.Width);
            Assert.Equal("7,5", vm.SelectedWidgetEditor.WidthText);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Canvas_NativeLayoutAnchorsPanAndHistoryStayCorrectAcrossScales() => RunInSta(() =>
    {
        using var data = new StudioData();
        var vm = data.Vm;
        var window = new AdaptiveBarPC.Designer.MainWindow(vm, LoadResources())
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = -20000, Width = 1366, Height = 768
        };
        window.Show();
        try
        {
            Pump(window);
            Assert.NotNull(window.Icon);
            Assert.IsType<DrawingImage>(window.FindResource("AppLogo"));
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                // Caption colors are write-only DWM attributes: verify the real API accepts them.
                Assert.Equal(0, AdaptiveBarPC.Designer.Interop.DwmWindowTheme.ApplyDarkCaption(hwnd));
            }
            var viewport = window.CanvasViewport;
            Assert.True(window.IsFitPreview);
            Assert.InRange(window.CanvasZoom, 0.1, 1);
            var initialFit = window.CanvasZoom;
            Assert.False(vm.IsDirty);
            Assert.False(vm.CanUndo);
            Assert.Null(DesignerCoordinates.FitScale(0, 0, 1280, 72));
            Assert.Null(DesignerCoordinates.FitScale(100, double.NaN, 1280, 72));
            Assert.False(window.TryHandleCanvasKey(Key.Space, true, new TextBox()));
            Assert.False(window.TryHandleCanvasKey(Key.Space, true, new ComboBox()));
            foreach (var scale in new[] { .25, .5, 1, 2 })
            {
                window.SetCanvasZoom(scale);
                Pump(window);
                Assert.Equal(scale, window.CanvasZoom);
                var transform = window.PreviewSurface.TransformToAncestor(window);
                var display = transform.Transform(new Point(100, 0)).X - transform.Transform(new Point(0, 0)).X;
                Assert.Equal(100, DesignerCoordinates.DisplayDeltaToLogical(display, scale), 5);
                viewport.ScrollToHorizontalOffset(100);
                Pump(window);
                // Logical hit coordinates round-trip through the actual viewport, including its scroll offset.
                var logical = new Point(180, 36);
                var point = window.PreviewSurface.TranslatePoint(logical, viewport);
                var restored = viewport.TranslatePoint(point, window.PreviewSurface);
                Assert.Equal(logical.X, restored.X, 5);
                Assert.Equal(logical.Y, restored.Y, 5);
            }
            window.SetCanvasZoom(1);
            viewport.ScrollToHorizontalOffset(240);
            Pump(window);
            var anchor = new Point(120, 90);
            var before = viewport.TranslatePoint(anchor, window.HardwareFrame);
            window.SetCanvasZoom(1.5, anchor);
            Pump(window);
            var after = viewport.TranslatePoint(anchor, window.HardwareFrame);
            Assert.Equal(before.X, after.X, 4);
            window.BeginCanvasPan(new Point(200, 80));
            var offset = viewport.HorizontalOffset;
            window.MoveCanvasPan(new Point(140, 80));
            Pump(window);
            Assert.Equal(offset + 60, viewport.HorizontalOffset, 4);
            viewport.ReleaseMouseCapture();
            Assert.False(window.IsCanvasPanning);
            Assert.True(window.TryHandleCanvasKey(Key.Space, true, viewport));
            Assert.True(window.TryHandleCanvasKey(Key.Space, false, viewport));
            window.Width = 960;
            window.Height = 480;
            Pump(window);
            Assert.Equal(1.5, window.CanvasZoom); // Manual zoom survives resizing.
            window.ZoomPicker.SelectedItem = window.ZoomPicker.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "fit"));
            Pump(window);
            Assert.True(window.IsFitPreview);
            Assert.True(window.CanvasZoom < initialFit);
            var bounds = window.HardwareFrame.TransformToAncestor(viewport).TransformBounds(new Rect(window.HardwareFrame.RenderSize));
            Assert.True(bounds.Left >= -1 && bounds.Right <= viewport.ActualWidth + 1, bounds.ToString());
            vm.SelectedRightTabIndex = 1;
            window.BackgroundMediaEditToggle.IsChecked = true;
            Pump(window);
            var expandedFit = window.CanvasZoom;
            Assert.Equal(1440, window.PreviewMediaStage.ActualWidth);
            window.BackgroundMediaEditToggle.IsChecked = false;
            Pump(window);
            Assert.True(window.CanvasZoom > expandedFit);
            Assert.False(vm.IsDirty);
            Assert.False(vm.CanUndo);
            Assert.False(window.IsActive);
            Assert.Equal(PanningMode.VerticalFirst, window.ExplorerScroll.PanningMode);
            Assert.Equal(PanningMode.VerticalFirst, ScrollViewer.GetPanningMode(window.ProfilesList));
            Assert.All(Descendants(window).OfType<TabItem>().Where(t => t.IsVisible), tab => Assert.InRange(tab.ActualHeight, 56, 57));
            // Use the real repeat button route so pointer gesture history is checked with its template.
            vm.SelectedRightTabIndex = 0;
            Pump(window);
            var increase = Descendants(window).OfType<RepeatButton>().First(b => Equals(b.Tag, "1"));
            // Designer is a mouse editor; its two compact steppers share a 44-DIP input.
            Assert.InRange(increase.ActualHeight, 21, 22);
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            increase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            increase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            Assert.Equal(8, vm.SelectedWidget!.Width);
            Assert.True(vm.Undo());
            Assert.Equal(6, vm.SelectedWidget!.Width);
            Assert.False(vm.CanUndo);
            var button = WidgetDefinitionRegistry.All.Single(d => d.TypeId == WidgetTypes.Button);
            foreach (var scale in new[] { .25, .5, 1, 2 })
            {
                Assert.True(vm.AddWidget(button));
                var widget = vm.SelectedWidget!;
                window.SetCanvasZoom(scale);
                Pump(window);
                viewport.ScrollToHorizontalOffset(120);
                Pump(window);
                var preview = vm.PreviewItems.Single(p => p.WidgetId == widget.Id);
                var container = (FrameworkElement)window.MainPreviewItems.ItemContainerGenerator.ContainerFromItem(preview);
                var pointer = container.TranslatePoint(new Point(container.ActualWidth / 4, 0), viewport);
                var logicalPointer = viewport.TranslatePoint(pointer, window.PreviewContentGrid);
                Assert.True(window.TryGetInsertionTarget(null, logicalPointer.X, out var insertion, out var indicator));
                Assert.Equal(vm.SelectedPage!.Items.IndexOf(widget), insertion);
                Assert.InRange(indicator, 0, window.PreviewContentGrid.ActualWidth);
                Assert.True(vm.MoveWidget(widget, 0));
                Assert.Equal(widget.Id, vm.CurrentWidgets[0].Id);
                vm.BeginHistoryGesture();
                var originalWidth = widget.Width;
                vm.ResizeWidget(widget, originalWidth + DesignerCoordinates.DisplayDeltaToLogical(20 * scale, scale));
                vm.ResizeWidget(widget, originalWidth + DesignerCoordinates.DisplayDeltaToLogical(40 * scale, scale));
                vm.EndHistoryGesture();
                Assert.True(vm.Undo());
                Assert.Equal(originalWidth, vm.SelectedWidget!.Width);
                Assert.True(vm.SaveCurrent());
                Assert.True(vm.TryReload(DirtyDecision.Discard));
                Assert.Equal(widget.Id, vm.CurrentWidgets[0].Id);
            }
            var targetId = vm.CurrentWidgets[0].Id;
            vm.SelectedWidget = null;
            var issue = new ProfileDiagnostic("test", ProfileDiagnosticSeverity.Warning, "Navigate to this widget", vm.ProfileId, vm.SelectedPage!.Id, targetId);
            vm.ReplaceDiagnostics([issue]);
            vm.IsValidationExpanded = true;
            Pump(window);
            window.DiagnosticsList.SelectedItem = issue;
            window.DiagnosticsList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(targetId, vm.SelectedWidget!.Id);
            vm.SelectedWidget = null;
            Pump(window);
            var go = Descendants(window.DiagnosticsList).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Go to issue");
            go.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(targetId, vm.SelectedWidget!.Id);
        }
        finally
        {
            vm.TryReload(DirtyDecision.Discard);
            window.Close();
        }
    });

    private static void Pump(FrameworkElement element)
    {
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    internal static ResourceDictionary LoadResources()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AdaptiveBarPC.sln"))) root = root.Parent;
        var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(root!.FullName, "src/AdaptiveBarPC.Designer/App.xaml"));
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml.Root!.Elements().Single().Elements().Single().ToString());
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed class StudioData : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "AdaptiveBarStudio_" + Guid.NewGuid().ToString("N"));
        public string Profiles => Path.Combine(_root, "Profiles");
        public string Executable => Path.Combine(_root, "Example App.exe");
        public DesignerViewModel Vm { get; }
        public StudioData()
        {
            Directory.CreateDirectory(Profiles);
            File.WriteAllText(Executable, "test fixture only");
            new ProfileDocumentSerializer().SaveFile(Path.Combine(Profiles, "desktop.json"), new ProfileDefinition
            {
                Id = "desktop", DisplayName = "Desktop", IsDefault = true,
                Pages = [new() { Id = "main", DisplayName = "Main", Items = [new() { Id = "spacer", Type = WidgetTypes.Spacer, Width = 6 }] }]
            });
            Vm = new DesignerViewModel(Profiles, Path.Combine(_root, "appearance.json"));
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
