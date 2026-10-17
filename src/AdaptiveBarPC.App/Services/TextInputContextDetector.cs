using System.Windows.Automation;

namespace AdaptiveBarPC.Services;

internal static class TextInputContextDetector
{
    public static bool? Detect()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null)
                return false;

            var controlType = element.GetCurrentPropertyValue(
                AutomationElement.ControlTypeProperty,
                true) as ControlType;
            var enabled = ReadBool(element, AutomationElement.IsEnabledProperty);
            var focusable = ReadBool(element, AutomationElement.IsKeyboardFocusableProperty);
            var password = ReadBool(element, AutomationElement.IsPasswordProperty, fallback: true);
            var readOnly = element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                           ((ValuePattern)pattern).Current.IsReadOnly;

            return ShouldActivate(
                controlType == ControlType.Edit,
                enabled,
                focusable,
                password,
                readOnly);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static bool ShouldActivate(
        bool isEdit,
        bool isEnabled,
        bool isKeyboardFocusable,
        bool isPassword,
        bool isReadOnly) =>
        isEdit && isEnabled && isKeyboardFocusable && !isPassword && !isReadOnly;

    private static bool ReadBool(
        AutomationElement element,
        AutomationProperty property,
        bool fallback = false)
    {
        var value = element.GetCurrentPropertyValue(property, true);
        return value is bool result ? result : fallback;
    }
}
