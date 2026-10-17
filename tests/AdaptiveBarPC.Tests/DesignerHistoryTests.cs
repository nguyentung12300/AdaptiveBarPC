using System.IO;
using System.Text.Json;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public sealed class DesignerHistoryTests
{
    [Fact]
    public void LayoutEdits_UndoRedoRestoreContentSelectionAndSavedState()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.True(vm.AddWidget(WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.Button)));
        var addedId = vm.SelectedWidget!.Id;
        Assert.True(vm.MoveSelectedWidget(-1));
        Assert.True(vm.DeleteSelectedWidget());
        Assert.True(vm.Undo());
        Assert.Equal(addedId, vm.CurrentWidgets[0].Id);
        Assert.Equal(addedId, vm.SelectedWidget!.Id);
        Assert.True(vm.Undo());
        Assert.Equal(addedId, vm.CurrentWidgets[^1].Id);
        Assert.True(vm.Undo());
        Assert.Equal("a", Assert.Single(vm.CurrentWidgets).Id);
        Assert.False(vm.IsDirty);
        Assert.True(vm.Redo());
        Assert.Equal(addedId, vm.SelectedWidget!.Id);
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);
        Assert.True(vm.Undo());
        Assert.True(vm.IsDirty);
        Assert.True(vm.Redo());
        Assert.False(vm.IsDirty);
        Assert.True(vm.Undo());
        vm.ProfileDisplayName = "New branch";
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void TypingAndPointerGestures_GroupEditsWithoutCombiningDifferentFields()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        vm.SelectedWidgetEditor!.Label = "B";
        vm.SelectedWidgetEditor.Label = "Br";
        vm.SelectedWidgetEditor.Label = "Browser";
        vm.SelectedWidgetEditor.WidthText = "180";
        Assert.True(vm.Undo());
        Assert.Equal(120, vm.SelectedWidget!.Width);
        Assert.Equal("Browser", vm.SelectedWidget.Label);
        Assert.True(vm.Undo());
        Assert.Equal("Original", vm.SelectedWidget!.Label);
        Assert.False(vm.CanUndo);
        vm.BeginHistoryGesture();
        vm.ResizeWidget(vm.SelectedWidget, 160, validate: false);
        vm.ResizeWidget(vm.SelectedWidget, 220, validate: false);
        vm.EndHistoryGesture();
        Assert.True(vm.Undo());
        Assert.Equal(120, vm.SelectedWidget!.Width);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void GlobalPlacementAndInputTools_AreRestoredTogetherAndPersisted()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        vm.SelectedWidgetEditor!.Placement = "Fixed Left (All Profiles)";
        Assert.Empty(vm.CurrentWidgets);
        Assert.Single(GlobalAppearanceStore.Load(fixture.AppearancePath).FixedLeft);
        Assert.True(vm.Undo());
        Assert.Equal("a", Assert.Single(vm.CurrentWidgets).Id);
        Assert.Empty(GlobalAppearanceStore.Load(fixture.AppearancePath).FixedLeft);
        Assert.True(vm.Redo());
        Assert.Equal("Fixed Left (All Profiles)", vm.SelectedWidgetEditor!.Placement);
        Assert.True(vm.AddInputItem(WidgetTypes.TextSlot));
        vm.SelectedWidgetEditor!.InputContent = "My text";
        Assert.True(vm.Undo());
        Assert.Equal("Saved text", Assert.Single(vm.InputItems).Action!.Value);
        Assert.True(vm.Undo());
        Assert.Empty(vm.InputItems);
        Assert.Empty(GlobalAppearanceStore.Load(fixture.AppearancePath).InputItems);
        Assert.True(vm.Redo());
        Assert.Single(vm.InputItems);
    }

    [Fact]
    public void BackgroundGestureAndAppliedTheme_CanBeUndoneWithoutPendingSaveOverwritingThem()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        vm.BeginHistoryGesture();
        vm.AppearanceEditor.BackgroundMediaOffsetX = 50;
        vm.AppearanceEditor.BackgroundMediaOffsetX = 90;
        vm.AppearanceEditor.BackgroundMediaOffsetY = -25;
        vm.EndHistoryGesture();
        Assert.True(vm.Undo());
        vm.FlushPendingAppearanceSave();
        Assert.Equal(0, GlobalAppearanceStore.Load(fixture.AppearancePath).BackgroundMedia.OffsetX);
        Assert.Equal(0, vm.AppearanceEditor.BackgroundMediaOffsetY);
        Assert.False(vm.IsDirty);
        Assert.True(vm.Redo());
        Assert.Equal(90, vm.AppearanceEditor.BackgroundMediaOffsetX);
        vm.ApplyTheme(new GlobalAppearanceSettings
        {
            Appearance = new AppearanceDefinition { Preset = "pixel" },
            BackgroundMedia = new BackgroundMediaDefinition { Source = "background.gif", Zoom = 2 }
        }, "Imported theme");
        Assert.True(vm.Undo());
        Assert.Equal(90, vm.AppearanceEditor.BackgroundMediaOffsetX);
        Assert.Equal("", vm.AppearanceEditor.BackgroundMediaSource);
        Assert.True(vm.Redo());
        Assert.Equal("pixel", vm.AppearanceEditor.SelectedPresetId);
        Assert.Equal(2, vm.AppearanceEditor.BackgroundMediaZoom);
        Assert.Equal("a", Assert.Single(vm.CurrentWidgets).Id);
    }

    [Fact]
    public void PagesLegacyAndUnknownFields_SurviveHistoryAndReloadClearsHistory()
    {
        using var fixture = new Fixture(legacy: true);
        var vm = fixture.Vm;
        Assert.True(vm.AddPage());
        var pageId = vm.SelectedPage!.Id;
        Assert.True(vm.Undo());
        Assert.True(vm.SelectedPage!.IsLegacyMain);
        Assert.Empty(vm.Document!.Pages);
        Assert.Equal(42, vm.Document.ExtensionData!["futureOption"].GetInt32());
        Assert.True(vm.Redo());
        Assert.Equal(pageId, vm.SelectedPage!.Id);
        vm.PageDisplayName = "Tools";
        Assert.True(vm.MoveSelectedPage(-1));
        Assert.True(vm.DeleteSelectedPage());
        Assert.True(vm.Undo());
        Assert.Equal("Tools", vm.SelectedPage!.DisplayName);
        Assert.True(vm.Undo());
        Assert.Equal(pageId, vm.Pages[^1].Id);
        Assert.False(vm.TryReload(DirtyDecision.Cancel));
        Assert.True(vm.CanUndo);
        Assert.True(vm.TryReload(DirtyDecision.Discard));
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        vm.ProfileDisplayName = "Changed";
        Assert.True(vm.TrySwitchProfile(vm.Profiles[1], DirtyDecision.Discard));
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void UndoProfileEdit_DoesNotDiscardDeferredBackgroundSave()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        vm.AppearanceEditor.BackgroundMediaOffsetX = 125;
        vm.ProfileDisplayName = "Changed";
        Assert.True(vm.Undo());
        vm.FlushPendingAppearanceSave();
        Assert.Equal(125, GlobalAppearanceStore.Load(fixture.AppearancePath).BackgroundMedia.OffsetX);
    }

    [Fact]
    public void UndoMediaAdjustment_KeepsOverflowEditorOpen()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        vm.AddWidget(WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.DecorativeMedia));
        vm.IsDecorativeMediaEditMode = true;
        vm.SelectedWidgetEditor!.MediaOffsetX = 100;
        Assert.True(vm.Undo());
        Assert.Equal(0, vm.SelectedWidgetEditor!.MediaOffsetX);
        Assert.True(vm.IsDecorativeMediaEditMode);
    }

    [Fact]
    public void IconChoice_IsOneStepIncludingKindAndValue()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        vm.SelectedWidgetEditor!.SelectCatalogIcon(IconCatalog.Search("").First());
        Assert.True(vm.Undo());
        Assert.Null(vm.SelectedWidget!.Icon);
        Assert.False(vm.CanUndo);
        Assert.True(vm.Redo());
        Assert.Equal("glyph", vm.SelectedWidget!.Icon!.Kind);
    }

    [Fact]
    public void HistoryIsBoundedAndFailedGlobalRestoreKeepsCurrentState()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        for (var i = 0; i < 105; i++)
        {
            vm.ProfileDisplayName = "Name " + i;
            vm.EndHistoryGesture();
        }
        var count = 0;
        while (vm.Undo()) count++;
        Assert.Equal(100, count);
        vm.AppearanceEditor.BackgroundMediaZoom = 2;
        // Replace only this fixture's settings file with a directory to simulate a failed write.
        File.Delete(fixture.AppearancePath);
        Directory.CreateDirectory(fixture.AppearancePath);
        Assert.False(vm.Undo());
        Assert.Equal(2, vm.AppearanceEditor.BackgroundMediaZoom);
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.Contains("Undo failed", vm.StatusMessage);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "AdaptiveBarHistory_" + Guid.NewGuid().ToString("N"));
        public string AppearancePath => Path.Combine(_root, "appearance.json");
        public DesignerViewModel Vm { get; }
        public Fixture(bool legacy = false)
        {
            var profiles = Path.Combine(_root, "Profiles");
            Directory.CreateDirectory(profiles);
            var item = new ControlDefinition { Id = "a", Type = WidgetTypes.Button, Label = "Original", Width = 120 };
            var profile = new ProfileDefinition
            {
                Id = "first", DisplayName = "First",
                ExtensionData = new() { ["futureOption"] = JsonSerializer.SerializeToElement(42) },
                Items = legacy ? [item] : [],
                Pages = legacy ? [] : [new PageDefinition { Id = "main", DisplayName = "Main", Items = [item] }]
            };
            var serializer = new ProfileDocumentSerializer();
            serializer.SaveFile(Path.Combine(profiles, "a.json"), profile);
            serializer.SaveFile(Path.Combine(profiles, "b.json"), new ProfileDefinition { Id = "second", DisplayName = "Second" });
            Vm = new DesignerViewModel(profiles, AppearancePath);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
