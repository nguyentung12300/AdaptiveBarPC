using System.IO;
using AdaptiveBarPC.Designer;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public class AssetIconTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [Fact]
    public void AssetIcon_LoadsWithoutLock_AndDrivesIconPresentation()
    {
        var root = Path.Combine(Path.GetTempPath(), "AdaptiveBar_AssetIcon_" + Guid.NewGuid().ToString("N"));
        var profiles = Path.Combine(root, "Profiles");
        var icon = Path.Combine(root, "Assets", "icons", "icon.png");
        Directory.CreateDirectory(profiles);
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.WriteAllBytes(icon, Convert.FromBase64String(OnePixelPng));

        try
        {
            var resolver = AssetPathResolver.ForProfilesDirectory(profiles);
            var source = AssetImageLoader.LoadIcon(resolver, "Assets/icons/icon.png");
            var vm = new ButtonItemViewModel
            {
                Label = "Asset",
                Icon = "Assets/icons/icon.png",
                IconKind = "asset",
                AssetIcon = source
            };

            Assert.NotNull(source);
            Assert.True(vm.HasAssetIcon);
            Assert.True(vm.HasIcon);
            Assert.False(vm.HasGlyphIcon);
            Assert.Equal("", vm.DisplayGlyph);

            File.Delete(icon);
            Assert.False(File.Exists(icon));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DecorativeMediaBitmap_LoadsAtDecodeSizeWithoutLock()
    {
        var root = Path.Combine(Path.GetTempPath(), "AdaptiveBar_DecorativeMedia_" + Guid.NewGuid().ToString("N"));
        var image = Path.Combine(root, "image.png");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(image, Convert.FromBase64String(OnePixelPng));

        try
        {
            Assert.NotNull(AssetImageLoader.LoadBitmapFile(image, 160));
            File.Delete(image);
            Assert.False(File.Exists(image));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MissingOrMalformedAsset_FallsBackToLabelSafely()
    {
        var root = Path.Combine(Path.GetTempPath(), "AdaptiveBar_AssetIcon_" + Guid.NewGuid().ToString("N"));
        var profiles = Path.Combine(root, "Profiles");
        var malformed = Path.Combine(root, "Assets", "icons", "broken.png");
        Directory.CreateDirectory(profiles);
        Directory.CreateDirectory(Path.GetDirectoryName(malformed)!);
        File.WriteAllText(malformed, "not an image");

        try
        {
            var resolver = AssetPathResolver.ForProfilesDirectory(profiles);
            Assert.Null(AssetImageLoader.LoadIcon(resolver, "Assets/icons/missing.png"));
            Assert.Null(AssetImageLoader.LoadIcon(resolver, "Assets/icons/broken.png"));

            var vm = new ButtonItemViewModel
            {
                Label = "Fallback",
                Icon = "Assets/icons/missing.png",
                IconKind = "asset"
            };
            Assert.False(vm.HasIcon);
            Assert.Equal("Fallback", vm.IconFallbackLabel);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void NoneKind_PreservesDerivedSliderAndSensorGlyphs()
    {
        var slider = new SliderItemViewModel("Brightness", 84, 0, 100, 50, null)
        {
            IconKind = "none"
        };
        var sensor = new SensorItemViewModel
        {
            Label = "CPU Temp",
            Metric = "temperature",
            IconKind = "none"
        };

        Assert.True(slider.HasGlyphIcon);
        Assert.Equal("\uE706", slider.DisplayGlyph);
        Assert.True(sensor.HasGlyphIcon);
        Assert.Equal(sensor.Glyph, sensor.DisplayGlyph);
    }

    [Fact]
    public void MissingAsset_DoesNotFallBackToDerivedGlyph()
    {
        var slider = new SliderItemViewModel("Brightness", 84, 0, 100, 50, null)
        {
            Icon = "Assets/icons/missing.png",
            IconKind = "asset"
        };
        var sensor = new SensorItemViewModel
        {
            Label = "CPU Temp",
            Metric = "temperature",
            Icon = "Assets/icons/missing.png",
            IconKind = "asset"
        };

        Assert.False(slider.HasGlyphIcon);
        Assert.Equal("Brightness", slider.IconFallbackLabel);
        Assert.False(sensor.HasGlyphIcon);
        Assert.Equal("", sensor.DisplayGlyph);
        Assert.Equal("CPU Temp", sensor.IconFallbackLabel);
    }

    [Fact]
    public void DesignerPreview_UsesSameProfilesRootForRelativeAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), "AdaptiveBar_DesignerAsset_" + Guid.NewGuid().ToString("N"));
        var profiles = Path.Combine(root, "Profiles");
        var icon = Path.Combine(root, "Assets", "icons", "icon.png");
        Directory.CreateDirectory(profiles);
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.WriteAllBytes(icon, Convert.FromBase64String(OnePixelPng));

        try
        {
            const string assetPath = "Assets/icons/icon.png";
            var runtimePath = AssetPathResolver.ForProfilesDirectory(profiles).ResolveExistingFile(assetPath);
            var preview = Assert.IsType<ButtonItemViewModel>(new DesignerPreviewAdapter(profiles).CreateContent(
                new ControlDefinition
                {
                    Id = "asset-button",
                    Type = WidgetTypes.Button,
                    Label = "Asset",
                    Icon = new IconDefinition { Kind = "asset", Value = assetPath }
                }));

            Assert.Equal(Path.GetFullPath(icon), runtimePath);
            Assert.NotNull(preview.AssetIcon);
            Assert.True(preview.HasAssetIcon);
            Assert.False(preview.HasGlyphIcon);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
