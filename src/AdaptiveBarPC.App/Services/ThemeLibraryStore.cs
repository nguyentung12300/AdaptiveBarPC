using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace AdaptiveBarPC.Services;

public sealed record ThemeLibraryEntry(string Id, string Name, string PreviewPath);

public sealed class ThemeLibraryStore
{
    private const int ManifestLimit = 256 * 1024;
    private const int PreviewLimit = 4 * 1024 * 1024;
    private const int MediaLimit = 128 * 1024 * 1024;
    private readonly string _root;

    public ThemeLibraryStore(string root) => _root = Path.GetFullPath(root);

    public IReadOnlyList<ThemeLibraryEntry> List()
    {
        if (!Directory.Exists(_root)) return [];
        var entries = new List<ThemeLibraryEntry>();
        foreach (var folder in Directory.EnumerateDirectories(_root))
        {
            var id = Path.GetFileName(folder);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                var path = Path.Combine(folder, "theme.json");
                if (new FileInfo(path).Length > ManifestLimit) continue;
                using var json = JsonDocument.Parse(File.ReadAllBytes(path));
                var name = ReadName(json.RootElement);
                GlobalAppearanceStore.LoadTheme(path);
                entries.Add(new ThemeLibraryEntry(id, name, Path.Combine(folder, "preview.png")));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                System.Diagnostics.Trace.TraceWarning($"Skipping saved theme {id}: {ex.Message}");
            }
        }
        return entries.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public ThemeLibraryEntry Save(string name, GlobalAppearanceSettings settings, string? mediaPath, byte[] preview)
    {
        name = UniqueName(ValidateName(name));
        if (preview.Length == 0 || preview.Length > PreviewLimit)
            throw new InvalidDataException("Theme preview must be between 1 byte and 4 MB.");
        using var snapshot = new MemoryStream(GlobalAppearanceStore.SerializeTheme(settings));
        var saved = GlobalAppearanceStore.ReadTheme(snapshot);
        var mediaName = string.IsNullOrWhiteSpace(saved.BackgroundMedia.Source)
            ? null : BackgroundFileName(mediaPath ?? throw new FileNotFoundException("The background media file is missing."));
        return CreateEntry(name, folder =>
        {
            if (mediaName is not null)
            {
                using var source = File.OpenRead(mediaPath!);
                using var destination = File.Create(Path.Combine(folder, mediaName));
                CopyLimited(source, destination, MediaLimit);
            }
            saved.BackgroundMedia.Source = mediaName;
            File.WriteAllBytes(Path.Combine(folder, "preview.png"), preview);
            GlobalAppearanceStore.SaveTheme(saved, Path.Combine(folder, "theme.json"), name);
        });
    }

    public GlobalAppearanceSettings Load(ThemeLibraryEntry entry)
    {
        var folder = EntryFolder(entry.Id);
        var settings = GlobalAppearanceStore.LoadTheme(Path.Combine(folder, "theme.json"));
        if (!string.IsNullOrWhiteSpace(settings.BackgroundMedia.Source))
        {
            var source = settings.BackgroundMedia.Source;
            if (source != BackgroundFileName(source))
                throw new InvalidDataException("Invalid background path in saved theme.");
            var path = Path.Combine(folder, source);
            if (!File.Exists(path)) throw new FileNotFoundException("The saved background media is missing.");
            settings.BackgroundMedia.Source = path;
        }
        return settings;
    }

