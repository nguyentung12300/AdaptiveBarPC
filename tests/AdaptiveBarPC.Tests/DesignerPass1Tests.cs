using System.IO;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public sealed class DesignerPass1Tests
{
    [Fact]
    public void BundledProfiles_AreEnumerated_AndPaletteContainsOnlyPlaceableWidgets()
    {
        var vm = new DesignerViewModel(Path.Combine(AppContext.BaseDirectory, "Profiles"));

        Assert.NotEmpty(vm.Profiles);
        Assert.NotNull(vm.Document);
        Assert.NotEmpty(vm.Pages);
        Assert.All(vm.Palette, definition => Assert.True(definition.IsPlaceable));
        Assert.DoesNotContain(vm.Palette, definition => definition.TypeId == WidgetTypes.AudioSession);
    }

    [Fact]
    public void LoadingProfile_BuildsEditableState_WithoutMutatingDisk()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"));
        var path = folder.PathFor("alpha");
        var before = File.ReadAllBytes(path);

        var vm = new DesignerViewModel(folder.DirectoryPath);

        Assert.Equal("alpha", vm.Document!.Id);
        Assert.Single(vm.Pages);
        Assert.Single(vm.CurrentWidgets);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void InspectorEdits_UpdatePreview_SetDirty_AndKeepInvalidWidthText()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        editor.Label = "Renamed";

        Assert.True(vm.IsDirty);
        var preview = Assert.IsType<ButtonItemViewModel>(Assert.Single(vm.PreviewItems).Content);
        Assert.Equal("Renamed", preview.Label);

        var originalWidth = editor.Model.Width;
        editor.WidthText = "not-a-number";
        Assert.Equal("not-a-number", editor.WidthText);
        Assert.Equal(originalWidth, editor.Model.Width);
        Assert.Contains(vm.Diagnostics, d =>
            d.Code == "DesignerInvalidWidthInput" &&
            d.ProfileId == "alpha" &&
            d.PageId == "main" &&
            d.WidgetId == "alpha-button");
    }

    [Fact]
    public void WidthValidation_UsesRegistry_AndSaveRefusesErrors()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"));
        var path = folder.PathFor("alpha");
        var before = File.ReadAllText(path);
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        editor.WidthText = "10";

        var diagnostic = Assert.Single(vm.Diagnostics.Where(d => d.Code == ProfileDiagnosticCodes.InvalidWidth));
        Assert.Equal("alpha", diagnostic.ProfileId);
        Assert.Equal("main", diagnostic.PageId);
        Assert.Equal("alpha-button", diagnostic.WidgetId);
        Assert.False(vm.SaveCurrent());
        Assert.Equal(before, File.ReadAllText(path));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void AddWidget_UsesPlaceableRegistryDefaults_AndUniquePersistentIds()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var button = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.Button);

        Assert.True(vm.AddWidget(button));
        var first = vm.SelectedWidget!;
        Assert.True(vm.AddWidget(button));
        var second = vm.SelectedWidget!;

        Assert.StartsWith("widget-", first.Id);
        Assert.StartsWith("widget-", second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(button.DefaultWidth, first.Width);
        Assert.Equal(button.DefaultWidth, second.Width);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void RuntimeGeneratedWidget_CannotBeAdded()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var runtimeOnly = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.AudioSession);
        var before = vm.SelectedPage!.Items.Count;

        Assert.False(vm.AddWidget(runtimeOnly));
        Assert.Equal(before, vm.SelectedPage.Items.Count);
        Assert.Contains("runtime-generated", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteWidget_AffectsOnlyCurrentPage()
    {
        var profile = CreateProfile("alpha");
        profile.Pages.Add(new PageDefinition
        {
            Id = "second",
            DisplayName = "Second",
            Items = [new ControlDefinition { Id = "second-button", Type = WidgetTypes.Button, Label = "Second", Width = 84 }]
        });
        using var folder = new TempProfiles(profile);
        var vm = new DesignerViewModel(folder.DirectoryPath);

        Assert.True(vm.DeleteSelectedWidget());

        Assert.Empty(vm.Pages[0].Items);
        Assert.Single(vm.Pages[1].Items);
        Assert.Equal("second-button", vm.Pages[1].Items[0].Id);
    }

    [Fact]
    public void DirtyProfileSwitch_ImplementsCancelDiscardAndSave()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"), CreateProfile("beta"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var alpha = vm.Profiles.Single(x => x.Id == "alpha");
        var beta = vm.Profiles.Single(x => x.Id == "beta");
        var alphaPath = folder.PathFor("alpha");
        var original = File.ReadAllText(alphaPath);

        vm.SelectedWidgetEditor!.Label = "Cancelled edit";
        Assert.False(vm.TrySwitchProfile(beta, DirtyDecision.Cancel));
        Assert.Same(alpha, vm.SelectedProfile);
        Assert.True(vm.IsDirty);

        Assert.True(vm.TrySwitchProfile(beta, DirtyDecision.Discard));
        Assert.Equal(original, File.ReadAllText(alphaPath));

        Assert.True(vm.TrySwitchProfile(alpha, DirtyDecision.Discard));
        vm.SelectedWidgetEditor!.Label = "Saved edit";
        Assert.True(vm.TrySwitchProfile(beta, DirtyDecision.Save));

        var saved = new ProfileDocumentSerializer().LoadFile(alphaPath).Document!;
        Assert.Equal("Saved edit", saved.Pages[0].Items[0].Label);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SuccessfulSave_PersistsDocument_AndClearsDirtyState()
    {
        using var folder = new TempProfiles(CreateProfile("alpha"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        vm.SelectedWidgetEditor!.Label = "Persisted";

        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);

        var saved = new ProfileDocumentSerializer().LoadFile(folder.PathFor("alpha")).Document!;
        Assert.Equal("Persisted", saved.Pages[0].Items[0].Label);
    }

    [Fact]
    public void PreviewAdapter_NeverExecutesActionDefinition()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"adaptivebar-designer-{Guid.NewGuid():N}.marker");
        var adapter = new DesignerPreviewAdapter();
        var widget = new ControlDefinition
        {
            Id = "dangerous-action",
            Type = WidgetTypes.Button,
            Label = "Preview",
            Width = 84,
            Action = new ActionDefinition
            {
                Type = "shell",
                Value = $"cmd /c type nul > \"{marker}\""
            }
        };

        var preview = Assert.IsType<ButtonItemViewModel>(adapter.CreateContent(widget));
        preview.Command.Execute(null);

        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void PreviewAdapter_UsesDesignerFixtures_AndSafeUnknownProviderPlaceholder()
    {
        var adapter = new DesignerPreviewAdapter();

        var hardware = Assert.IsType<DynamicListItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "hardware",
            Type = WidgetTypes.DynamicList,
            Provider = "hardware",
            Width = 0
        }));
        var sensors = hardware.Items.Cast<SensorItemViewModel>().ToList();
        Assert.Equal(4, sensors.Count);
        Assert.Contains("42", sensors[0].ValueText);
        Assert.Contains("61", sensors[1].ValueText);
        Assert.Contains("58", sensors[2].ValueText);
        Assert.Contains("73", sensors[3].ValueText);

        var audio = Assert.IsType<DynamicListItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "audio",
            Type = WidgetTypes.DynamicList,
            Provider = "audioSessions",
            Width = 0
        }));
        var sessions = audio.Items.Cast<AudioSessionItemViewModel>().ToList();
        Assert.Collection(
            sessions,
            item => { Assert.Equal("Brave", item.Label); Assert.Equal(72, item.Value); },
            item => { Assert.Equal("VLC", item.Label); Assert.Equal(48, item.Value); });

        var media = Assert.IsType<MediaItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "media",
            Type = WidgetTypes.Media,
            Width = 0
        }));
        Assert.Equal("Example Track", media.Title);
        Assert.Equal("Example Artist", media.Artist);

        var unknown = Assert.IsType<DesignerPlaceholderPreview>(adapter.CreateContent(new ControlDefinition
        {
            Id = "custom",
            Type = WidgetTypes.DynamicList,
            Provider = "customProvider",
            Width = 0
        }));
        Assert.Equal("Dynamic: customProvider", unknown.Text);
    }

    private static ProfileDefinition CreateProfile(string id) => new()
    {
        Id = id,
        DisplayName = id,
        Pages =
        [
            new PageDefinition
            {
                Id = "main",
                DisplayName = "Main",
                Items =
                [
                    new ControlDefinition
                    {
                        Id = $"{id}-button",
                        Type = WidgetTypes.Button,
                        Label = "Button",
                        Width = 84
                    }
                ]
            }
        ]
    };

    private sealed class TempProfiles : IDisposable
    {
        private readonly ProfileDocumentSerializer _serializer = new();

        public TempProfiles(params ProfileDefinition[] profiles)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "AdaptiveBarDesignerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            for (var i = 0; i < profiles.Length; i++)
                _serializer.SaveFile(Path.Combine(DirectoryPath, $"{i:D2}-{profiles[i].Id}.json"), profiles[i]);
        }

        public string DirectoryPath { get; }

        public string PathFor(string id) =>
            Directory.EnumerateFiles(DirectoryPath, $"*-{id}.json").Single();

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only; test assertions should not depend on temp deletion.
            }
        }
    }
}
