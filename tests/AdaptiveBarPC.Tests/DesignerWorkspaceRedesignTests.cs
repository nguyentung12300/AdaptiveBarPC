using System.IO;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public sealed class DesignerWorkspaceRedesignTests
{
    [Fact]
    public void LeftPanel_SelectedTabIndex_DefaultsToExplorerAndSwitchesToLibrary()
    {
        using var folder = new TempProfiles(CreateValidProfile("test-workspace"));
        var vm = new DesignerViewModel(folder.DirectoryPath);

        // Default tab is 0 (Explorer)
        Assert.Equal(0, vm.SelectedLeftTabIndex);

        // Switch to Library tab (1)
        vm.SelectedLeftTabIndex = 1;
        Assert.Equal(1, vm.SelectedLeftTabIndex);

        // Switch back to Explorer tab (0)
        vm.SelectedLeftTabIndex = 0;
        Assert.Equal(0, vm.SelectedLeftTabIndex);
    }

    [Fact]
    public void CurrentWidgets_RemainsAStableSnapshot_WhenLibraryAddsAWidget()
    {
        using var folder = new TempProfiles(CreateValidProfile("test-workspace"));
        var vm = new DesignerViewModel(folder.DirectoryPath);
        var explorerSnapshot = vm.CurrentWidgets;
        var button = WidgetDefinitionRegistry.All.First(x => x.TypeId == WidgetTypes.Button);

        vm.SelectedLeftTabIndex = 1;
        Assert.True(vm.AddWidget(button));
        vm.SelectedLeftTabIndex = 0;

        Assert.Empty(explorerSnapshot);
        Assert.Single(vm.CurrentWidgets);
    }

    [Fact]
    public void ValidationDrawer_ToggleCommand_ExpandsAndCollapsesDrawer()
    {
        using var folder = new TempProfiles(CreateValidProfile("test-workspace"));
        var vm = new DesignerViewModel(folder.DirectoryPath);

        // Default state: collapsed
        Assert.False(vm.IsValidationExpanded);

        // Execute toggle command
        Assert.True(vm.ToggleValidationCommand.CanExecute(null));
        vm.ToggleValidationCommand.Execute(null);
        Assert.True(vm.IsValidationExpanded);

        // Execute toggle command again
        vm.ToggleValidationCommand.Execute(null);
        Assert.False(vm.IsValidationExpanded);
    }

    [Fact]
    public void ValidationSummary_ReflectsCleanAndDiagnosticStates()
    {
        using var folder = new TempProfiles(CreateValidProfile("test-workspace"));
        var vm = new DesignerViewModel(folder.DirectoryPath);

        // Clean profile: no errors or warnings
        Assert.False(vm.HasErrors);
        Assert.False(vm.HasWarnings);
        Assert.Equal("No issues", vm.ValidationSummary);

        // Add error diagnostic
        vm.ReplaceDiagnostics(new[]
        {
            new ProfileDiagnostic("SCHEMA_ERROR", ProfileDiagnosticSeverity.Error, "Test error message", "pages[0]")
        });

        Assert.True(vm.HasErrors);
        Assert.False(vm.HasWarnings);
        Assert.Equal("1 error(s), 0 warning(s)", vm.ValidationSummary);

        // Add warning diagnostic
        vm.ReplaceDiagnostics(new[]
        {
            new ProfileDiagnostic("WARN_CODE", ProfileDiagnosticSeverity.Warning, "Test warning message", "items[0]")
        });

        Assert.False(vm.HasErrors);
        Assert.True(vm.HasWarnings);
        Assert.Equal("0 error(s), 1 warning(s)", vm.ValidationSummary);

        // Both error and warning
        vm.ReplaceDiagnostics(new[]
        {
            new ProfileDiagnostic("ERR1", ProfileDiagnosticSeverity.Error, "Error 1"),
            new ProfileDiagnostic("ERR2", ProfileDiagnosticSeverity.Error, "Error 2"),
            new ProfileDiagnostic("WARN1", ProfileDiagnosticSeverity.Warning, "Warning 1")
        });

        Assert.True(vm.HasErrors);
        Assert.True(vm.HasWarnings);
        Assert.Equal("2 error(s), 1 warning(s)", vm.ValidationSummary);
    }

    [Fact]
    public void StudioWorkspace_PaletteInsertAndInspectorEditing_MaintainsDirtyAndSaveFlow()
    {
        using var folder = new TempProfiles(CreateValidProfile("studio-profile"));
        var vm = new DesignerViewModel(folder.DirectoryPath);

        Assert.False(vm.IsDirty);
        Assert.Equal("studio-profile", vm.ProfileId);

        // Insert widget from palette (simulate Library workflow)
        var buttonDef = WidgetDefinitionRegistry.All.First(x => x.TypeId == WidgetTypes.Button);
        Assert.True(vm.AddWidget(buttonDef, 0));

        Assert.True(vm.IsDirty);
        Assert.NotNull(vm.SelectedWidgetEditor);

        // Edit properties via Inspector
        vm.SelectedWidgetEditor.Label = "Studio Action";
        vm.SelectedWidgetEditor.ActionType = "media";
        vm.SelectedWidgetEditor.ActionValue = "playpause";

        Assert.True(vm.IsDirty);

        // Save
        Assert.True(vm.SaveCurrent());
        Assert.False(vm.IsDirty);

        // Verify saved profile content on disk
        var filePath = Directory.EnumerateFiles(folder.DirectoryPath, "*.json").Single();
        var reloaded = new ProfileDocumentSerializer().LoadFile(filePath);
        Assert.True(reloaded.IsValid);
        var item = Assert.Single(reloaded.Document!.Pages[0].Items);
        Assert.Equal("Studio Action", item.Label);
        Assert.Equal("media", item.Action?.Type);
        Assert.Equal("playpause", item.Action?.Value);
    }

    private static ProfileDefinition CreateValidProfile(string id) => new()
    {
        Id = id,
        DisplayName = "Studio Workspace Test",
        Pages =
        [
            new()
            {
                Id = "page-1",
                DisplayName = "Main Page",
                Items = []
            }
        ]
    };

    private sealed class TempProfiles : IDisposable
    {
        private readonly ProfileDocumentSerializer _serializer = new();

        public TempProfiles(params ProfileDefinition[] profiles)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "AdaptiveBarDesignerWorkspaceRedesignTests", Guid.NewGuid().ToString("N"));
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
}