    public void Export(ThemeLibraryEntry entry, string destination)
    {
        var folder = EntryFolder(entry.Id);
        var settings = Load(entry);
        var fullPath = Path.GetFullPath(destination);
        if (fullPath.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose an export location outside the theme library.");
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                AddFile(zip, Path.Combine(folder, "theme.json"), "theme.json", ManifestLimit);
                AddFile(zip, Path.Combine(folder, "preview.png"), "preview.png", PreviewLimit);
                if (!string.IsNullOrWhiteSpace(settings.BackgroundMedia.Source))
                    AddFile(zip, settings.BackgroundMedia.Source, BackgroundFileName(settings.BackgroundMedia.Source), MediaLimit);
            }
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public ThemeLibraryEntry Import(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count is < 2 or > 3 ||
            zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new InvalidDataException("The package must contain a theme, a preview and optional background media.");
        var manifest = zip.GetEntry("theme.json") ?? throw new InvalidDataException("Theme package is missing theme.json.");
        using var data = new MemoryStream();
        using (var source = manifest.Open()) CopyLimited(source, data, ManifestLimit);
        data.Position = 0;
        var settings = GlobalAppearanceStore.ReadTheme(data);
        data.Position = 0;
        using var json = JsonDocument.Parse(data);
        var name = UniqueName(ReadName(json.RootElement));
        var mediaName = settings.BackgroundMedia.Source;
        if (!string.IsNullOrWhiteSpace(mediaName) && mediaName != BackgroundFileName(mediaName))
            throw new InvalidDataException("The package background must use a local bundled filename.");
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["theme.json"] = ManifestLimit, ["preview.png"] = PreviewLimit
        };
        if (!string.IsNullOrWhiteSpace(mediaName)) expected[mediaName] = MediaLimit;
        if (zip.Entries.Count != expected.Count || zip.Entries.Any(e =>
                !expected.TryGetValue(e.FullName, out var limit) || e.Length <= 0 || e.Length > limit ||
                ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000))
            throw new InvalidDataException("The theme package contains an unexpected, oversized or invalid file.");

        return CreateEntry(name, folder =>
        {
            // Extract only exact allowlisted filenames; archive paths never choose a destination.
            foreach (var file in expected.Where(e => e.Key != "theme.json"))
            {
                using var source = zip.GetEntry(file.Key)!.Open();
                using var destination = File.Create(Path.Combine(folder, file.Key));
                CopyLimited(source, destination, file.Value);
            }
            GlobalAppearanceStore.SaveTheme(settings, Path.Combine(folder, "theme.json"), name);
        });
    }

    private ThemeLibraryEntry CreateEntry(string name, Action<string> write)
    {
        Directory.CreateDirectory(_root);
        var id = Guid.NewGuid().ToString("N");
        var pending = Path.Combine(_root, ".pending-" + id);
        Directory.CreateDirectory(pending);
        try
        {
            write(pending);
            if (StickerImageLoader.Load(Path.Combine(pending, "preview.png"), 640) is null)
                throw new InvalidDataException("The theme preview is not a valid image.");
            Directory.Move(pending, EntryFolder(id));
            return new ThemeLibraryEntry(id, name, Path.Combine(EntryFolder(id), "preview.png"));
        }
        finally
        {
            // Only the freshly created staging directory belongs to this operation.
            if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true);
        }
    }

    private string EntryFolder(string id) => Guid.TryParseExact(id, "N", out _)
        ? Path.Combine(_root, id) : throw new InvalidDataException("Invalid saved theme identifier.");

    private string UniqueName(string name)
    {
        var names = List().Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = name;
        for (var number = 2; names.Contains(result); number++)
            result = $"{name[..Math.Min(name.Length, 70)]} ({number})";
        return result;
    }

    private static string ReadName(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? ValidateName(name.GetString()!) : throw new InvalidDataException("Saved theme is missing its name.");

    private static string ValidateName(string name)
    {
        name = name.Trim();
        return name.Length is > 0 and <= 80 && !name.Any(char.IsControl)
            ? name : throw new InvalidDataException("Enter a theme name between 1 and 80 characters.");
    }

    private static string BackgroundFileName(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".mp4"
            ? "background" + extension : throw new InvalidDataException("Background must be PNG, JPG, GIF or MP4.");
    }

    private static void AddFile(ZipArchive zip, string path, string name, int limit)
    {
        using var source = File.OpenRead(path);
        using var destination = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        CopyLimited(source, destination, limit);
    }

    private static void CopyLimited(Stream source, Stream destination, int limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException($"Theme file exceeds {limit / 1024 / 1024} MB.");
            destination.Write(buffer, 0, read);
        }
    }
}
