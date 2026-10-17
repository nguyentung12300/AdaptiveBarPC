using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdaptiveBarPC.Models;

public sealed class ProfileDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Priority { get; set; } = 0;
    public bool IsDefault { get; set; } = false;
    public MatchDefinition Match { get; set; } = new();

    // v0.1 compatibility: a profile can still define Items directly.
    public List<ControlDefinition> Items { get; set; } = new();

    // v0.2: multiple swipeable pages.
    public List<PageDefinition> Pages { get; set; } = new();

    // Profile-scoped controls that remain mounted while pages change.
    public List<ControlDefinition> FixedLeft { get; set; } = new();
    public List<ControlDefinition> FixedRight { get; set; } = new();

    // v0.2: optional per-profile visual theme.
    public ThemeDefinition Theme { get; set; } = new();

    // Phase 6: optional user-facing appearance intent. Legacy Theme remains supported.
    public AppearanceDefinition? Appearance { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class MatchDefinition
{
    public List<string> ProcessNames { get; set; } = new();
    public List<string> ExecutablePaths { get; set; } = new();
    public string? WindowTitleContains { get; set; }
    public string? Category { get; set; }
    public List<string> Categories { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class PageDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<ControlDefinition> Items { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ThemeDefinition
{
    public string? Background { get; set; } = "#FF0E0E11";
    public string? TileBackground { get; set; } = "#FF222228";
    public string? Accent { get; set; } = "#FF7C5CFC";
    public string? PrimaryText { get; set; } = "#E6F5F5F7";
    public string? SecondaryText { get; set; } = "#80F5F5F7";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class AppearanceDefinition
{
    public string? Preset { get; set; }
    public string? Accent { get; set; }
    public string? SecondaryAccent { get; set; }
    public double? Intensity { get; set; }
    public bool? MotionEnabled { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class BackgroundMediaDefinition
{
    public string? Source { get; set; }
    public string Fit { get; set; } = "cover";
    public bool Loop { get; set; } = true;
    public double Opacity { get; set; } = 1d;
    public bool Enabled { get; set; } = true;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Zoom { get; set; } = 1d;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ControlDefinition
{
    public string Type { get; set; } = "button";
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public IconDefinition? Icon { get; set; }
    public double Width { get; set; } = 84;
    public double Min { get; set; } = 0;
    public double Max { get; set; } = 100;
    public double? Value { get; set; }
    public bool? Enabled { get; set; }

    // Phase 8 decorative_media configuration.
    public string? Source { get; set; }
    public string? Fit { get; set; }
    public bool? Loop { get; set; }
    public double? Opacity { get; set; }
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
    public double? Zoom { get; set; }

    // Primary tap action.
    public ActionDefinition? Action { get; set; }

    // v0.2: action fired after long press.
    public ActionDefinition? SecondaryAction { get; set; }

    // v0.2: override the app-wide long-press threshold for this control.
    public int? LongPressMs { get; set; }

    // v0.2: route item to the pinned system-controls panel on the right.
    public bool System { get; set; } = false;

    // v0.2: when true, collapse this control completely when its underlying feature (e.g. media) is unavailable.
    public bool CollapseWhenUnavailable { get; set; } = false;
    public bool AutoCollapse { get; set; } = false;

    // DynamicList foundation
    public string? Provider { get; set; }
    public string? EmptyBehavior { get; set; } // "collapse", "compact", "hidden"
    public int? MaxItems { get; set; }

    // Toggle foundation
    public bool IsChecked { get; set; } = false;
    public string? OnLabel { get; set; }
    public string? OffLabel { get; set; }
    public ActionDefinition? OnAction { get; set; }
    public ActionDefinition? OffAction { get; set; }

    // MultiState foundation
    public string? State { get; set; }
    public List<WidgetStateDefinition> States { get; set; } = new();

    // Sensor foundation
    public string? Metric { get; set; }
    public string? Unit { get; set; }
    public string? Format { get; set; }
    public double? WarningThreshold { get; set; }
    public double? CriticalThreshold { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class IconDefinition
{
    public string Kind { get; set; } = "none";
    public string? Value { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class WidgetStateDefinition
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Glyph { get; set; }
    public ActionDefinition? Action { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ActionDefinition
{
    public string Type { get; set; } = "";
    public string? Value { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
