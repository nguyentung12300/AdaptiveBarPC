using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;
using System.Windows.Media.Animation;

namespace AdaptiveBarPC.Tests;

public class DecorativeMediaTests
{
    private const string TinyAnimatedGif =
        "R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAAgABAAAIBQABAAgIACH5BAEKAAEALAAAAAACAAEAgQD/AAAAAAAAAAAAAAgFAAEACAgAOw==";

    [Theory]
    [InlineData("image.png", "StaticImage")]
    [InlineData("image.JPG", "StaticImage")]
    [InlineData("clip.gif", "Gif")]
    [InlineData("clip.mp4", "Video")]
    [InlineData("clip.webp", "Unsupported")]
    [InlineData("", "Unsupported")]
    public void ClassifySource_RecognizesSupportedMedia(string source, string expected) =>
        Assert.Equal(expected, DecorativeMediaView.ClassifySource(source).ToString());

    [Theory]
    [InlineData("contain", Stretch.Uniform)]
    [InlineData("cover", Stretch.UniformToFill)]
    [InlineData("fill", Stretch.Fill)]
    [InlineData("unknown", Stretch.Uniform)]
    public void ResolveStretch_MapsFitModes(string fit, Stretch expected) =>
        Assert.Equal(expected, DecorativeMediaView.ResolveStretch(fit));

    [Fact]
    public void ResolveMediaSize_CoverKeepsOverflowAvailableForPanAndZoom()
    {
        var covered = DecorativeMediaView.ResolveMediaSize(100, 50, 100, 100, "cover", 1);
        var zoomed = DecorativeMediaView.ResolveMediaSize(100, 50, 100, 100, "cover", 1.5);

        Assert.Equal(new Size(100, 100), covered);
        Assert.Equal(new Size(150, 150), zoomed);
    }

