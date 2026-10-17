using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public enum ProfileDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record ProfileDiagnostic(
    string Code,
    ProfileDiagnosticSeverity Severity,
    string Message,
    string? ProfileId = null,
    string? PageId = null,
    string? WidgetId = null);

public static class ProfileDiagnosticCodes
{
    public const string InvalidJson = nameof(InvalidJson);
    public const string UnsupportedSchemaVersion = nameof(UnsupportedSchemaVersion);
    public const string MissingProfileId = nameof(MissingProfileId);
    public const string DuplicateProfileId = nameof(DuplicateProfileId);
    public const string MissingPageId = nameof(MissingPageId);
    public const string DuplicatePageId = nameof(DuplicatePageId);
    public const string MissingWidgetId = nameof(MissingWidgetId);
    public const string DuplicateWidgetId = nameof(DuplicateWidgetId);
    public const string UnknownWidgetType = nameof(UnknownWidgetType);
    public const string RuntimeGeneratedWidgetType = nameof(RuntimeGeneratedWidgetType);
    public const string InvalidWidth = nameof(InvalidWidth);
    public const string MissingRequiredProvider = nameof(MissingRequiredProvider);
    public const string InvalidMatchConfiguration = nameof(InvalidMatchConfiguration);
    public const string InvalidIconAssetReference = nameof(InvalidIconAssetReference);
}

public sealed record ProfileDocumentResult(
    ProfileDefinition? Document,
    IReadOnlyList<ProfileDiagnostic> Diagnostics)
{
    public bool IsValid =>
        Document is not null &&
        Diagnostics.All(d => d.Severity != ProfileDiagnosticSeverity.Error);
}

