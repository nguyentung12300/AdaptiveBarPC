using System.IO;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public class AssetPathResolverTests
{
    [Fact]
    public void ResolvesRelativeAndAbsoluteFiles_AndFailsSafely()
    {
        var root = Path.Combine(Path.GetTempPath(), "AdaptiveBar_Assets_" + Guid.NewGuid().ToString("N"));
        var profiles = Path.Combine(root, "Profiles");
        var icon = Path.Combine(root, "Assets", "icons", "icon.png");
        var outside = Path.Combine(Path.GetTempPath(), "AdaptiveBar_Outside_" + Guid.NewGuid().ToString("N") + ".png");

        Directory.CreateDirectory(profiles);
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.WriteAllBytes(icon, [1]);
        File.WriteAllBytes(outside, [2]);

        try
        {
            var resolver = AssetPathResolver.ForProfilesDirectory(profiles);

            Assert.Equal(Path.GetFullPath(icon), resolver.ResolveExistingFile("Assets/icons/icon.png"));
            Assert.Equal(Path.GetFullPath(outside), resolver.ResolveExistingFile(outside));
            Assert.Null(resolver.ResolveExistingFile("Assets/icons/missing.png"));
            Assert.Null(resolver.ResolveExistingFile("../outside.png"));
            Assert.Null(resolver.ResolveExistingFile("bad\0path.png"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
            if (File.Exists(outside))
                File.Delete(outside);
        }
    }

    [Fact]
    public void ManagedMediaPaths_RemainPortableAfterRootMove()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "AdaptiveBar_Portable_" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(basePath, "one");
        var profiles = Path.Combine(root, "Profiles");
        var media = Path.Combine(root, "Assets", "media");
        Directory.CreateDirectory(profiles);
        Directory.CreateDirectory(media);
        File.WriteAllBytes(Path.Combine(media, "image.png"), [1]);
        File.WriteAllBytes(Path.Combine(media, "anim.gif"), [1]);
        File.WriteAllBytes(Path.Combine(media, "clip.mp4"), [1]);

        try
        {
            var resolver = AssetPathResolver.ForProfilesDirectory(profiles);
            Assert.NotNull(resolver.ResolveExistingFile("Assets/media/image.png"));
            Assert.NotNull(resolver.ResolveExistingFile("Assets/media/anim.gif"));
            Assert.NotNull(resolver.ResolveExistingFile("Assets/media/clip.mp4"));

            var moved = Path.Combine(basePath, "two");
            Directory.Move(root, moved);
            var movedResolver = AssetPathResolver.ForProfilesDirectory(Path.Combine(moved, "Profiles"));
            Assert.Equal(Path.Combine(moved, "Assets", "media", "image.png"), movedResolver.ResolveExistingFile("Assets/media/image.png"));
            Assert.Equal(Path.Combine(moved, "Assets", "media", "anim.gif"), movedResolver.ResolveExistingFile("Assets/media/anim.gif"));
            Assert.Equal(Path.Combine(moved, "Assets", "media", "clip.mp4"), movedResolver.ResolveExistingFile("Assets/media/clip.mp4"));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, true);
        }
    }
}
