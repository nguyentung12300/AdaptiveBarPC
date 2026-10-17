namespace AdaptiveBarPC.Models;

public sealed record WidgetDefinition(
    string TypeId,
    string DisplayName,
    string Category,
    double DefaultWidth,
    double MinWidth,
    double MaxWidth,
    bool AllowsFlexibleWidth,
    bool IsPlaceable,
    bool RequiresProvider = false);

public static class WidgetDefinitionRegistry
{
    private static readonly Dictionary<string, WidgetDefinition> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [WidgetTypes.Button] = new(WidgetTypes.Button, "Button", "Controls", 84, 40, 360, false, true),
            [WidgetTypes.Emoji] = new(WidgetTypes.Emoji, "Emoji", "Input", 84, 40, 240, false, true),
            [WidgetTypes.Clipboard] = new(WidgetTypes.Clipboard, "Clipboard", "Input", 96, 48, 240, false, true),
            [WidgetTypes.TextSlot] = new(WidgetTypes.TextSlot, "Text slot", "Input", 112, 48, 360, false, true),
            [WidgetTypes.Sticker] = new(WidgetTypes.Sticker, "Sticker", "Input", 72, 48, 240, false, true),
            [WidgetTypes.Toggle] = new(WidgetTypes.Toggle, "Toggle", "Controls", 96, 40, 360, false, true),
            [WidgetTypes.MultiState] = new(WidgetTypes.MultiState, "Multi-state", "Controls", 110, 48, 420, false, true),
            [WidgetTypes.Slider] = new(WidgetTypes.Slider, "Slider", "Controls", 180, 80, 480, false, true),
            [WidgetTypes.ExpandableSlider] = new(WidgetTypes.ExpandableSlider, "Expandable slider", "Controls", 56, 56, 56, false, true),
            [WidgetTypes.Media] = new(WidgetTypes.Media, "Media", "Media", 0, 120, 900, true, true),
            [WidgetTypes.DecorativeMedia] = new(WidgetTypes.DecorativeMedia, "Decorative Media", "Media", 120, 40, 900, false, true),
            [WidgetTypes.DynamicList] = new(WidgetTypes.DynamicList, "Dynamic list", "Dynamic", 0, 80, 1600, true, true, true),
            [WidgetTypes.Sensor] = new(WidgetTypes.Sensor, "Sensor", "Dynamic", 96, 48, 320, false, true),
            [WidgetTypes.Spacer] = new(WidgetTypes.Spacer, "Spacer", "Layout", 6, 1, 800, false, true),
            [WidgetTypes.AudioSession] = new(WidgetTypes.AudioSession, "Audio session", "Runtime", 180, 80, 480, false, false)
        };

    public static IReadOnlyCollection<WidgetDefinition> All => Definitions.Values;

    public static bool TryGet(string? typeId, out WidgetDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(typeId) && Definitions.TryGetValue(typeId.Trim(), out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }
}