public sealed class ProfileDocumentSerializer
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new(ReadOptions)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public ProfileDocumentResult Load(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ProfileDefinition>(json, ReadOptions);
            if (document is null)
            {
                return new ProfileDocumentResult(null,
                [
                    new(ProfileDiagnosticCodes.InvalidJson, ProfileDiagnosticSeverity.Error,
                        "Profile document is empty.")
                ]);
            }

            NormalizeLegacyIds(document);
            return new ProfileDocumentResult(document, ProfileDocumentValidator.Validate(document));
        }
        catch (JsonException ex)
        {
            return new ProfileDocumentResult(null,
            [
                new(ProfileDiagnosticCodes.InvalidJson, ProfileDiagnosticSeverity.Error, ex.Message)
            ]);
        }
    }

    public ProfileDocumentResult LoadFile(string path) => Load(File.ReadAllText(path));

    public string Save(ProfileDefinition document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = Clone(document);
        NormalizeLegacyIds(normalized);
        return JsonSerializer.Serialize(normalized, WriteOptions);
    }

    public void SaveFile(string path, ProfileDefinition document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Profile path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, Save(document), new UTF8Encoding(false));

            if (File.Exists(fullPath))
            {
                try
                {
                    File.Replace(tempPath, fullPath, null, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(tempPath, fullPath, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(tempPath, fullPath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public ProfileDocumentResult ToRuntime(ProfileDefinition document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = Clone(document);
        NormalizeLegacyIds(normalized);
        return new ProfileDocumentResult(normalized, ProfileDocumentValidator.Validate(normalized));
    }

    public IReadOnlyList<ProfileDiagnostic> Validate(ProfileDefinition document) =>
        ProfileDocumentValidator.Validate(document);

    private static ProfileDefinition Clone(ProfileDefinition document)
    {
        var json = JsonSerializer.Serialize(document, WriteOptions);
        return JsonSerializer.Deserialize<ProfileDefinition>(json, ReadOptions)
            ?? throw new InvalidDataException("Profile document could not be cloned.");
    }

    private static void NormalizeLegacyIds(ProfileDefinition profile)
    {
        if (profile.SchemaVersion <= 0)
            profile.SchemaVersion = CurrentSchemaVersion;

        NormalizeDecorativeMedia(profile.Items);
        NormalizeDecorativeMedia(profile.FixedLeft);
        NormalizeDecorativeMedia(profile.FixedRight);
        NormalizePageIds(profile);

        foreach (var page in profile.Pages)
        {
            NormalizeDecorativeMedia(page.Items);
            NormalizeWidgetIds(profile.Id, page);
        }

        // v0.1 compatibility: top-level Items are an implicit main page at runtime.
        NormalizeWidgetIds(profile.Id, new PageDefinition { Id = "main", Items = profile.Items });
        NormalizeWidgetIds(profile.Id, new PageDefinition { Id = "fixed-left", Items = profile.FixedLeft });
        NormalizeWidgetIds(profile.Id, new PageDefinition { Id = "fixed-right", Items = profile.FixedRight });
    }

    private static void NormalizeDecorativeMedia(IEnumerable<ControlDefinition> widgets)
    {
        foreach (var widget in widgets.Where(x =>
                     x.Type.Equals(WidgetTypes.DecorativeMedia, StringComparison.OrdinalIgnoreCase)))
        {
            widget.Fit = widget.Fit?.Trim().ToLowerInvariant() switch
            {
                "cover" => "cover",
                "fill" => "fill",
                _ => "contain"
            };
            widget.Loop ??= true;
            widget.Enabled ??= true;
            widget.Opacity = Math.Clamp(
                double.IsFinite(widget.Opacity ?? 1d) ? widget.Opacity ?? 1d : 1d,
                0d,
                1d);
        }
    }

    private static void NormalizePageIds(ProfileDefinition profile)
    {
        var missing = profile.Pages.Where(p => string.IsNullOrWhiteSpace(p.Id)).ToList();
        var groups = missing.GroupBy(PageIdentitySeed, StringComparer.Ordinal).ToList();

        foreach (var group in groups)
        {
            if (group.Count() == 1)
            {
                group.First().Id = CompatibilityId("page", profile.Id + "|" + group.Key);
                continue;
            }

            foreach (var page in group)
            {
                page.Id = CompatibilityId(
                    "page",
                    profile.Id + "|" + group.Key + "|legacy-name:" + page.DisplayName.Trim());
            }
        }
    }

    private static void NormalizeWidgetIds(string profileId, PageDefinition page)
    {
        var missing = page.Items.Where(i => string.IsNullOrWhiteSpace(i.Id)).ToList();
        var groups = missing.GroupBy(WidgetIdentitySeed, StringComparer.Ordinal).ToList();

        foreach (var group in groups)
        {
            if (group.Count() == 1)
            {
                group.First().Id = CompatibilityId("widget", profileId + "|" + page.Id + "|" + group.Key);
                continue;
            }

            // Legacy files can contain otherwise indistinguishable controls. Label is used only
            // to disambiguate the one-time compatibility migration; saved documents persist the ID.
            foreach (var widget in group)
            {
                widget.Id = CompatibilityId(
                    "widget",
                    profileId + "|" + page.Id + "|" + group.Key + "|legacy-label:" + widget.Label.Trim());
            }
        }
    }

    private static string PageIdentitySeed(PageDefinition page) =>
        string.Join("||", page.Items.Select(WidgetIdentitySeed).OrderBy(x => x, StringComparer.Ordinal));

    private static string WidgetIdentitySeed(ControlDefinition widget)
    {
        static string ActionSeed(ActionDefinition? action) =>
            action is null ? "" : $"{action.Type.Trim().ToLowerInvariant()}:{action.Value?.Trim()}";

        return string.Join("|",
            widget.Type.Trim().ToLowerInvariant(),
            widget.Provider?.Trim() ?? "",
            widget.Metric?.Trim() ?? "",
            ActionSeed(widget.Action),
            ActionSeed(widget.SecondaryAction),
            ActionSeed(widget.OnAction),
            ActionSeed(widget.OffAction));
    }

    private static string CompatibilityId(string prefix, string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return $"{prefix}-{Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant()}";
    }
}

public static class ProfileDocumentValidator
{
    public static IReadOnlyList<ProfileDiagnostic> Validate(ProfileDefinition profile)
    {
        var diagnostics = new List<ProfileDiagnostic>();
        var profileId = profile.Id;

        if (profile.SchemaVersion != ProfileDocumentSerializer.CurrentSchemaVersion)
        {
            diagnostics.Add(new(
                ProfileDiagnosticCodes.UnsupportedSchemaVersion,
                ProfileDiagnosticSeverity.Error,
                $"Schema version {profile.SchemaVersion} is not supported. Current version is {ProfileDocumentSerializer.CurrentSchemaVersion}.",
                profileId));
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            diagnostics.Add(new(
                ProfileDiagnosticCodes.MissingProfileId,
                ProfileDiagnosticSeverity.Error,
                "Profile id is required."));
        }

        ValidateMatch(profile, diagnostics);

        var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in profile.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.MissingPageId,
                    ProfileDiagnosticSeverity.Error,
                    "Page id is required.",
                    profileId));
            }
            else if (!pageIds.Add(page.Id))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.DuplicatePageId,
                    ProfileDiagnosticSeverity.Error,
                    $"Duplicate page id '{page.Id}'.",
                    profileId,
                    page.Id));
            }

            ValidateWidgets(profileId, page.Id, page.Items, diagnostics);
        }

        ValidateWidgets(profileId, "main", profile.Items, diagnostics);
        ValidateWidgets(profileId, "fixed-left", profile.FixedLeft, diagnostics);
        ValidateWidgets(profileId, "fixed-right", profile.FixedRight, diagnostics);
        return diagnostics;
    }

    public static IReadOnlyList<ProfileDiagnostic> ValidateProfiles(IEnumerable<ProfileDefinition> profiles)
    {
        var diagnostics = new List<ProfileDiagnostic>();
        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            diagnostics.AddRange(Validate(profile));
            if (!string.IsNullOrWhiteSpace(profile.Id) && !profileIds.Add(profile.Id))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.DuplicateProfileId,
                    ProfileDiagnosticSeverity.Error,
                    $"Duplicate profile id '{profile.Id}'.",
                    profile.Id));
            }
        }

        return diagnostics;
    }

    private static void ValidateWidgets(
        string profileId,
        string? pageId,
        IEnumerable<ControlDefinition> widgets,
        List<ProfileDiagnostic> diagnostics)
    {
        var widgetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var widget in widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.MissingWidgetId,
                    ProfileDiagnosticSeverity.Error,
                    "Widget id is required.",
                    profileId,
                    pageId));
            }
            else if (!widgetIds.Add(widget.Id))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.DuplicateWidgetId,
                    ProfileDiagnosticSeverity.Error,
                    $"Duplicate widget id '{widget.Id}'.",
                    profileId,
                    pageId,
                    widget.Id));
            }

            if (!WidgetDefinitionRegistry.TryGet(widget.Type, out var definition))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.UnknownWidgetType,
                    ProfileDiagnosticSeverity.Error,
                    $"Unknown widget type '{widget.Type}'.",
                    profileId,
                    pageId,
                    widget.Id));
                continue;
            }

            if (!definition.IsPlaceable)
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.RuntimeGeneratedWidgetType,
                    ProfileDiagnosticSeverity.Error,
                    $"Widget type '{widget.Type}' is runtime-generated and cannot be placed in a profile.",
                    profileId,
                    pageId,
                    widget.Id));
            }

            if (double.IsNaN(widget.Width) ||
                double.IsInfinity(widget.Width) ||
                (widget.Width <= 0 && !definition.AllowsFlexibleWidth) ||
                (widget.Width > 0 && (widget.Width < definition.MinWidth || widget.Width > definition.MaxWidth)))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.InvalidWidth,
                    ProfileDiagnosticSeverity.Error,
                    $"Width {widget.Width} is invalid for widget type '{widget.Type}'.",
                    profileId,
                    pageId,
                    widget.Id));
            }

            if (definition.RequiresProvider && string.IsNullOrWhiteSpace(widget.Provider))
            {
                diagnostics.Add(new(
                    ProfileDiagnosticCodes.MissingRequiredProvider,
                    ProfileDiagnosticSeverity.Warning,
                    $"Widget type '{widget.Type}' should declare a provider id. Runtime legacy fallback to widget id remains supported.",
                    profileId,
                    pageId,
                    widget.Id));
            }

            ValidateIcon(profileId, pageId, widget, diagnostics);
        }
    }

    private static void ValidateMatch(ProfileDefinition profile, List<ProfileDiagnostic> diagnostics)
    {
        var invalid =
            profile.Match.ProcessNames.Any(string.IsNullOrWhiteSpace) ||
            profile.Match.ExecutablePaths.Any(string.IsNullOrWhiteSpace) ||
            profile.Match.Categories.Any(string.IsNullOrWhiteSpace) ||
            (profile.Match.WindowTitleContains is not null && string.IsNullOrWhiteSpace(profile.Match.WindowTitleContains)) ||
            (profile.Match.Category is not null && string.IsNullOrWhiteSpace(profile.Match.Category));

        if (invalid)
        {
            diagnostics.Add(new(
                ProfileDiagnosticCodes.InvalidMatchConfiguration,
                ProfileDiagnosticSeverity.Error,
                "Match entries must not be empty or whitespace.",
                profile.Id));
        }
    }

    private static void ValidateIcon(
        string profileId,
        string? pageId,
        ControlDefinition widget,
        List<ProfileDiagnostic> diagnostics)
    {
        if (widget.Icon is null ||
            !widget.Icon.Kind.Equals("asset", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(widget.Icon.Value))
        {
            return;
        }

        var value = widget.Icon.Value.Replace('\\', '/');
        var invalidRelativeTraversal =
            !Path.IsPathRooted(widget.Icon.Value) &&
            value.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..");

        var malformed = false;
        try
        {
            _ = Path.GetFullPath(widget.Icon.Value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            malformed = true;
        }

        if (invalidRelativeTraversal || malformed)
        {
            diagnostics.Add(new(
                ProfileDiagnosticCodes.InvalidIconAssetReference,
                ProfileDiagnosticSeverity.Error,
                "Icon asset reference is malformed or escapes its managed asset root.",
                profileId,
                pageId,
                widget.Id));
        }
    }
}
