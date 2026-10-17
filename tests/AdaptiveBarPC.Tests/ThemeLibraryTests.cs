using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Tests;

public sealed class ThemeLibraryTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData(".gif")]
    public void SavedTheme_PackagesBackgroundAndImportsWithoutOriginalFiles(string extension)
    {
        RunInSta(() =>
        {
            using var temp = new TestDirectory();
            var preview = MakeImage(new PngBitmapEncoder());
            var background = MakeImage(extension == ".gif" ? new GifBitmapEncoder() : new PngBitmapEncoder());
            var source = Path.Combine(temp.Path, "original" + extension);
            File.WriteAllBytes(source, background);
            var settings = new GlobalAppearanceSettings
            {
                Theme = new ThemeDefinition { TileBackground = "#FF204060" },
                Appearance = new AppearanceDefinition { Preset = "pixel", SecondaryAccent = "#FFAABBCC" },
                BackgroundMedia = new BackgroundMediaDefinition { Source = source, Zoom = 1.7, OffsetX = 75, OffsetY = -15 },
                InputItems = [new ControlDefinition { Id = "private-slot", Action = new ActionDefinition { Value = "secret-text" } }]
            };
            var library = new ThemeLibraryStore(Path.Combine(temp.Path, "first-library"));
            var saved = library.Save("Đêm mưa", settings, source, preview);
            // Deleting this test fixture's original proves the saved theme owns its asset.
            File.Delete(source);
            Assert.Equal(source, settings.BackgroundMedia.Source);
            Assert.Equal("Đêm mưa", Assert.Single(library.List()).Name);
            Assert.Equal(background, File.ReadAllBytes(library.Load(saved).BackgroundMedia.Source!));
            var export = Path.Combine(temp.Path, "shared.adaptivebar-theme.zip");
            library.Export(saved, export);
            using (var zip = ZipFile.OpenRead(export))
            {
                Assert.Equal(3, zip.Entries.Count);
                using var reader = new StreamReader(zip.GetEntry("theme.json")!.Open());
                var json = reader.ReadToEnd();
                Assert.DoesNotContain("secret-text", json);
                Assert.DoesNotContain("inputItems", json);
                Assert.DoesNotContain(temp.Path.Replace("\\", "\\\\"), json);
                Assert.Contains("background" + extension, json);
            }
            var importedLibrary = new ThemeLibraryStore(Path.Combine(temp.Path, "second-library"));
            var imported = importedLibrary.Import(export);
            var restored = importedLibrary.Load(imported);
            Assert.Equal("Đêm mưa", imported.Name);
            Assert.Equal(preview, File.ReadAllBytes(imported.PreviewPath));
            Assert.Equal(background, File.ReadAllBytes(restored.BackgroundMedia.Source!));
            Assert.StartsWith(Path.Combine(temp.Path, "second-library"), restored.BackgroundMedia.Source!);
            Assert.Equal("pixel", restored.Appearance!.Preset);
            Assert.Equal("#FF204060", restored.Theme.TileBackground);
            Assert.Equal(1.7, restored.BackgroundMedia.Zoom);
            Assert.Equal(75, restored.BackgroundMedia.OffsetX);
            Assert.Equal(-15, restored.BackgroundMedia.OffsetY);
            Assert.Equal("Đêm mưa (2)", importedLibrary.Import(export).Name);
        });
    }

    [Fact]
    public void Save_CreatesIndependentCopiesAndRollsBackFailedMediaCopy()
    {
        RunInSta(() =>
        {
            using var temp = new TestDirectory();
            var library = new ThemeLibraryStore(Path.Combine(temp.Path, "library"));
            var preview = MakeImage(new PngBitmapEncoder());
            var settings = new GlobalAppearanceSettings();
            var first = library.Save("Custom", settings, null, preview);
            settings.Theme.PrimaryText = "#FF123456";
            var second = library.Save("Custom", settings, null, preview);
            Assert.Equal("Custom (2)", second.Name);
            Assert.NotEqual(library.Load(first).Theme.PrimaryText, library.Load(second).Theme.PrimaryText);
            settings.BackgroundMedia.Source = "missing.gif";
            Assert.Throws<FileNotFoundException>(() => library.Save("Missing", settings, Path.Combine(temp.Path, "missing.gif"), preview));
            Assert.Equal(2, library.List().Count);
            Assert.Empty(Directory.GetDirectories(Path.Combine(temp.Path, "library"), ".pending-*"));
        });
    }

    [Theory]
    [InlineData("../../outside.gif")]
    [InlineData("C:/outside.gif")]
    [InlineData("background.gif:payload")]
    public void Import_RejectsExternalBackgroundPaths(string source)
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Path, "bad.zip");
        var settings = new GlobalAppearanceSettings { BackgroundMedia = new BackgroundMediaDefinition { Source = source } };
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "theme.json", GlobalAppearanceStore.SerializeTheme(settings, "Bad"));
            WriteEntry(zip, "preview.png", [1]);
        }
        var library = new ThemeLibraryStore(Path.Combine(temp.Path, "library"));
        Assert.Throws<InvalidDataException>(() => library.Import(path));
        Assert.Empty(library.List());
        Assert.False(File.Exists(Path.Combine(temp.Path, "outside.gif")));
    }

    [Theory]
    [InlineData("../outside.png", false)]
    [InlineData("preview.png", false)]
    [InlineData("preview.png", true)]
    public void Import_RejectsTraversalDuplicateAndOversizedEntries(string entryName, bool oversized)
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Path, "bad.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "theme.json", GlobalAppearanceStore.SerializeTheme(new GlobalAppearanceSettings(), "Bad"));
            WriteEntry(zip, "preview.png", oversized ? new byte[4 * 1024 * 1024 + 1] : [1]);
            if (!oversized) WriteEntry(zip, entryName, [1]);
        }
        var library = new ThemeLibraryStore(Path.Combine(temp.Path, "library"));
        Assert.Throws<InvalidDataException>(() => library.Import(path));
        Assert.Empty(library.List());
        Assert.False(File.Exists(Path.Combine(temp.Path, "outside.png")));
    }

    [Fact]
    public void InvalidPreview_DoesNotLeaveAnIncompleteThemeAndBrokenLibraryEntriesAreSkipped()
    {
        using var temp = new TestDirectory();
        var root = Path.Combine(temp.Path, "library");
        var library = new ThemeLibraryStore(root);
        Assert.Throws<InvalidDataException>(() => library.Save("Bad preview", new GlobalAppearanceSettings(), null, [1, 2, 3]));
        Assert.Empty(Directory.GetDirectories(root));
        var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "theme.json"), "[]");
        Assert.Empty(library.List());
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(bytes);
    }

    private static byte[] MakeImage(BitmapEncoder encoder)
    {
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 30, 60, 90, 255, 40, 70, 100, 255, 50, 80, 110, 255, 60, 90, 120, 255 }, 8)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AdaptiveBarThemeLibrary_" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