    [Fact]
    public void DecorativeMediaView_CoverKeepsOriginalMediaSizeAndOffsets()
    {
        var path = Path.Combine(Path.GetTempPath(), "AdaptiveBar_Offset_" + Guid.NewGuid().ToString("N") + ".png");
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
            new System.Windows.Media.Imaging.WriteableBitmap(200, 100, 96, 96, PixelFormats.Bgra32, null)));
        using (var stream = File.Create(path))
            encoder.Save(stream);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new DecorativeMediaView
                {
                    Source = path,
                    Fit = "cover",
                    Width = 100,
                    Height = 50,
                    ClipToBounds = false,
                    HorizontalOffset = 24,
                    VerticalOffset = -6
                };
                var window = new Window
                {
                    Content = view,
                    Width = 120,
                    Height = 70,
                    Left = -32000,
                    Top = -32000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None
                };

                try
                {
                    window.Show();
                    AuditRegressionTests.WaitFor(view.MediaLoadTask);
                    var mediaLayer = Assert.Single(view.Children.OfType<System.Windows.Controls.Canvas>());
                    Assert.False(mediaLayer.ClipToBounds);
                    var image = Assert.Single(mediaLayer.Children.OfType<System.Windows.Controls.Image>());
                    Assert.Equal(200, image.Width);
                    Assert.Equal(100, image.Height);
                    Assert.Equal(-50, System.Windows.Controls.Canvas.GetLeft(image));
                    Assert.Equal(-25, System.Windows.Controls.Canvas.GetTop(image));
                    var transforms = mediaLayer.Children
                        .OfType<FrameworkElement>()
                        .Take(2)
                        .Select(child => Assert.IsType<TranslateTransform>(child.RenderTransform))
                        .ToArray();

                    Assert.All(transforms, transform =>
                    {
                        Assert.Equal(24, transform.X);
                        Assert.Equal(-6, transform.Y);
                    });
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DecorativeMediaView_GifCreatesAnimationClockWithoutHoldingSourceFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "AdaptiveBar_Gif_" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, Convert.FromBase64String(TinyAnimatedGif));

        try
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var view = new DecorativeMediaView
                    {
                        Source = path,
                        Width = 160,
                        Height = 64
                    };
                    var window = new Window
                    {
                        Content = view,
                        Width = 1,
                        Height = 1,
                        Left = -32000,
                        Top = -32000,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None
                    };

                    window.Show();
                    AuditRegressionTests.WaitFor(view.MediaLoadTask);
                    var mediaLayer = Assert.Single(view.Children.OfType<System.Windows.Controls.Canvas>());
                    var image = Assert.Single(mediaLayer.Children.OfType<System.Windows.Controls.Image>());
                    var clock = Assert.IsType<AnimationClock>(view.GifClock);
                    Assert.True(Assert.IsType<ObjectAnimationUsingKeyFrames>(clock.Timeline).KeyFrames.Count >= 2);
                    view.MotionEnabled = false;
                    AuditRegressionTests.WaitFor(Task.Delay(50)); // Clock commands are applied on the next WPF timing tick.
                    Assert.True(clock.IsPaused);
                    view.MotionEnabled = true;
                    AuditRegressionTests.WaitFor(Task.Delay(50));
                    Assert.False(clock.IsPaused);
                    window.Close();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();

            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CorruptBitmap_FailsQuietlyWithoutHoldingSourceFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "AdaptiveBar_Corrupt_" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, [0x47, 0x49, 0x46, 0x38, 0x39]);

        try
        {
            Assert.Null(AssetImageLoader.LoadBitmapFile(path, 160));
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DecorativeMediaView_BlankSource_LoadsAndClosesWithoutThrowing()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new DecorativeMediaView
                {
                    Source = "",
                    Width = 160,
                    Height = 46,
                    ShowDiagnostics = true
                };
                var window = new Window
                {
                    Content = view,
                    Width = 180,
                    Height = 64,
                    Left = -32000,
                    Top = -32000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None
                };

                window.Show();
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void DesignerPreviewItem_RemainsHitTestableAfterMediaIsApplied()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AdaptiveBarPC.Designer",
            "MainWindow.xaml"));

        var document = System.Xml.Linq.XDocument.Parse(xaml);
        var hitTarget = Assert.Single(document.Descendants().Where(element =>
            (string?)element.Attribute("PreviewMouseLeftButtonDown") == "PreviewItem_OnPreviewMouseLeftButtonDown"));
        Assert.Equal("Grid", hitTarget.Name.LocalName);
        Assert.Equal("Transparent", (string?)hitTarget.Attribute("Background"));
    }

    [Fact]
    public void DesignerBackgroundPreview_AllowsMainPreviewMediaToOverflow()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AdaptiveBarPC.Designer",
            "MainWindow.xaml"));

        Assert.Contains(
            "ClipToBounds=\"False\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"BackgroundMediaFullFrame\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DesignerDragPayload_ResolvesFixedWidgetsOutsideSelectedPage()
    {
        var codeBehind = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AdaptiveBarPC.Designer",
            "MainWindow.xaml.cs"));

        Assert.Contains("widget = _viewModel.FindWidgetById(widgetId);", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignerEditor_MapsMediaFields_AndSameValueDoesNotDirty()
    {
        var root = CreateDesignerRoot(new ControlDefinition
        {
            Id = "media",
            Type = WidgetTypes.DecorativeMedia,
            Width = 120,
            Source = "Assets/media/a.gif",
            Fit = "cover",
            Loop = false,
            Opacity = 0.4,
            Enabled = false,
            OffsetX = 24,
            OffsetY = -6,
            Zoom = 1.5
        });

        try
        {
            var profiles = Path.Combine(root, "Profiles");
            var vm = new DesignerViewModel(profiles, Path.Combine(root, "appearance.json"));
            var editor = Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor);

            Assert.Contains(vm.Palette, x => x.TypeId == WidgetTypes.DecorativeMedia);
            Assert.True(editor.HasDecorativeMediaSettings);
            Assert.False(editor.HasLabel);
            Assert.Equal("Assets/media/a.gif", editor.MediaSource);
            Assert.Equal("cover", editor.MediaFit);
            Assert.False(editor.MediaLoop);
            Assert.Equal(0.4, editor.MediaOpacity);
            Assert.False(editor.MediaEnabled);
            Assert.Equal(24, editor.MediaOffsetX);
            Assert.Equal(-6, editor.MediaOffsetY);
            Assert.Equal(1.5, editor.MediaZoom);

            var preview = Assert.IsType<DecorativeMediaItemViewModel>(Assert.Single(vm.PreviewItems).Content);
            Assert.Equal(24, preview.HorizontalOffset);
            Assert.Equal(-6, preview.VerticalOffset);
            Assert.Equal(1.5, preview.Zoom);
            Assert.True(preview.ClipToBounds);

            vm.IsDecorativeMediaEditMode = true;
            Assert.False(Assert.IsType<DecorativeMediaItemViewModel>(Assert.Single(vm.PreviewItems).Content).ClipToBounds);

            editor.MediaSource = "Assets/media/a.gif";
            Assert.False(vm.IsDirty);

            editor.MediaSource = "Assets/media/b.gif";
            Assert.True(vm.IsDirty);
            Assert.Equal("Assets/media/b.gif", editor.Model.Source);
            Assert.Equal("Assets/media/b.gif", Assert.IsType<DecorativeMediaItemViewModel>(Assert.Single(vm.PreviewItems).Content).Source);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AddDecorativeMedia_UsesCanonicalDefaults()
    {
        var root = CreateDesignerRoot(new ControlDefinition
        {
            Id = "button",
            Type = WidgetTypes.Button,
            Label = "Button",
            Width = 84
        });

        try
        {
            var vm = new DesignerViewModel(Path.Combine(root, "Profiles"), Path.Combine(root, "appearance.json"));
            var definition = WidgetDefinitionRegistry.All.Single(x => x.TypeId == WidgetTypes.DecorativeMedia);

            Assert.True(vm.AddWidget(definition));
            var widget = Assert.IsType<ControlDefinition>(vm.SelectedWidget);
            Assert.Equal("", widget.Label);
            Assert.Equal("", widget.Source);
            Assert.Equal("contain", widget.Fit);
            Assert.True(widget.Loop);
            Assert.Equal(1d, widget.Opacity);
            Assert.True(widget.Enabled);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EditingOneWidget_DoesNotRecreateUnrelatedMediaPreview()
    {
        var root = CreateDesignerRoot(
            new ControlDefinition
            {
                Id = "media",
                Type = WidgetTypes.DecorativeMedia,
                Width = 120,
                Source = "Assets/media/missing.gif"
            },
            new ControlDefinition
            {
                Id = "button",
                Type = WidgetTypes.Button,
                Label = "Before",
                Width = 84
            });

        try
        {
            var vm = new DesignerViewModel(Path.Combine(root, "Profiles"), Path.Combine(root, "appearance.json"));
            vm.SelectWidgetById("button");
            var mediaBefore = vm.PreviewItems.Single(x => x.WidgetId == "media").Content;
            var buttonBefore = vm.PreviewItems.Single(x => x.WidgetId == "button").Content;

            Assert.IsType<WidgetEditorViewModel>(vm.SelectedWidgetEditor).Label = "After";

            Assert.Same(mediaBefore, vm.PreviewItems.Single(x => x.WidgetId == "media").Content);
            Assert.NotSame(buttonBefore, vm.PreviewItems.Single(x => x.WidgetId == "button").Content);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ImportMediaAsset_CopiesWithoutOverwrite_AndPreviewUsesManagedResolution()
    {
        var root = CreateDesignerRoot(new ControlDefinition
        {
            Id = "media",
            Type = WidgetTypes.DecorativeMedia,
            Width = 120
        });
        var source = Path.Combine(Path.GetTempPath(), $"adaptivebar-media-{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(source, Convert.FromBase64String(TinyAnimatedGif));

        try
        {
            var profiles = Path.Combine(root, "Profiles");
            var vm = new DesignerViewModel(profiles, Path.Combine(root, "appearance.json"));

            var availability = new List<bool>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.IsMediaImportAvailable)) availability.Add(vm.IsMediaImportAvailable);
            };
            var first = Assert.IsType<string>(await vm.ImportMediaAssetAsync(source));
            Assert.Equal(new[] { false, true }, availability);
            var second = Assert.IsType<string>(await vm.ImportMediaAssetAsync(source));
            Assert.Equal("Assets/media/" + Path.GetFileName(source), first);
            Assert.EndsWith("-2.gif", second, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(source));

            var firstPath = Path.Combine(root, first.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(firstPath));
            Assert.Equal(first, await vm.ImportMediaAssetAsync(firstPath));
            Assert.Null(await vm.ImportMediaAssetAsync(source + ".missing"));
            Assert.True(vm.IsMediaImportAvailable);
            Assert.Contains("Media import failed", vm.StatusMessage);

            var widget = vm.SelectedWidget!;
            widget.Source = first;
            var preview = Assert.IsType<DecorativeMediaItemViewModel>(new DesignerPreviewAdapter(profiles).CreateContent(widget));
            Assert.Equal(new AssetPathResolver(root).ResolveExistingFile(first), preview.ResolvedSource);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            Directory.Delete(root, true);
        }
    }

    private static string CreateDesignerRoot(params ControlDefinition[] widgets)
    {
        var root = Path.Combine(Path.GetTempPath(), "AdaptiveBarDecorativeMedia", Guid.NewGuid().ToString("N"));
        var profiles = Path.Combine(root, "Profiles");
        Directory.CreateDirectory(profiles);
        new ProfileDocumentSerializer().SaveFile(Path.Combine(profiles, "00-test.json"), new ProfileDefinition
        {
            Id = "test",
            DisplayName = "Test",
            Pages =
            [
                new PageDefinition
                {
                    Id = "main",
                    DisplayName = "Main",
                    Items = [.. widgets]
                }
            ]
        });
        return root;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AdaptiveBarPC.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
