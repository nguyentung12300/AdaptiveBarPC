using System;
using System.IO;
using System.Linq;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using Xunit;

namespace AdaptiveBarPC.Tests;

public sealed class IconPickerTests
{
    [Fact]
    public void DesignerInspector_ExposesCatalogIconPicker()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName ?? "";

        var xaml = File.ReadAllText(Path.Combine(dir, "src", "AdaptiveBarPC.Designer", "MainWindow.xaml"));

        Assert.Contains("Command=\"{Binding ToggleIconPickerCommand}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding FilteredIconGroups}\"", xaml);
        Assert.Contains("Click=\"IconTile_OnClick\"", xaml);
    }

    [Fact]
    public void SelectCatalogIcon_WritesCorrectKindAndValue()
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-icon"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        var catalogItem = IconCatalog.FindByGlyph("E767");
        Assert.NotNull(catalogItem);
        Assert.Equal("Volume High", catalogItem.Name);

        editor.SelectCatalogIcon(catalogItem);

        Assert.Equal("glyph", editor.IconKind);
        Assert.Equal("E767", editor.IconValue);
        Assert.Equal("Volume High", editor.SelectedIconDisplayName);
        Assert.Equal("\uE767", editor.SelectedIconGlyph);
        Assert.True(editor.HasSelectedIcon);
    }

    [Fact]
    public void SelectCatalogIconCommand_ExecutesWithTileOrItem_AndUpdatesSelection()
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-cmd"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        var item = IconCatalog.FindByGlyph("E74F"); // Volume Mute
        Assert.NotNull(item);

        var tile = new IconPickerTile(item, IsSelected: false);
        Assert.True(editor.SelectCatalogIconCommand.CanExecute(tile));
        editor.SelectCatalogIconCommand.Execute(tile);

        Assert.Equal("glyph", editor.IconKind);
        Assert.Equal("E74F", editor.IconValue);
        Assert.Equal("Volume Mute", editor.SelectedIconDisplayName);
        Assert.False(editor.IsIconPickerOpen);
    }

    [Fact]
    public void ClearIcon_RestoresNoIconState()
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-clear"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        // First select an icon
        editor.IconKind = "glyph";
        editor.IconValue = "E710"; // Add
        Assert.True(editor.HasSelectedIcon);

        // Clear icon
        editor.ClearIcon();

        Assert.Equal("none", editor.IconKind);
        Assert.Equal("", editor.IconValue);
        Assert.Equal("None (Use Label)", editor.SelectedIconDisplayName);
        Assert.Equal("", editor.SelectedIconGlyph);
        Assert.False(editor.HasSelectedIcon);
        Assert.False(editor.IsIconPickerOpen);
    }

    [Theory]
    [InlineData("volume", "Volume High")]
    [InlineData("mute", "Volume Mute")]
    [InlineData("back", "Back")]
    [InlineData("wifi", "WiFi")]
    [InlineData("play", "Play")]
    [InlineData("brightness", "Brightness")]
    [InlineData("hardware", "Display")] // category search
    [InlineData("E767", "Volume High")] // glyph hex search
    public void Search_FiltersByNameCategoryAndKeywords(string query, string expectedItemName)
    {
        var results = IconCatalog.Search(query);
        Assert.NotEmpty(results);
        Assert.Contains(results, item => item.Name == expectedItemName);
    }

    [Fact]
    public void Search_IsCaseInsensitive_AndEmptyQueryReturnsAll()
    {
        var all = IconCatalog.Search(null);
        Assert.True(all.Count >= 70);

        var lower = IconCatalog.Search("volume");
        var upper = IconCatalog.Search("VOLUME");
        var mixed = IconCatalog.Search("VoLuMe");

        Assert.Equal(lower.Count, upper.Count);
        Assert.Equal(lower.Count, mixed.Count);
        Assert.True(lower.Count >= 4); // High, Medium, Low, Mute
    }

    [Fact]
    public void Search_WithNoMatches_ReturnsEmptyList()
    {
        var results = IconCatalog.Search("nonexistentglyphquery12345");
        Assert.Empty(results);
    }

    [Fact]
    public void ManualCustomGlyph_RemainsSupported_AndShowsCustomDisplayName()
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-custom"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        // Enter a glyph code not present in the curated catalog (e.g. EA80)
        editor.IconValue = "EA80";

        Assert.Equal("glyph", editor.IconKind);
        Assert.Equal("EA80", editor.IconValue);
        Assert.Equal("Custom Glyph", editor.SelectedIconDisplayName);
        Assert.Equal("\uEA80", editor.SelectedIconGlyph);
        Assert.True(editor.HasSelectedIcon);
        Assert.Null(editor.SelectedCatalogItem);
    }

    [Fact]
    public void AssetPathEditing_DoesNotConvertAssetKindToGlyph()
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-asset-path"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        editor.IconKind = "asset";
        editor.IconValue = "Assets/icons/custom.png";

        Assert.Equal("asset", editor.IconKind);
        Assert.True(editor.IsAssetIcon);
        Assert.False(editor.IsGlyphIcon);
        Assert.Equal("Assets/icons/custom.png", editor.SelectedIconDisplayName);
        Assert.Equal("", editor.SelectedIconGlyph);
        Assert.True(editor.HasSelectedIcon);
        Assert.Null(editor.SelectedCatalogItem);
    }

    [Theory]
    [InlineData("E767", "Volume High")]
    [InlineData("0xE767", "Volume High")]
    [InlineData(@"\uE767", "Volume High")]
    [InlineData("&#xE767;", "Volume High")]
    [InlineData("\uE767", "Volume High")]
    [InlineData("  e767  ", "Volume High")]
    [InlineData("E710", "Add")]
    [InlineData("0xE710", "Add")]
    [InlineData("E713", "Settings")]
    public void MatchingRawGlyph_ResolvesCatalogDisplayName(string rawGlyph, string expectedName)
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-match"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        editor.IconValue = rawGlyph;

        Assert.Equal(expectedName, editor.SelectedIconDisplayName);
        Assert.NotNull(editor.SelectedCatalogItem);
        Assert.Equal(expectedName, editor.SelectedCatalogItem.Name);
    }

    [Fact]
    public void SelectingOrClearingIcon_MarksDesignerStateDirty_AndUpdatesPreviewImmediately()
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-dirty"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        Assert.False(vm.IsDirty);

        // Select catalog icon
        var item = IconCatalog.FindByGlyph("E768"); // Play
        Assert.NotNull(item);
        editor.SelectCatalogIcon(item);

        Assert.True(vm.IsDirty);
        var preview = Assert.IsType<ButtonItemViewModel>(Assert.Single(vm.PreviewItems).Content);
        Assert.Equal("\uE768", preview.DisplayGlyph);
        Assert.True(preview.HasIcon);

        // Reset dirty flag by simulating save
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);

        // Clear icon
        editor.ClearIcon();
        Assert.True(vm.IsDirty);
        preview = Assert.IsType<ButtonItemViewModel>(Assert.Single(vm.PreviewItems).Content);
        Assert.False(preview.HasIcon);
    }

    [Fact]
    public void ProfileSchema_RemainsUnchanged_NoNewPropertiesPersisted()
    {
        // Assert IconDefinition schema contract (only Kind, Value, and ExtensionData for forward-compatibility)
        var iconProps = typeof(IconDefinition).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(3, iconProps.Count);
        Assert.Contains("Kind", iconProps);
        Assert.Contains("Value", iconProps);
        Assert.Contains("ExtensionData", iconProps);
        Assert.DoesNotContain("Name", iconProps);
        Assert.DoesNotContain("IconName", iconProps);
        Assert.DoesNotContain("DisplayName", iconProps);

        // Assert serialization roundtrip has no added properties
        var original = new ProfileDefinition
        {
            Id = "schema-test",
            DisplayName = "Schema Test",
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
                            Id = "btn-1",
                            Type = WidgetTypes.Button,
                            Label = "Test Button",
                            Icon = new IconDefinition
                            {
                                Kind = "glyph",
                                Value = "E767"
                            }
                        }
                    ]
                }
            ]
        };

        var serializer = new ProfileDocumentSerializer();
        var tempPath = Path.Combine(Path.GetTempPath(), "AdaptiveBar_SchemaTest_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            serializer.SaveFile(tempPath, original);
            var json = File.ReadAllText(tempPath);

            Assert.Contains("\"kind\": \"glyph\"", json);
            Assert.Contains("\"value\": \"E767\"", json);
            Assert.DoesNotContain("iconName", json, StringComparison.OrdinalIgnoreCase);

            var result = serializer.Load(json);
            Assert.NotNull(result.Document);
            var page = Assert.Single(result.Document.Pages);
            var control = Assert.Single(page.Items);
            Assert.NotNull(control.Icon);
            Assert.Equal("glyph", control.Icon.Kind);
            Assert.Equal("E767", control.Icon.Value);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void FilteredIconGroups_HighlightsCurrentSelectedIcon()
    {
        using var folder = new TempProfiles(CreateProfileWithButton("test-highlight"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

        editor.IconKind = "glyph";
        editor.IconValue = "E767"; // Volume High in Audio category

        var groups = editor.FilteredIconGroups;
        Assert.NotEmpty(groups);

        var audioGroup = groups.FirstOrDefault(g => g.Category == "Audio");
        Assert.NotNull(audioGroup);

        var volumeHighTile = audioGroup.Items.FirstOrDefault(t => t.GlyphValue == "E767");
        Assert.NotNull(volumeHighTile);
        Assert.True(volumeHighTile.IsSelected);

        var volumeMuteTile = audioGroup.Items.FirstOrDefault(t => t.GlyphValue == "E74F");
        Assert.NotNull(volumeMuteTile);
        Assert.False(volumeMuteTile.IsSelected);
    }

    [Fact]
    public void EndToEnd_ManualVerificationWorkflow_Succeeds()
    {
        // 1. Open Designer with button profile
        using var folder = new TempProfiles(CreateProfileWithButton("workflow-test"));
        var vm = new DesignerViewModel(folder.DirectoryPath);

        // 2. Select Button widget
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);
        Assert.Equal("Sample Button", editor.Label);
        Assert.False(editor.HasSelectedIcon);

        // 3. Open icon picker
        editor.ToggleIconPickerCommand.Execute(null);
        Assert.True(editor.IsIconPickerOpen);

        // 4. Search "volume"
        editor.IconSearchText = "volume";
        var results = editor.FilteredIconGroups;
        Assert.Contains(results, g => g.Items.Any(i => i.Name == "Volume High"));

        // 5. Select Volume High
        var volumeHigh = IconCatalog.FindByGlyph("E767");
        Assert.NotNull(volumeHigh);
        editor.SelectCatalogIcon(volumeHigh);

        // 6. Preview immediately changes
        var preview = Assert.IsType<ButtonItemViewModel>(Assert.Single(vm.PreviewItems).Content);
        Assert.True(preview.HasIcon);
        Assert.Equal("\uE767", preview.DisplayGlyph);
        Assert.True(vm.IsDirty);

        // 7. Save
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);

        // 8. Reload profile
        var reloadedVm = new DesignerViewModel(folder.DirectoryPath);
        var reloadedEditor = Assert.IsType<WidgetEditorViewModel>(reloadedVm.SelectedWidgetEditor);

        // 9. Selection persists
        Assert.Equal("glyph", reloadedEditor.IconKind);
        Assert.Equal("E767", reloadedEditor.IconValue);
        Assert.Equal("Volume High", reloadedEditor.SelectedIconDisplayName);
        var reloadedPreview = Assert.IsType<ButtonItemViewModel>(Assert.Single(reloadedVm.PreviewItems).Content);
        Assert.True(reloadedPreview.HasIcon);
        Assert.Equal("\uE767", reloadedPreview.DisplayGlyph);

        // 10. Clear icon
        reloadedEditor.ClearIcon();

        // 11. Label fallback returns
        Assert.Equal("none", reloadedEditor.IconKind);
        Assert.Equal("", reloadedEditor.IconValue);
        Assert.Equal("None (Use Label)", reloadedEditor.SelectedIconDisplayName);
        reloadedPreview = Assert.IsType<ButtonItemViewModel>(Assert.Single(reloadedVm.PreviewItems).Content);
        Assert.False(reloadedPreview.HasIcon);
        Assert.Equal("Sample Button", reloadedPreview.Label);

        // 12. Manually enter custom glyph
        reloadedEditor.IconValue = "EA80";

        // 13. Custom glyph renders correctly
        Assert.Equal("glyph", reloadedEditor.IconKind);
        Assert.Equal("EA80", reloadedEditor.IconValue);
        Assert.Equal("Custom Glyph", reloadedEditor.SelectedIconDisplayName);
        reloadedPreview = Assert.IsType<ButtonItemViewModel>(Assert.Single(reloadedVm.PreviewItems).Content);
        Assert.True(reloadedPreview.HasIcon);
        Assert.Equal("\uEA80", reloadedPreview.DisplayGlyph);
    }

    private static ProfileDefinition CreateProfileWithButton(string id)
    {
        return new ProfileDefinition
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
                            Label = "Sample Button"
                        }
                    ]
                }
            ]
        };
    }

    private sealed class TempProfiles : IDisposable
    {
        private readonly ProfileDocumentSerializer _serializer = new();
        public string DirectoryPath { get; }

        public TempProfiles(params ProfileDefinition[] profiles)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_IconPickerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);

            for (var i = 0; i < profiles.Length; i++)
            {
                var filePath = Path.Combine(DirectoryPath, $"{i:D2}-{profiles[i].Id}.json");
                _serializer.SaveFile(filePath, profiles[i]);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
