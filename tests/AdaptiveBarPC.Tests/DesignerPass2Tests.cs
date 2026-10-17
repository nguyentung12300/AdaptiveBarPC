using System.IO;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public sealed class DesignerPass2Tests
{
    [Fact]
    public void ExpandableSlider_AddPreviewEditSaveReload_PreservesGenericConfiguration()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var definition = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.ExpandableSlider);

        Assert.True(vm.AddWidget(definition, 0));

        var widget = Assert.Single(vm.SelectedPage!.Items);
        Assert.Equal(ExpandableTouchSlider.CollapsedWidthDip, widget.Width);
        Assert.Equal(50d, widget.Value);
        Assert.True(widget.Enabled);

        var preview = Assert.IsType<ExpandableSliderItemViewModel>(Assert.Single(vm.PreviewItems).Content);
        Assert.Equal(ExpandableTouchSlider.CollapsedWidthDip, preview.Width);
        Assert.Equal(0d, preview.Minimum);
        Assert.Equal(100d, preview.Maximum);
        Assert.Equal(50d, preview.Value);
        Assert.True(preview.IsEnabled);

        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);
        Assert.False(editor.HasEditableWidth);
        editor.SliderValue = 23.5;
        editor.SliderMinimum = -10;
        editor.SliderMaximum = 80;
        editor.SliderEnabled = false;
        editor.IconKind = "glyph";
        editor.IconValue = "\uE945";

        Assert.True(vm.SaveCurrent());

        var saved = new ProfileDocumentSerializer().LoadFile(Directory.EnumerateFiles(folder.DirectoryPath, "*.json").Single());
        Assert.True(saved.IsValid);
        var roundTrip = Assert.Single(saved.Document!.Pages[0].Items);
        Assert.Equal(WidgetTypes.ExpandableSlider, roundTrip.Type);
        Assert.Equal(23.5, roundTrip.Value);
        Assert.Equal(-10d, roundTrip.Min);
        Assert.Equal(80d, roundTrip.Max);
        Assert.False(roundTrip.Enabled);
        Assert.Equal("glyph", roundTrip.Icon!.Kind);
        Assert.Equal("\uE945", roundTrip.Icon.Value);

        var reloadedPreview = Assert.IsType<ExpandableSliderItemViewModel>(
            new DesignerPreviewAdapter().CreateContent(roundTrip));
        Assert.Equal(23.5, reloadedPreview.Value);
        Assert.False(reloadedPreview.IsEnabled);
        Assert.Equal("\uE945", reloadedPreview.Icon);
    }

    [Fact]
    public void ExpandableSlider_MissingNewFields_UsesDefaults_AndRuntimeKeepsMasterPath()
    {
        var missingFields = new ControlDefinition
        {
            Id = "expandable",
            Type = WidgetTypes.ExpandableSlider,
            Label = "Level",
            Width = ExpandableTouchSlider.CollapsedWidthDip,
            Min = 0,
            Max = 100
        };
        var preview = Assert.IsType<ExpandableSliderItemViewModel>(
            new DesignerPreviewAdapter().CreateContent(missingFields));
        Assert.Equal(50d, preview.Value);
        Assert.True(preview.IsEnabled);

        var profile = CreateProfile("runtime");
        profile.IsDefault = true;
        profile.Pages[0].Items =
        [
            new ControlDefinition
            {
                Id = "master",
                Type = WidgetTypes.Slider,
                Label = "MASTER",
                Width = 220,
                Min = 0,
                Max = 100,
                Action = new ActionDefinition { Type = "volume" }
            },
            new ControlDefinition
            {
                Id = "level",
                Type = WidgetTypes.ExpandableSlider,
                Label = "Level",
                Width = ExpandableTouchSlider.CollapsedWidthDip,
                Min = 10,
                Max = 90,
                Value = 35,
                Enabled = false,
                Icon = new IconDefinition { Kind = "glyph", Value = "\uE945" }
            }
        ];
        using var folder = new TempProfiles(profile);
        var logger = new AppLogger(folder.DirectoryPath);
        var config = new AppConfig();
        using var foreground = new ForegroundAppService();
        foreground.Dispose(); // Keep this runtime-construction test free of real foreground polling races.
        var profiles = new ProfileService(folder.DirectoryPath, logger);
        var audio = new FakeAudioProvider { MasterVolume = 41 };
        var actions = new ActionExecutor(config, logger, audio);
        using var media = new MediaSessionService(logger);
        using var runtime = new MainViewModel(config, foreground, profiles, audio, actions, media, logger,
            appearanceFilePath: Path.Combine(folder.DirectoryPath, "appearance.json"));

        // Exercise the default test profile; Desktop has its own dedicated layout.
        runtime.ApplyForegroundApp(new ForegroundAppInfo(new IntPtr(123), "test-target", "Test target", null, "Test target"));

        Assert.Collection(
            runtime.Items,
            item =>
            {
                var master = Assert.IsType<MasterVolumeItemViewModel>(item);
                Assert.Equal(41d, master.Value);
                master.Value = 52;
                Assert.Equal(52d, audio.MasterVolume);
            },
            item =>
            {
                var expandable = Assert.IsType<ExpandableSliderItemViewModel>(item);
                Assert.Equal(35d, expandable.Value);
                Assert.Equal(10d, expandable.Minimum);
                Assert.Equal(90d, expandable.Maximum);
                Assert.False(expandable.IsEnabled);
                Assert.Equal("\uE945", expandable.Icon);
            });
        Assert.True(audio.ReadCount >= 1);
    }

    [Fact]
    public void PaletteInsert_UsesRequestedIndex_AndUniquePersistentId()
    {
        using var folder = new TempProfiles(CreateProfile("alpha", "a", "b"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var button = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.Button);

        Assert.True(vm.AddWidget(button, 1));

        Assert.Collection(
            vm.SelectedPage!.Items,
            item => Assert.Equal("a", item.Id),
            item =>
            {
                Assert.StartsWith("widget-", item.Id);
                Assert.NotEqual("a", item.Id);
                Assert.NotEqual("b", item.Id);
            },
            item => Assert.Equal("b", item.Id));
    }

    [Fact]
    public void Reorder_MovesSameWidget_AndPreservesStableId()
    {
        using var folder = new TempProfiles(CreateProfile("alpha", "a", "b", "c"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var moved = vm.SelectedPage!.Items[2];

        Assert.True(vm.MoveWidget(moved, 1));

        Assert.Equal(["a", "c", "b"], vm.SelectedPage.Items.Select(x => x.Id));
        Assert.Same(moved, vm.SelectedPage.Items[1]);
        Assert.Equal("c", moved.Id);
    }

    [Fact]
    public void AppAssignmentAndPlacement_SaveExecutablePathAndFixedWidget()
    {
        using var folder = new TempProfiles(CreateProfile("alpha", "a", "b"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var executable = Path.Combine(folder.DirectoryPath, "Acme.exe");
        File.WriteAllText(executable, "test");

        Assert.True(vm.AssignApplication(executable));
        vm.SelectedWidget = vm.SelectedPage!.Items[0];
        vm.SelectedWidgetEditor!.Placement = "Fixed Left";

        Assert.Equal("Fixed Left", vm.SelectedWidgetEditor.Placement);
        Assert.Single(vm.FixedLeftPreviewItems);
        Assert.Single(vm.SelectedPage.Items);
        Assert.True(vm.SaveCurrent());

        var saved = new ProfileDocumentSerializer()
            .LoadFile(Directory.EnumerateFiles(folder.DirectoryPath, "*.json").Single())
            .Document!;
        Assert.Equal([Path.GetFullPath(executable)], saved.Match.ExecutablePaths);
        Assert.Equal(["Acme"], saved.Match.ProcessNames);
        Assert.Equal("a", Assert.Single(saved.FixedLeft).Id);
        Assert.Equal("b", Assert.Single(saved.Pages[0].Items).Id);
    }

    [Fact]
    public void GlobalFixedPlacement_PersistsAndAppearsAcrossProfiles()
    {
        using var folder = new TempProfiles(CreateProfile("alpha", "a"), CreateProfile("beta", "b"));
        var appearancePath = Path.Combine(folder.DirectoryPath, "settings", "appearance.json");
        var vm = new DesignerViewModel(folder.DirectoryPath, appearancePath);

        vm.SelectedWidget = Assert.Single(vm.SelectedPage!.Items);
        vm.SelectedWidgetEditor!.Placement = "Fixed Left (All Profiles)";

        Assert.Empty(vm.SelectedPage.Items);
        Assert.Equal("a", Assert.Single(GlobalAppearanceStore.Load(appearancePath).FixedLeft).Id);
        Assert.True(vm.SaveCurrent());
        Assert.True(vm.TrySwitchProfile(vm.Profiles.Single(x => x.Id == "beta"), DirtyDecision.Discard));
        Assert.Equal("a", Assert.Single(vm.FixedLeftPreviewItems).WidgetId);
        Assert.Equal("Fixed Left (All Profiles)", vm.SelectedWidgetEditor!.Placement);
    }

    [Fact]
    public void DeleteSelectedProfile_RemovesFileAndSelectsFallback()
    {
        using var folder = new TempProfiles(CreateProfile("alpha", "a"), CreateProfile("beta", "b"));
        var vm = new DesignerViewModel(folder.DirectoryPath, Path.Combine(folder.DirectoryPath, "settings", "appearance.json"));
        var deletedPath = vm.SelectedProfile!.FilePath;

        Assert.True(vm.DeleteSelectedProfile());

        Assert.False(File.Exists(deletedPath));
        Assert.Single(vm.Profiles);
        Assert.Equal("beta", vm.SelectedProfile!.Id);
        Assert.Equal("beta", vm.Document!.Id);
    }

    [Fact]
    public void Resize_ClampsRegistryBounds_AndStoresLogicalDipIndependentOfScale()
    {
        var profile = CreateProfile("alpha", "slider");
        profile.Pages[0].Items[0].Type = WidgetTypes.Slider;
        profile.Pages[0].Items[0].Width = 180;
        using var folder = new TempProfiles(profile);
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var widget = vm.SelectedPage!.Items[0];
        var definition = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.Slider);

        var deltaAt150Percent = DesignerCoordinates.DisplayDeltaToLogical(30, 1.5);
        var deltaAt200Percent = DesignerCoordinates.DisplayDeltaToLogical(40, 2.0);
        Assert.Equal(20, deltaAt150Percent);
        Assert.Equal(deltaAt150Percent, deltaAt200Percent);

        Assert.True(vm.ResizeWidget(widget, 180 + deltaAt150Percent));
        Assert.Equal(200, widget.Width);
        Assert.True(vm.ResizeWidget(widget, definition.MinWidth - 100));
        Assert.Equal(definition.MinWidth, widget.Width);
        Assert.True(vm.ResizeWidget(widget, definition.MaxWidth + 100));
        Assert.Equal(definition.MaxWidth, widget.Width);
        Assert.Equal("slider", widget.Id);
    }

    [Fact]
    public void RuntimeGeneratedWidget_CannotBeInsertedManually()
    {
        using var folder = new TempProfiles(CreateProfile("alpha", "a"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var runtimeOnly = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.AudioSession);

        Assert.False(vm.AddWidget(runtimeOnly, 0));
        Assert.Single(vm.SelectedPage!.Items);
        Assert.Equal("a", vm.SelectedPage.Items[0].Id);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void PageMutation_AddMoveDelete_PreservesExistingIdsAndContents()
    {
        var profile = CreateProfile("alpha", "a");
        profile.Pages.Add(new PageDefinition
        {
            Id = "second",
            DisplayName = "Second",
            Items = [new ControlDefinition { Id = "second-widget", Type = WidgetTypes.Button, Label = "Second", Width = 84 }]
        });
        using var folder = new TempProfiles(profile);
        var vm = new DesignerViewModel(folder.DirectoryPath);

        Assert.True(vm.AddPage());
        var addedId = vm.SelectedPage!.Id;
        Assert.StartsWith("page-", addedId);
        Assert.Empty(vm.SelectedPage.Items);

        Assert.True(vm.MoveSelectedPage(-1));
        Assert.Equal(addedId, vm.Pages[0].Id);
        Assert.Equal("main", vm.Pages[1].Id);
        Assert.Equal("a", vm.Pages[1].Items.Single().Id);
        Assert.Equal("second", vm.Pages[2].Id);
        Assert.Equal("second-widget", vm.Pages[2].Items.Single().Id);

        Assert.True(vm.DeleteSelectedPage());
        Assert.Equal(["main", "second"], vm.Pages.Select(x => x.Id));
        Assert.Equal("a", vm.Pages[0].Items.Single().Id);
        Assert.Equal("second-widget", vm.Pages[1].Items.Single().Id);
    }

    [Fact]
    public void DirtyState_SelectionStaysClean_MutationsDirty_AndSaveClears()
    {
        using var folder = new TempProfiles(CreateProfile("alpha", "a", "b"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var button = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.Button);

        vm.SelectedWidget = vm.SelectedPage!.Items[1];
        vm.SelectedPaletteDefinition = button;
        _ = DesignerCoordinates.DisplayDeltaToLogical(30, 1.5);
        Assert.False(vm.IsDirty);

        Assert.True(vm.AddWidget(button, 1));
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);

        var selected = vm.SelectedWidget!;
        Assert.True(vm.MoveWidget(selected, 0));
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);

        Assert.True(vm.ResizeWidget(selected, 120));
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);

        Assert.True(vm.DeleteSelectedWidget());
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);
    }

    private static ProfileDefinition CreateProfile(string id, params string[] widgetIds) => new()
    {
        Id = id,
        DisplayName = id,
        Pages =
        [
            new PageDefinition
            {
                Id = "main",
                DisplayName = "Main",
                Items = widgetIds.Select(widgetId => new ControlDefinition
                {
                    Id = widgetId,
                    Type = WidgetTypes.Button,
                    Label = widgetId,
                    Width = 84
                }).ToList()
            }
        ]
    };

    private sealed class TempProfiles : IDisposable
    {
        private readonly ProfileDocumentSerializer _serializer = new();

        public TempProfiles(params ProfileDefinition[] profiles)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "AdaptiveBarDesignerPass2Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            for (var i = 0; i < profiles.Length; i++)
                _serializer.SaveFile(Path.Combine(DirectoryPath, $"{i:D2}-{profiles[i].Id}.json"), profiles[i]);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private sealed class FakeAudioProvider : IAudioProvider
    {
        public double MasterVolume { get; set; }
        public int ReadCount { get; private set; }

        public double GetMasterVolumePercent()
        {
            ReadCount++;
            return MasterVolume;
        }

        public void SetMasterVolumePercent(double percent) => MasterVolume = percent;
        public void ToggleMute() { }
        public bool GetMute() => false;
        public void Dispose() { }
    }
}
