using System;
using System.IO;
using System.Linq;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.ViewModels;
using Xunit;

namespace AdaptiveBarPC.Tests;

public sealed class DesignerPreviewIntegrationTests
{
    [Theory]
    [InlineData(WidgetTypes.Button, typeof(ButtonItemViewModel))]
    [InlineData(WidgetTypes.Toggle, typeof(ToggleItemViewModel))]
    [InlineData(WidgetTypes.MultiState, typeof(MultiStateItemViewModel))]
    [InlineData(WidgetTypes.Slider, typeof(SliderItemViewModel))]
    [InlineData(WidgetTypes.ExpandableSlider, typeof(ExpandableSliderItemViewModel))]
    [InlineData(WidgetTypes.Media, typeof(MediaItemViewModel))]
    [InlineData(WidgetTypes.DecorativeMedia, typeof(DecorativeMediaItemViewModel))]
    [InlineData(WidgetTypes.Sensor, typeof(SensorItemViewModel))]
    [InlineData(WidgetTypes.Spacer, typeof(SpacerItemViewModel))]
    [InlineData(WidgetTypes.AudioSession, typeof(AudioSessionItemViewModel))]
    [InlineData(WidgetTypes.DynamicList, typeof(DynamicListItemViewModel))]
    public void PreviewAdapter_ReturnsExpectedRuntimeViewModelType(string widgetType, Type expectedType)
    {
        var adapter = new DesignerPreviewAdapter();
        var widget = new ControlDefinition
        {
            Id = $"test-{widgetType}",
            Type = widgetType,
            Provider = widgetType == WidgetTypes.DynamicList ? "hardware" : null,
            Width = 100
        };

        var content = adapter.CreateContent(widget);

        Assert.NotNull(content);
        Assert.IsAssignableFrom(expectedType, content);
    }

    [Fact]
    public void MediaItemViewModel_CreatePreview_DoesNotRequireServices_AndIsSideEffectSafe()
    {
        var preview = MediaItemViewModel.CreatePreview(
            width: 360,
            title: "Preview Track",
            artist: "Preview Artist",
            position: TimeSpan.FromMinutes(1),
            duration: TimeSpan.FromMinutes(3),
            isPlaying: true);

        Assert.Equal(360, preview.Width);
        Assert.Equal("Preview Track", preview.Title);
        Assert.Equal("Preview Artist", preview.Artist);
        Assert.True(preview.IsAvailable);
        Assert.True(preview.IsPlaying);
        Assert.True(preview.CanPrevious);
        Assert.True(preview.CanPlayPause);
        Assert.True(preview.CanNext);
        Assert.True(preview.HasArtwork);
        Assert.NotNull(preview.Artwork);
        Assert.True(preview.Artwork!.IsFrozen);
        Assert.Equal("1:00", preview.PositionText);
        Assert.Equal("3:00", preview.DurationText);
        Assert.True(preview.ProgressPercent > 30 && preview.ProgressPercent < 35);
        Assert.Equal("\uE769", preview.PlayPauseGlyph);

        preview.IsSeeking = true;
        preview.ProgressPercent = 100;
        Assert.Equal(preview.Duration, preview.Position);
        preview.IsSeeking = false;

        // Safe command execution without external side effects
        Assert.True(preview.PreviousCommand.CanExecute(null));
        preview.PreviousCommand.Execute(null);

        Assert.True(preview.PlayPauseCommand.CanExecute(null));
        preview.PlayPauseCommand.Execute(null);

        Assert.True(preview.NextCommand.CanExecute(null));
        preview.NextCommand.Execute(null);

        // Clean disposal
        preview.Dispose();
    }

    [Fact]
    public void DynamicListItemViewModel_CreatePreview_DoesNotRequireProvider_AndIsSideEffectSafe()
    {
        var sensor1 = new SensorItemViewModel { Id = "s1", Label = "CPU", Value = 50, Unit = "%" };
        var sensor2 = new SensorItemViewModel { Id = "s2", Label = "RAM", Value = 70, Unit = "%" };

        var dynamicVm = DynamicListItemViewModel.CreatePreview("hardware", new[] { sensor1, sensor2 }, 200);

        Assert.Equal("hardware", dynamicVm.ProviderId);
        Assert.Equal(200, dynamicVm.Width);
        Assert.Equal(2, dynamicVm.Items.Count);
        Assert.False(dynamicVm.IsEmpty);
        Assert.Same(sensor1, dynamicVm.Items[0]);
        Assert.Same(sensor2, dynamicVm.Items[1]);

        dynamicVm.Dispose();
    }

