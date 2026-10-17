using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public sealed class GlobalAppearanceSettings
{
    public ThemeDefinition Theme { get; set; } = new();
    public AppearanceDefinition? Appearance { get; set; }
    public BackgroundMediaDefinition BackgroundMedia { get; set; } = new();
    public List<ControlDefinition> FixedLeft { get; set; } = new();
    public List<ControlDefinition> FixedRight { get; set; } = new();
    public List<ControlDefinition> InputItems { get; set; } = new();
}

public static class GlobalAppearanceStore
{
    private const string ThemeFormat = "adaptivebar-theme";
    private const string LegacyThemeFormat = "flexbar-theme";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AdaptiveBarPC",
        "appearance.json");

    private static string LegacyDefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlexBarPC",
        "appearance.json");

    public static GlobalAppearanceSettings Load(string? path = null)
    {
        var useLegacyDefault = path is null || string.Equals(path, DefaultPath, StringComparison.OrdinalIgnoreCase);
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path) && useLegacyDefault && File.Exists(LegacyDefaultPath))
                path = LegacyDefaultPath;

            if (!File.Exists(path))
                return new GlobalAppearanceSettings();

            var settings = JsonSerializer.Deserialize<GlobalAppearanceSettings>(File.ReadAllText(path), JsonOptions)
                ?? new GlobalAppearanceSettings();
            settings.Theme ??= new ThemeDefinition();
            settings.BackgroundMedia ??= new BackgroundMediaDefinition();
            Normalize(settings.BackgroundMedia);
            settings.FixedLeft ??= new List<ControlDefinition>();
            settings.FixedRight ??= new List<ControlDefinition>();
            settings.InputItems ??= new List<ControlDefinition>();
            return settings;
        }
        catch (JsonException)
        {
            return new GlobalAppearanceSettings();
        }
        catch (IOException)
        {
            return new GlobalAppearanceSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new GlobalAppearanceSettings();
        }
    }

    public static GlobalAppearanceSettings FromProfile(ProfileDefinition? profile)
    {
        if (profile is null)
            return new GlobalAppearanceSettings();

        var json = JsonSerializer.Serialize(new GlobalAppearanceSettings
        {
            Theme = profile.Theme ?? new ThemeDefinition(),
            Appearance = profile.Appearance
        }, JsonOptions);

        return JsonSerializer.Deserialize<GlobalAppearanceSettings>(json, JsonOptions)
            ?? new GlobalAppearanceSettings();
    }

    public static void Save(GlobalAppearanceSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SaveDocument(settings, path ?? DefaultPath);
    }

    public static void SaveTheme(GlobalAppearanceSettings settings, string path, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SaveDocument(ThemeDocument(settings, name), path);
    }

    public static byte[] SerializeTheme(GlobalAppearanceSettings settings, string? name = null) =>
        JsonSerializer.SerializeToUtf8Bytes(ThemeDocument(settings, name), JsonOptions);

    private static object ThemeDocument(GlobalAppearanceSettings settings, string? name) => new
        {
            Format = ThemeFormat,
            Version = 1,
            Name = name,
            settings.Theme,
            settings.Appearance,
            settings.BackgroundMedia
        };

    public static GlobalAppearanceSettings LoadTheme(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadTheme(stream);
    }

    public static GlobalAppearanceSettings ReadTheme(Stream stream)
    {
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String ||
            format.GetString() is not (ThemeFormat or LegacyThemeFormat) ||
            !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number != 1 ||
            !root.TryGetProperty("theme", out var theme) || theme.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Select an AdaptiveBarPC theme saved with Save Theme (version 1).");
        }

        var settings = new GlobalAppearanceSettings
        {
            Theme = theme.Deserialize<ThemeDefinition>(JsonOptions)!,
            Appearance = root.TryGetProperty("appearance", out var appearance)
                ? appearance.Deserialize<AppearanceDefinition>(JsonOptions)
                : null,
            BackgroundMedia = root.TryGetProperty("backgroundMedia", out var media)
                ? media.Deserialize<BackgroundMediaDefinition>(JsonOptions) ?? new BackgroundMediaDefinition()
                : new BackgroundMediaDefinition()
        };
        Normalize(settings.BackgroundMedia);
        return settings;
    }

    private static void SaveDocument<T>(T document, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Appearance path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void Normalize(BackgroundMediaDefinition media)
    {
        var fit = media.Fit?.Trim().ToLowerInvariant();
        media.Fit = fit is "contain" or "fill" or "cover" ? fit : "cover";
        media.Opacity = double.IsFinite(media.Opacity) ? Math.Clamp(media.Opacity, 0d, 1d) : 1d;
        media.OffsetX = double.IsFinite(media.OffsetX) ? Math.Clamp(media.OffsetX, -4096d, 4096d) : 0d;
        media.OffsetY = double.IsFinite(media.OffsetY) ? Math.Clamp(media.OffsetY, -4096d, 4096d) : 0d;
        media.Zoom = double.IsFinite(media.Zoom) ? Math.Clamp(media.Zoom, 0.1d, 4d) : 1d;
    }
}
