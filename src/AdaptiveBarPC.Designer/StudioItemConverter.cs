using System.Globalization;
using System.Windows.Data;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Designer;

public sealed class StudioItemConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter as string == "ProfileIcon" && value is ProfileEntry profile)
            return profile.IsDefault ? "\uE7F4" : profile.Category?.ToLowerInvariant() switch
            {
                "browser" => "\uE774",
                "development" => "\uE943",
                "communication" => "\uE8F2",
                "game" or "gaming" => "\uE7FC",
                "media" => "\uE714",
                _ => "\uE80A"
            };
        if (parameter as string == "PageIcon" && value is DesignerPage page)
            return page.Id.ToLowerInvariant() switch
            {
                "tools" => "\uE90F",
                "system" => "\uE713",
                "mixer" => "\uE9E9",
                _ => "\uE8A5"
            };
        if (parameter as string == "Icon")
        {
            if (value is ControlDefinition widget)
            {
                if (widget.Icon?.Kind.Equals("glyph", StringComparison.OrdinalIgnoreCase) == true &&
                    !string.IsNullOrWhiteSpace(widget.Icon.Value)) return GlyphHelper.Resolve(widget.Icon.Value);
                if (widget.Action?.Type == "launch" && Uri.TryCreate(widget.Action.Value, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "http" or "https") return "\uE774";
            }
            return (value is ControlDefinition control ? control.Type : value as string) switch
            {
                WidgetTypes.Spacer => "\uE80A",
                WidgetTypes.Media => "\uE8D6",
                WidgetTypes.Slider or WidgetTypes.ExpandableSlider => "\uE767",
                WidgetTypes.DecorativeMedia or WidgetTypes.Sticker => "\uE91B",
                WidgetTypes.DynamicList => "\uE8FD",
                WidgetTypes.Sensor => "\uE9D9",
                WidgetTypes.TextSlot => "\uE8D2",
                WidgetTypes.Clipboard => "\uE77F",
                WidgetTypes.Toggle or WidgetTypes.MultiState => "\uE9E9",
                _ => "\uE8A7"
            };
        }
        if (value is ControlDefinition item)
            return !string.IsNullOrWhiteSpace(item.Label) ? item.Label :
                WidgetDefinitionRegistry.TryGet(item.Type, out var definition) ? definition.DisplayName : item.Type;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
