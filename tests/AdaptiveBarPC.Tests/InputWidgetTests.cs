using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public sealed class InputWidgetTests
{
    [Fact]
    public void InputTools_AreSeparatedFromPagePalette()
    {
        using var profiles = new InputProfiles();
        var vm = new DesignerViewModel(profiles.Path, profiles.AppearancePath);

        Assert.DoesNotContain(vm.Palette, item => item.Category == "Input");
        Assert.False(vm.AddInputItem(WidgetTypes.Emoji));
        Assert.False(vm.AddInputItem(WidgetTypes.Clipboard));
        Assert.Empty(vm.InputItems);
    }

    [Fact]
    public void UnicodeTextInputs_KeepEmojiSurrogatePair()
    {
        var inputs = ActionExecutor.BuildUnicodeInputs("A😀");

        Assert.Equal(6, inputs.Length);
        Assert.Equal((ushort)'A', inputs[0].U.ki.wScan);
        Assert.Equal(KeyboardInterop.KEYEVENTF_UNICODE, inputs[0].U.ki.dwFlags);
        Assert.Equal(KeyboardInterop.KEYEVENTF_UNICODE | KeyboardInterop.KEYEVENTF_KEYUP, inputs[1].U.ki.dwFlags);
        Assert.Equal((ushort)'\uD83D', inputs[2].U.ki.wScan);
        Assert.Equal((ushort)'\uDE00', inputs[4].U.ki.wScan);
    }

    [Fact]
    public void SharedInputItems_PersistOutsideTheCurrentProfile()
    {
        using var profiles = new InputProfiles();
        var vm = new DesignerViewModel(profiles.Path, profiles.AppearancePath);

        Assert.True(vm.AddInputItem(WidgetTypes.TextSlot));
        Assert.True(vm.AddInputItem(WidgetTypes.Sticker));

        Assert.Empty(vm.SelectedPage!.Items);
        Assert.Equal([WidgetTypes.TextSlot, WidgetTypes.Sticker], vm.InputItems.Select(item => item.Type));
        Assert.Equal([WidgetTypes.TextSlot, WidgetTypes.Sticker],
            GlobalAppearanceStore.Load(profiles.AppearancePath).InputItems.Select(item => item.Type));

        Assert.True(vm.MoveSelectedWidget(-1));
        Assert.Equal([WidgetTypes.Sticker, WidgetTypes.TextSlot], vm.InputItems.Select(item => item.Type));
        Assert.Equal([WidgetTypes.Sticker, WidgetTypes.TextSlot],
            GlobalAppearanceStore.Load(profiles.AppearancePath).InputItems.Select(item => item.Type));
    }

    [Fact]
    public void LegacyPageInputItem_CanMoveToSharedInputTools()
    {
        using var profiles = new InputProfiles();
        profiles.WriteItems("""
        [
          { "id": "legacy-email", "type": "text_slot", "label": "Email", "width": 112,
            "action": { "type": "clipboard_text", "value": "hello@example.com" } }
        ]
        """);
        var vm = new DesignerViewModel(profiles.Path, profiles.AppearancePath);
        vm.SelectedWidget = Assert.Single(vm.SelectedPage!.Items);

        vm.SelectedWidgetEditor!.Placement = "Input Tools (All Profiles)";

        Assert.Empty(vm.SelectedPage.Items);
        Assert.Equal("legacy-email", Assert.Single(vm.InputItems).Id);
        Assert.Equal("legacy-email", Assert.Single(GlobalAppearanceStore.Load(profiles.AppearancePath).InputItems).Id);
    }

    [Fact]
    public void TextSlot_DefaultsToClipboardPasteAndKeepsEditedContent()
    {
        using var profiles = new InputProfiles();
        var vm = new DesignerViewModel(profiles.Path, profiles.AppearancePath);

        Assert.True(vm.AddInputItem(WidgetTypes.TextSlot));

        var widget = Assert.IsType<ControlDefinition>(vm.SelectedWidget);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);
        Assert.Equal("clipboard_text", widget.Action?.Type);
        Assert.True(editor.HasInputContent);

        editor.InputContent = "Xin chào 👋";

        Assert.Equal("clipboard_text", widget.Action?.Type);
        Assert.Equal("Xin chào 👋", widget.Action?.Value);
        Assert.Same(widget, Assert.Single(vm.InputItems));
        Assert.Empty(vm.PreviewItems);
    }

    [Fact]
    public void ClipboardTextAction_WritesSlotBeforeSendingPasteChord()
    {
        using var logs = new InputProfiles();
        var writes = new List<string>();
        var sent = new List<KeyboardInterop.INPUT[]>();
        var executor = new ActionExecutor(
            new AppConfig(),
            new AppLogger(logs.Path),
            writes.Add,
            inputs =>
            {
                sent.Add(inputs);
                return (uint)inputs.Length;
            });

        executor.Execute(new ActionDefinition { Type = "clipboard_text", Value = "Saved text" });

        Assert.Equal(["Saved text"], writes);
        var paste = Assert.Single(sent);
        Assert.Equal(4, paste.Length);
        Assert.Equal((ushort)0x11, paste[0].U.ki.wVk);
        Assert.Equal((ushort)'V', paste[1].U.ki.wVk);
    }

    [Fact]
    public void ClipboardTextAction_DoesNotPasteStaleClipboardWhenWriteFails()
    {
        using var logs = new InputProfiles();
        var sent = new List<KeyboardInterop.INPUT[]>();
        var executor = new ActionExecutor(
            new AppConfig(),
            new AppLogger(logs.Path),
            _ => throw new InvalidOperationException("clipboard busy"),
            inputs =>
            {
                sent.Add(inputs);
                return (uint)inputs.Length;
            });

        executor.Execute(new ActionDefinition { Type = "clipboard_text", Value = "New text" });

        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    public void ClipboardImageAction_WritesSupportedImageBeforeSendingPasteChord(string extension)
    {
        RunInSta(() =>
        {
            using var logs = new InputProfiles();
            var imagePath = WriteTestImage(logs.Path, extension);
            var images = new List<BitmapSource>();
            var sent = new List<KeyboardInterop.INPUT[]>();
            var executor = new ActionExecutor(
                new AppConfig(),
                new AppLogger(logs.Path),
                _ => { },
                images.Add,
                inputs =>
                {
                    sent.Add(inputs);
                    return (uint)inputs.Length;
                });

            executor.Execute(new ActionDefinition { Type = "clipboard_image", Value = imagePath });

            Assert.Single(images);
            var paste = Assert.Single(sent);
            Assert.Equal((ushort)0x11, paste[0].U.ki.wVk);
            Assert.Equal((ushort)'V', paste[1].U.ki.wVk);
        });
    }

    [Fact]
    public void ClipboardImageAction_DoesNotPasteInvalidImage()
    {
        using var logs = new InputProfiles();
        var imagePath = System.IO.Path.Combine(logs.Path, "invalid.png");
        File.WriteAllText(imagePath, "not an image");
        var images = new List<BitmapSource>();
        var sent = new List<KeyboardInterop.INPUT[]>();
        var executor = new ActionExecutor(
            new AppConfig(),
            new AppLogger(logs.Path),
            _ => { },
            images.Add,
            inputs =>
            {
                sent.Add(inputs);
                return (uint)inputs.Length;
            });

        executor.Execute(new ActionDefinition { Type = "clipboard_image", Value = imagePath });

        Assert.Empty(images);
        Assert.Empty(sent);
    }

    [Fact]
    public void ClipboardHistory_KeepsUniqueTextNewestFirst()
    {
        using var logs = new InputProfiles();
        using var history = new ClipboardHistoryService(new AppLogger(logs.Path));

        history.RecordText("first");
        history.RecordText("second");
        history.RecordText("first");

        Assert.Equal(["first", "second"], history.Items);
    }

    [Fact]
    public void TextInputContext_ShowsInputToolsAndRestoresCurrentPage()
    {
        RunInSta(() =>
        {
            using var profilesFolder = new InputProfiles();
            profilesFolder.WriteItems("""
            [
              { "id": "normal", "type": "button", "label": "Normal", "width": 84 },
              { "id": "email", "type": "text_slot", "label": "Email", "width": 112, "action": { "type": "clipboard_text", "value": "hello@example.com" } }
            ]
            """);

            var logger = new AppLogger(profilesFolder.Path);
            var config = new AppConfig();
            using var foreground = new ForegroundAppService(logger);
            using var profiles = new ProfileService(profilesFolder.Path, logger);
            using var audio = new FakeAudioProvider();
            using var media = new MediaSessionService(logger);
            using var history = new ClipboardHistoryService(logger);
            var actions = new ActionExecutor(config, logger, _ => { }, _ => 1, audio, media);
            using var vm = new MainViewModel(
                config, foreground, profiles, audio, actions, media, logger,
                clipboardHistory: history);

            vm.ApplyTextInputContext(true);

            Assert.Equal("Input", vm.CurrentPageName);
            Assert.Equal(["Emoji", "Clipboard", "Text", "Sticker"],
                vm.Items.OfType<ButtonItemViewModel>().Select(item => item.Label));
            Assert.False(vm.HasMultiplePages);

            Assert.IsType<ButtonItemViewModel>(vm.Items[2]).Command.Execute(null);
            Assert.Equal("Text", vm.CurrentPageName);
            Assert.Contains(vm.Items.OfType<ButtonItemViewModel>(), item => item.Label == "Email");

            vm.ApplyTextInputContext(false);

            Assert.Equal("Main", vm.CurrentPageName);
            Assert.Equal("Normal", Assert.IsType<ButtonItemViewModel>(vm.Items[0]).Label);
        });
    }

    [Fact]
    public void EmojiPicker_LoadsAllBundledColorImages_AndTypesOriginalUnicode()
    {
        RunInSta(() =>
        {
            using var folder = new InputProfiles();
            var logger = new AppLogger(folder.Path);
            var config = new AppConfig();
            using var foreground = new ForegroundAppService(logger);
            using var profiles = new ProfileService(folder.Path, logger);
            using var audio = new FakeAudioProvider();
            using var media = new MediaSessionService(logger);
            using var history = new ClipboardHistoryService(logger);
            var writes = new List<string>();
            var sent = new List<KeyboardInterop.INPUT[]>();
            var actions = new ActionExecutor(config, logger, writes.Add, inputs =>
            {
                sent.Add(inputs);
                return (uint)inputs.Length;
            }, audio, media);
            using var vm = new MainViewModel(config, foreground, profiles, audio, actions, media, logger,
                appearanceFilePath: folder.AppearancePath, clipboardHistory: history);

            vm.ApplyTextInputContext(true);
            Assert.IsType<ButtonItemViewModel>(vm.Items[0]).Command.Execute(null);
            Assert.True(vm.IsEmojiPickerOpen);
            var choices = vm.Items.OfType<ButtonItemViewModel>().Skip(1).ToArray();
            Assert.Equal(32, choices.Length);
            foreach (var choice in choices)
            {
                var image = Assert.IsAssignableFrom<BitmapSource>(choice.AssetIcon);
                Assert.True(image.IsFrozen);
                Assert.True(choice.IsEmoji);
                Assert.True(image.PixelWidth >= 64);
                Assert.True(choice.Width >= 56);
                choice.Command.Execute(null);
                var inputs = sent[^1];
                Assert.Equal(choice.Label, new string(inputs.Where((_, index) => index % 2 == 0)
                    .Select(input => (char)input.U.ki.wScan).ToArray()));
                Assert.All(inputs, input => Assert.Equal(KeyboardInterop.KEYEVENTF_UNICODE,
                    input.U.ki.dwFlags & KeyboardInterop.KEYEVENTF_UNICODE));
            }
            Assert.Empty(writes);
            Assert.Equal(32, sent.Count);
            Assert.Equal("Emoji", vm.CurrentPageName);
            Assert.IsType<ButtonItemViewModel>(vm.Items[0]).Command.Execute(null);
            Assert.Equal("Input", vm.CurrentPageName);
            Assert.False(vm.IsEmojiPickerOpen);
        });
    }

    [Fact]
    public void EmojiPresentation_IsLargeThemeIndependentAndHasMissingImageFallback()
    {
        RunInSta(() =>
        {
            var surface = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var resource in new[] { "ThemeTokens", "BarControls" })
                surface.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/AdaptiveBarPC;component/Resources/{resource}.xaml", UriKind.Relative)
                });
            var image = AssetImageLoader.LoadBitmapFile(Path.Combine(AppContext.BaseDirectory,
                "Assets", "emoji", "1f600.png"), 64);
            Assert.NotNull(image);
            var template = (DataTemplate)surface.FindResource(new DataTemplateKey(typeof(ButtonItemViewModel)));
            foreach (var item in new[]
            {
                new ButtonItemViewModel { Label = "😀", Width = 56, IsEmoji = true, AssetIcon = image },
                new ButtonItemViewModel { Label = "❤️", Width = 56, IsEmoji = true },
                new ButtonItemViewModel { Label = "Normal", Width = 84 }
            })
                surface.Children.Add(new ContentControl { Content = item, ContentTemplate = template });

            foreach (var preset in new[] { "fluid", "pixel" })
            {
                AppearanceResolver.ApplyToResources(surface.Resources, AppearanceResolver.Resolve(
                    new ThemeDefinition { PrimaryText = "#FFFF00FF" }, new AppearanceDefinition { Preset = preset }));
                surface.Measure(new Size(240, 56));
                surface.Arrange(new Rect(0, 0, 240, 56));
                surface.UpdateLayout();
                var visuals = Descendants(surface).ToArray();
                var buttons = visuals.OfType<LongPressButton>().ToArray();
                Assert.Equal(new double[] { 56, 56, 56 }, buttons.Select(button => button.ActualHeight));
                Assert.All(buttons, button =>
                {
                    Assert.False(button.Focusable);
                    Assert.False(button.IsTabStop);
                });
                var artwork = Assert.Single(visuals.OfType<Image>(), item => item.Source is not null);
                Assert.Same(image, artwork.Source);
                Assert.Equal(32, artwork.ActualWidth);
                Assert.Equal(32, artwork.ActualHeight);
                Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(artwork));
                var fallback = Assert.Single(visuals.OfType<TextBlock>(),
                    item => item.Name == "EmojiFallback" && item.Visibility == Visibility.Visible);
                Assert.Equal("❤️", fallback.Text);
                Assert.Equal("Segoe UI Emoji", fallback.FontFamily.Source);
                Assert.Equal(TextTrimming.None, fallback.TextTrimming);
                Assert.Equal(28, fallback.FontSize);
            }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void StickerPicker_PastesConfiguredPngAndReturnsToInputContext()
    {
        RunInSta(() =>
        {
            using var profilesFolder = new InputProfiles();
            var imagePath = WriteTestPng(profilesFolder.Path).Replace('\\', '/');
            profilesFolder.WriteItems($$"""
            [
              { "id": "wave-sticker", "type": "sticker", "label": "Wave", "width": 72, "source": "{{imagePath}}" }
            ]
            """);

            var logger = new AppLogger(profilesFolder.Path);
            var config = new AppConfig();
            using var foreground = new ForegroundAppService(logger);
            using var profiles = new ProfileService(profilesFolder.Path, logger);
            using var audio = new FakeAudioProvider();
            using var media = new MediaSessionService(logger);
            using var history = new ClipboardHistoryService(logger);
            var images = new List<BitmapSource>();
            var actions = new ActionExecutor(
                config, logger, _ => { }, images.Add, inputs => (uint)inputs.Length, audio, media);
            using var vm = new MainViewModel(
                config, foreground, profiles, audio, actions, media, logger,
                clipboardHistory: history);

            vm.ApplyTextInputContext(true);
            Assert.IsType<ButtonItemViewModel>(vm.Items[3]).Command.Execute(null);

            Assert.Equal("Sticker", vm.CurrentPageName);
            var sticker = Assert.Single(vm.Items.OfType<ButtonItemViewModel>(), item => item.Label == "Wave");
            Assert.NotNull(sticker.AssetIcon);
            sticker.Command.Execute(null);

            Assert.Single(images);
            Assert.Equal("Input", vm.CurrentPageName);
        });
    }

    [Fact]
    public void InputPickers_IncludeGlobalItemsWithoutAddingThemToAPage()
    {
        RunInSta(() =>
        {
            using var profilesFolder = new InputProfiles();
            var imagePath = WriteTestPng(profilesFolder.Path).Replace('\\', '/');
            GlobalAppearanceStore.Save(new GlobalAppearanceSettings
            {
                InputItems =
                [
                    new ControlDefinition
                    {
                        Id = "shared-email",
                        Type = WidgetTypes.TextSlot,
                        Label = "Shared Email",
                        Width = 112,
                        Action = new ActionDefinition { Type = "clipboard_text", Value = "shared@example.com" }
                    },
                    new ControlDefinition
                    {
                        Id = "shared-sticker",
                        Type = WidgetTypes.Sticker,
                        Label = "Shared Sticker",
                        Width = 72,
                        Source = imagePath
                    }
                ]
            }, profilesFolder.AppearancePath);

            var logger = new AppLogger(profilesFolder.Path);
            var config = new AppConfig();
            using var foreground = new ForegroundAppService(logger);
            using var profiles = new ProfileService(profilesFolder.Path, logger);
            using var audio = new FakeAudioProvider();
            using var media = new MediaSessionService(logger);
            using var history = new ClipboardHistoryService(logger);
            var actions = new ActionExecutor(config, logger, _ => { }, _ => 1, audio, media);
            using var vm = new MainViewModel(
                config, foreground, profiles, audio, actions, media, logger,
                appearanceFilePath: profilesFolder.AppearancePath,
                clipboardHistory: history);

            vm.ApplyTextInputContext(true);
            Assert.IsType<ButtonItemViewModel>(vm.Items[2]).Command.Execute(null);
            Assert.Contains(vm.Items.OfType<ButtonItemViewModel>(), item => item.Label == "Shared Email");
            Assert.IsType<ButtonItemViewModel>(vm.Items.Single(item => item is ButtonItemViewModel button && button.Label == "Shared Email"))
                .Command.Execute(null);

            Assert.IsType<ButtonItemViewModel>(vm.Items[3]).Command.Execute(null);
            Assert.Contains(vm.Items.OfType<ButtonItemViewModel>(), item => item.Label == "Shared Sticker");
        });
    }

    [Theory]
    [InlineData(true, true, true, false, false, true)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(false, true, true, false, false, false)]
    public void TextInputDetection_OnlyActivatesForWritableNonPasswordEdits(
        bool isEdit,
        bool isEnabled,
        bool isKeyboardFocusable,
        bool isPassword,
        bool isReadOnly,
        bool expected)
    {
        Assert.Equal(expected, TextInputContextDetector.ShouldActivate(
            isEdit,
            isEnabled,
            isKeyboardFocusable,
            isPassword,
            isReadOnly));
    }

    [Fact]
    public void RuntimeProfile_BuildsAndExecutesAllBasicInputWidgets()
    {
        RunInSta(() =>
        {
            using var profilesFolder = new InputProfiles();
            profilesFolder.WriteItems("""
            [
              { "id": "wave", "type": "emoji", "label": "👋", "width": 56, "action": { "type": "text", "value": "👋" } },
              { "id": "clip", "type": "clipboard", "width": 150 },
              { "id": "slot", "type": "text_slot", "label": "Email", "width": 112, "action": { "type": "clipboard_text", "value": "hello@example.com" } }
            ]
            """);

            var logger = new AppLogger(profilesFolder.Path);
            var config = new AppConfig();
            using var foreground = new ForegroundAppService(logger);
            using var profiles = new ProfileService(profilesFolder.Path, logger);
            using var audio = new FakeAudioProvider();
            using var media = new MediaSessionService(logger);
            using var history = new ClipboardHistoryService(logger);
            history.RecordText("older clipboard text");
            history.RecordText("latest clipboard text");
            var writes = new List<string>();
            var sent = new List<KeyboardInterop.INPUT[]>();
            var actions = new ActionExecutor(
                config,
                logger,
                writes.Add,
                inputs =>
                {
                    sent.Add(inputs);
                    return (uint)inputs.Length;
                },
                audio,
                media);
            using var vm = new MainViewModel(
                config,
                foreground,
                profiles,
                audio,
                actions,
                media,
                logger,
                clipboardHistory: history);

            vm.ApplyForegroundApp(new ForegroundAppInfo(
                IntPtr.Zero, "", "", null, "Desktop", null, "Progman"));

            var emoji = Assert.IsType<ButtonItemViewModel>(vm.Items[0]);
            emoji.Command.Execute(null);
            Assert.Equal("Emoji", vm.CurrentPageName);
            Assert.Contains(vm.Items.OfType<ButtonItemViewModel>(), item => item.Label == "😀");
            Assert.IsType<ButtonItemViewModel>(vm.Items.First(
                    item => item is ButtonItemViewModel button && button.Label == "😀"))
                .Command.Execute(null);

            Assert.IsType<ButtonItemViewModel>(vm.Items[0]).Command.Execute(null);
            Assert.Equal("Main", vm.CurrentPageName);

            var clipboard = Assert.IsType<ButtonItemViewModel>(vm.Items[1]);
            clipboard.Command.Execute(null);
            Assert.Equal("Clipboard", vm.CurrentPageName);
            Assert.Equal("latest clipboard text", Assert.IsType<ButtonItemViewModel>(vm.Items[1]).Label);
            Assert.IsType<ButtonItemViewModel>(vm.Items[1]).Command.Execute(null);

            Assert.Equal("Main", vm.CurrentPageName);
            Assert.IsType<ButtonItemViewModel>(vm.Items[2]).Command.Execute(null);
            Assert.Equal(["latest clipboard text", "hello@example.com"], writes);
            Assert.Equal(3, sent.Count);
        });
    }

    [Fact]
    public void StickerWidget_IsPlaceableAndExposesImagePicker()
    {
        using var profiles = new InputProfiles();
        var vm = new DesignerViewModel(profiles.Path, profiles.AppearancePath);

        Assert.True(vm.AddInputItem(WidgetTypes.Sticker));

        var widget = Assert.IsType<ControlDefinition>(vm.SelectedWidget);
        var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);
        Assert.Equal("sticker", widget.Type);
        Assert.Equal("Sticker", widget.Label);
        Assert.Equal("", widget.Source);
        Assert.True(editor.HasStickerSettings);
        Assert.Same(widget, Assert.Single(vm.InputItems));
        Assert.Empty(vm.PreviewItems);
    }

    private sealed class InputProfiles : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AdaptiveBarPC_InputWidgets_" + Guid.NewGuid().ToString("N"));
        public string AppearancePath => System.IO.Path.Combine(Path, "settings", "appearance.json");

        public InputProfiles()
        {
            Directory.CreateDirectory(Path);
            WriteItems("[]");
        }

        public void WriteItems(string items)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "00-input.json"), $$"""
            {
              "id": "input-test",
              "displayName": "Input Test",
              "isDefault": true,
              "pages": [{ "id": "main", "displayName": "Main", "items": {{items}} }]
            }
            """);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }

    private sealed class FakeAudioProvider : IAudioProvider
    {
        public double GetMasterVolumePercent() => 50;
        public void SetMasterVolumePercent(double percent) { }
        public void ToggleMute() { }
        public bool GetMute() => false;
        public void Dispose() { }
    }

    private static string WriteTestPng(string directory) => WriteTestImage(directory, ".png");

    private static string WriteTestImage(string directory, string extension)
    {
        var path = System.IO.Path.Combine(directory, "sticker" + extension);
        BitmapEncoder encoder = extension == ".png" ? new PngBitmapEncoder() : new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(
            BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8)));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

}