    [Fact]
    public void PreviewInteraction_CommandsAndSetters_AreSideEffectFree()
    {
        var adapter = new DesignerPreviewAdapter();

        // 1. Button
        var button = Assert.IsType<ButtonItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "btn",
            Type = WidgetTypes.Button,
            Action = new ActionDefinition { Type = "hotkey", Value = "ctrl+c" }
        }));
        Assert.NotNull(button.Command);
        button.Command.Execute(null); // NoOpCommand

        // 2. Toggle
        var toggle = Assert.IsType<ToggleItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "toggle",
            Type = WidgetTypes.Toggle,
            Action = new ActionDefinition { Type = "hotkey", Value = "ctrl+t" }
        }));
        Assert.NotNull(toggle.ToggleCommand);
        toggle.ToggleCommand.Execute(null); // NoOpCommand

        // 3. Slider
        var slider = Assert.IsType<SliderItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "slider",
            Type = WidgetTypes.Slider,
            Min = 0,
            Max = 100
        }));
        Assert.Null(slider.Setter); // No live volume modification

        // 4. ExpandableSlider
        var expSlider = Assert.IsType<ExpandableSliderItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "exp_slider",
            Type = WidgetTypes.ExpandableSlider,
            Min = 0,
            Max = 100
        }));
        Assert.Null(expSlider.Setter); // No live volume modification

        // 5. AudioSession
        var audioSession = Assert.IsType<AudioSessionItemViewModel>(adapter.CreateContent(new ControlDefinition
        {
            Id = "audio_session",
            Type = WidgetTypes.AudioSession,
            Min = 0,
            Max = 100
        }));
        Assert.Null(audioSession.ValueSetter);
        Assert.Null(audioSession.MuteSetter);
    }

    [Fact]
    public void UnknownWidgetType_YieldsDesignerPlaceholderPreview()
    {
        var adapter = new DesignerPreviewAdapter();
        var unknown = Assert.IsType<DesignerPlaceholderPreview>(adapter.CreateContent(new ControlDefinition
        {
            Id = "custom",
            Type = "unsupported_widget_type",
            Width = 140
        }));

        Assert.Equal(140, unknown.Width);
        Assert.Equal("Unknown: unsupported_widget_type", unknown.Text);
    }

    [Fact]
    public void DesignerMainWindowXaml_DoesNotContainDuplicatePreviewTemplates()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        var xamlPath = Path.Combine(dir, "src", "AdaptiveBarPC.Designer", "MainWindow.xaml");

        Assert.True(File.Exists(xamlPath), $"MainWindow.xaml not found at {xamlPath}");
        var xamlContent = File.ReadAllText(xamlPath);

        // Verify duplicate preview classes/templates are gone
        Assert.DoesNotContain("DesignerMediaPreview", xamlContent);
        Assert.DoesNotContain("DesignerPreviewGroup", xamlContent);

        // Verify no private DataTemplates for canonical runtime ViewModels exist in MainWindow.xaml
        Assert.DoesNotContain("DataType=\"{x:Type vm:ButtonItemViewModel}\"", xamlContent);
        Assert.DoesNotContain("DataType=\"{x:Type vm:SliderItemViewModel}\"", xamlContent);
        Assert.DoesNotContain("DataType=\"{x:Type vm:MediaItemViewModel}\"", xamlContent);
        Assert.DoesNotContain("DataType=\"{x:Type vm:DynamicListItemViewModel}\"", xamlContent);
        Assert.DoesNotContain("DataType=\"{x:Type vm:ToggleItemViewModel}\"", xamlContent);
        Assert.DoesNotContain("DataType=\"{x:Type vm:SensorItemViewModel}\"", xamlContent);

        // Confirm DesignerPlaceholderPreview remains for true unsupported widgets
        Assert.Contains("DataType=\"{x:Type local:DesignerPlaceholderPreview}\"", xamlContent);

        // Confirm selection/resize/drag affordances remain in outer wrapper DesignerPreviewItemTemplate
        Assert.Contains("x:Key=\"DesignerPreviewItemTemplate\"", xamlContent);
        Assert.Contains("PreviewMouseLeftButtonDown=\"PreviewItem_OnPreviewMouseLeftButtonDown\"", xamlContent);
        Assert.Contains("DragDelta=\"ResizeThumb_OnDragDelta\"", xamlContent);

        // Designer shell uses the same appearance semantics as runtime.
        Assert.Contains("BorderBrush=\"{DynamicResource ShellBorderBrush}\"", xamlContent);
        Assert.Contains("CornerRadius=\"{DynamicResource ShellCornerRadius}\"", xamlContent);
    }

    [Fact]
    public void PreviewAffordances_SelectionAndResize_RemainInOuterWrapper()
    {
        var adapter = new DesignerPreviewAdapter();
        var item = adapter.Create(new ControlDefinition
        {
            Id = "btn-1",
            Type = WidgetTypes.Button,
            Width = 84
        }, selected: true);

        Assert.True(item.IsSelected);
        Assert.Equal("btn-1", item.WidgetId);
        Assert.True(item.CanResize);
        Assert.IsType<ButtonItemViewModel>(item.Content);

        // Modify selection on outer wrapper without mutating inner runtime ViewModel
        item.IsSelected = false;
        Assert.False(item.IsSelected);
        Assert.IsType<ButtonItemViewModel>(item.Content);
    }
}
