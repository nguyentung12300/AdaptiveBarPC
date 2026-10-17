using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public sealed record ResolvedAppearance(
    string Preset,
    string SurfaceBase,
    string SurfaceControl,
    string TextPrimary,
    string TextSecondary,
    string AccentPrimary,
    string AccentSecondary,
    double Intensity,
    bool MotionEnabled,
    double ControlCornerRadius = 8.0,
    double SurfaceCornerRadius = 12.0,
    string StrokeSubtle = "#18FFFFFF",
    bool IsPixel = false,
    string PixelOutline = "#00000000",
    string PixelHighlight = "#00FFFFFF",
    string PixelShadow = "#00000000",
    BitmapScalingMode IconScalingMode = BitmapScalingMode.Linear);

public static class AppearanceResolver
{
    public const string Fluid = "fluid";
    public const string Oled = "oled";
    public const string Cyber = "cyber";
    public const string Rgb = "rgb";
    public const string Pixel = "pixel";

    public static (string Base, string Control, string TextPrimary, string TextSecondary, string Accent, string SecondaryAccent) GetPresetDefaults(string preset)
    {
        return preset switch
        {
            Oled => ("#FF000000", "#FF0A0A0E", "#FFFFFFFF", "#FF9E9EA8", "#FFFFFFFF", "#FF888899"),
            Cyber => ("#FF080A12", "#FF101626", "#FFF0F6FF", "#FF7088A8", "#FF00F0FF", "#FFFF007F"),
            Rgb => ("#FF0C0A14", "#FF181424", "#FFF5F2FF", "#FF9A90B0", "#FFFF3366", "#FF00D4FF"),
            Pixel => ("#FF0A0B10", "#FF181A26", "#FFFFFFFF", "#FF8D93A8", "#FF00E5FF", "#FFFF0055"),
            _ => ("#FF0A0A0D", "#FF1C1C24", "#FFF5F5F7", "#FFA1A1AA", "#FF7C5CFC", "#FF7C5CFC")
        };
    }

    public static string GetEffectiveColor(string? userValue, string presetDefault, bool isFluid, string legacyDefault, string altLegacy)
    {
        if (string.IsNullOrWhiteSpace(userValue))
            return presetDefault;

        if (!isFluid && (string.Equals(userValue, legacyDefault, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(userValue, altLegacy, StringComparison.OrdinalIgnoreCase)))
        {
            return presetDefault;
        }

        return userValue;
    }

    public static string DeriveAlphaHex(string hex, byte alpha)
    {
        try
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            return $"#{alpha:X2}{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        }
        catch
        {
            return hex;
        }
    }

    public static ResolvedAppearance Resolve(ThemeDefinition? theme, AppearanceDefinition? appearance, bool enableAnimations = true)
    {
        theme ??= new ThemeDefinition();

        var preset = appearance?.Preset?.Trim().ToLowerInvariant();
        if (preset is not (Fluid or Oled or Cyber or Rgb or Pixel))
            preset = Fluid;

        var defaults = GetPresetDefaults(preset);

        var accent = string.IsNullOrWhiteSpace(appearance?.Accent)
            ? (string.IsNullOrWhiteSpace(theme.Accent) || (string.Equals(theme.Accent, "#FF7C5CFC", StringComparison.OrdinalIgnoreCase) && preset != Fluid) ? defaults.Accent : theme.Accent)
            : appearance.Accent!;

        var secondaryAccent = string.IsNullOrWhiteSpace(appearance?.SecondaryAccent)
            ? (preset == Fluid ? accent : defaults.SecondaryAccent)
            : appearance.SecondaryAccent!;

        var intensity = Math.Clamp(appearance?.Intensity ?? 1.0, 0.0, 1.0);
        var motionEnabled = enableAnimations && (appearance?.MotionEnabled ?? true);

        string surfaceBase = GetEffectiveColor(theme.Background, defaults.Base, isFluid: preset == Fluid, legacyDefault: "#FF0E0E11", altLegacy: "#FF0A0A0D");
        string surfaceControl = GetEffectiveColor(theme.TileBackground, defaults.Control, isFluid: preset == Fluid, legacyDefault: "#FF222228", altLegacy: "#FF1C1C24");
        string textPrimary = GetEffectiveColor(theme.PrimaryText, defaults.TextPrimary, isFluid: preset == Fluid, legacyDefault: "#E6F5F5F7", altLegacy: "#FFF5F5F7");
        string textSecondary = GetEffectiveColor(theme.SecondaryText, defaults.TextSecondary, isFluid: preset == Fluid, legacyDefault: "#80F5F5F7", altLegacy: "#FFA1A1AA");

        double controlRadius = 8.0;
        double surfaceRadius = 12.0;
        string strokeSubtle = "#18FFFFFF";
        bool isPixel = false;
        string pixelOutline = "#00000000";
        string pixelHighlight = "#00FFFFFF";
        string pixelShadow = "#00000000";
        var iconScalingMode = BitmapScalingMode.Linear;

        if (appearance is not null || preset != Fluid)
        {
            switch (preset)
            {
                case Oled:
                    strokeSubtle = "#22FFFFFF";
                    break;

                case Cyber:
                    controlRadius = 6.0;
                    surfaceRadius = 10.0;
                    strokeSubtle = DeriveAlphaHex(secondaryAccent, 0x40);
                    break;

                case Rgb:
                    strokeSubtle = "#20FFFFFF";
                    break;

                case Pixel:
                    controlRadius = 0.0;
                    surfaceRadius = 0.0;
                    strokeSubtle = "#FF000000";
                    pixelOutline = "#FF000000";
                    pixelHighlight = "#45FFFFFF";
                    pixelShadow = DeriveAlphaHex(secondaryAccent, 0xC0);
                    isPixel = true;
                    iconScalingMode = BitmapScalingMode.NearestNeighbor;
                    break;
            }
        }

        return new ResolvedAppearance(
            preset,
            surfaceBase,
            surfaceControl,
            textPrimary,
            textSecondary,
            accent,
            secondaryAccent,
            intensity,
            motionEnabled,
            controlRadius,
            surfaceRadius,
            strokeSubtle,
            isPixel,
            pixelOutline,
            pixelHighlight,
            pixelShadow,
            iconScalingMode);
    }

    public static void ApplyToResources(ResourceDictionary target, ResolvedAppearance resolved)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resolved);

        StopAppearanceAnimation(target);

        var baseBrush = NewBrush(resolved.SurfaceBase);
        target["SurfaceBase"] = baseBrush;
        target["BarBackground"] = baseBrush;

        var controlBrush = NewBrush(resolved.SurfaceControl);
        target["SurfaceControl"] = controlBrush;
        target["TileBackground"] = controlBrush;
        target["ControlSurface"] = controlBrush;

        target["SurfaceControlHover"] = DeriveTint(controlBrush, 12);
        target["SurfaceControlPressed"] = DeriveTint(controlBrush, 24);
        target["ControlHover"] = target["SurfaceControlHover"];
        target["ControlPressed"] = target["SurfaceControlPressed"];
        target["SurfaceContainer"] = DeriveTint(controlBrush, -6);
        target["ControlWell"] = target["SurfaceContainer"];

        var primaryAccent = NewBrush(resolved.AccentPrimary);
        target["AccentPrimary"] = primaryAccent;
        target["AccentBrush"] = primaryAccent;

        var secondaryAccent = NewBrush(resolved.AccentSecondary);
        target["AccentSecondary"] = secondaryAccent;

        target["SliderThumbAccentBrush"] = resolved.IsPixel || resolved.Preset is Cyber or Rgb
            ? secondaryAccent
            : primaryAccent;

        target["ToggleLedActiveBrush"] = secondaryAccent;

        byte subtleAlpha = (byte)Math.Clamp((int)(0x24 * resolved.Intensity), 0x08, 0x60);
        byte glowAlpha = (byte)Math.Clamp((int)(0x40 * resolved.Intensity), 0x10, 0x80);
        target["AccentSubtle"] = DeriveAlpha(primaryAccent, subtleAlpha);
        target["AccentGlow"] = DeriveAlpha(primaryAccent, glowAlpha);

        var primaryText = NewBrush(resolved.TextPrimary);
        target["TextPrimary"] = primaryText;
        target["PrimaryText"] = primaryText;

        var secondaryText = NewBrush(resolved.TextSecondary);
        target["TextSecondary"] = secondaryText;
        target["SecondaryText"] = secondaryText;
        target["TextMuted"] = secondaryText;

        target["SensorIconForeground"] = resolved.IsPixel || resolved.Preset is Cyber or Rgb
            ? secondaryAccent
            : secondaryText;

        target["ControlCornerRadius"] = new CornerRadius(resolved.ControlCornerRadius);
        target["SurfaceCornerRadius"] = new CornerRadius(resolved.SurfaceCornerRadius);

        if (!string.IsNullOrWhiteSpace(resolved.StrokeSubtle))
            target["StrokeSubtle"] = NewBrush(resolved.StrokeSubtle);

        Brush activeAccent = primaryAccent;
        Brush controlBorder = (Brush)target["StrokeSubtle"];
        Brush shellBorder = controlBorder;
        Brush mediaFrame = controlBorder;

        if (resolved.Preset == Oled)
        {
            var oledText = NewBrush(resolved.TextPrimary);
            controlBorder = DeriveAlpha(oledText, (byte)(0x0C + (0x10 * resolved.Intensity)));
            shellBorder = DeriveAlpha(oledText, (byte)(0x06 + (0x0A * resolved.Intensity)));
            mediaFrame = controlBorder;
        }
        else if (resolved.Preset == Cyber)
        {
            byte edgeAlpha = (byte)(0x18 + (0x48 * resolved.Intensity));
            activeAccent = CreateAccentGradient(primaryAccent, secondaryAccent, 0xFF);
            controlBorder = DeriveAlpha(secondaryAccent, (byte)Math.Max(0x12, edgeAlpha / 2));
            shellBorder = CreateAccentGradient(primaryAccent, secondaryAccent, edgeAlpha);
            mediaFrame = CreateAccentGradient(primaryAccent, secondaryAccent, (byte)Math.Min(0x90, edgeAlpha + 0x18));
        }
        else if (resolved.Preset == Rgb && resolved.Intensity > 0)
        {
            byte edgeAlpha = (byte)(0x20 + (0x60 * resolved.Intensity));
            byte effectAlpha = (byte)Math.Clamp((int)(0xFF * resolved.Intensity), 1, 0xFF);
            activeAccent = CreateRgbAccentGradient(primaryAccent, secondaryAccent, effectAlpha, resolved.MotionEnabled);
            controlBorder = DeriveAlpha(secondaryAccent, (byte)Math.Max(0x10, edgeAlpha / 2));
            shellBorder = activeAccent;
            mediaFrame = activeAccent;
        }

        target["ActiveAccentBrush"] = activeAccent;
        target["ControlBorderBrush"] = controlBorder;
        target["ShellBorderBrush"] = shellBorder;
        target["MediaFrameBrush"] = mediaFrame;
        target["ShellCornerRadius"] = new CornerRadius(resolved.IsPixel ? 0 : 14);
        target["AppearanceMotionActive"] = resolved.Preset == Rgb && resolved.MotionEnabled && resolved.Intensity > 0;

        // Typography tokens
        var barFont = resolved.IsPixel
            ? CreatePackagedFont("VT323")
            : new FontFamily("Segoe UI Variable, Segoe UI");
        var commandFont = barFont;
        var iconFont = resolved.IsPixel
            ? CreatePackagedFont("pixelart-icons-font")
            : new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");

        target["BarFontFamily"] = barFont;
        target["CommandFontFamily"] = commandFont;
        target["IconFontFamily"] = iconFont;
        target["BarFontWeight"] = resolved.IsPixel ? FontWeights.Normal : FontWeights.SemiBold;
        target["CommandFontWeight"] = resolved.IsPixel ? FontWeights.Normal : FontWeights.SemiBold;

        // Pixel-specific semantic tokens
        target["IsPixelPreset"] = resolved.IsPixel;
        target["PixelOutlineBrush"] = NewBrush(resolved.PixelOutline);
        target["PixelHighlightBrush"] = NewBrush(resolved.PixelHighlight);
        target["PixelShadowBrush"] = resolved.IsPixel ? DeriveAlpha(secondaryAccent, 0xC0) : Brushes.Transparent;
        target["PixelButtonFaceMargin"] = new Thickness(resolved.IsPixel ? 1 : 0);
        target["IconBitmapScalingMode"] = resolved.IconScalingMode;
        target["SliderThumbCornerRadius"] = new CornerRadius(resolved.IsPixel ? 0 : 8);
        target["ButtonPressMargin"] = resolved.IsPixel ? new Thickness(0, 1.5, 0, 0) : new Thickness(0);
        target["ButtonPressScale"] = resolved.IsPixel ? 1.0 : 0.97;
        target["SliderTrackHeight"] = resolved.IsPixel ? 8.0 : 6.0;
        target["SliderTrackCornerRadius"] = new CornerRadius(resolved.IsPixel ? 0 : 3);
        target["SliderTrackBorderBrush"] = resolved.IsPixel ? NewBrush(resolved.PixelOutline) : target["StrokeSubtle"];
        target["ProgressBarHeight"] = resolved.IsPixel ? 8.0 : 3.0;
        target["ProgressBarCornerRadius"] = new CornerRadius(resolved.IsPixel ? 0 : 1.5);

        if (resolved.IsPixel)
        {
            var blockFill = primaryAccent;
            var blockBase = controlBrush;
            var gapBrush = baseBrush;

            var bgSegmented = CreateSegmentedBrush(blockBase, gapBrush, blockSize: 6, gapSize: 2, height: 8);
            target["SliderTrackBackgroundBrush"] = bgSegmented;
            target["SliderTrackForegroundBrush"] = CreateSegmentedBrush(blockFill, gapBrush, blockSize: 6, gapSize: 2, height: 8);
            target["ProgressBarTrackBrush"] = bgSegmented;
            target["ToggleActiveBackground"] = primaryAccent;
            target["ToggleActiveForeground"] = baseBrush;
            target["ToggleLedVisibility"] = Visibility.Visible;
        }
        else
        {
            target["SliderTrackBackgroundBrush"] = target["SurfaceContainer"];
            target["SliderTrackForegroundBrush"] = primaryAccent;
            target["ProgressBarTrackBrush"] = target["StrokeSubtle"];
            target["ToggleActiveBackground"] = target["AccentSubtle"];
            target["ToggleActiveForeground"] = primaryText;
            target["ToggleLedVisibility"] = Visibility.Collapsed;
        }
    }

    private static Brush CreateAccentGradient(Brush first, Brush second, byte alpha)
    {
        if (first is not SolidColorBrush a || second is not SolidColorBrush b)
            return first;

        var brush = new LinearGradientBrush(
            Color.FromArgb(alpha, a.Color.R, a.Color.G, a.Color.B),
            Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    private static FontFamily CreatePackagedFont(string familyName)
    {
        if (System.Windows.Application.Current is null)
            return new FontFamily($"/AdaptiveBarPC;component/Resources/Fonts/#{familyName}");

        var baseUri = new Uri("pack://application:,,,/AdaptiveBarPC;component/Resources/Fonts/", UriKind.Absolute);
        return new FontFamily(baseUri, $"./#{familyName}");
    }

    private static Brush CreateRgbAccentGradient(Brush first, Brush second, byte alpha, bool animate)
    {
        if (first is not SolidColorBrush a || second is not SolidColorBrush b)
            return first;

        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, a.Color.R, a.Color.G, a.Color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, a.Color.R, a.Color.G, a.Color.B), 1));

        if (animate)
        {
            var rotate = new RotateTransform(0, 0.5, 0.5);
            rotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(8))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
            brush.RelativeTransform = rotate;
        }
        else if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private static void StopAppearanceAnimation(ResourceDictionary target)
    {
        if (target["ActiveAccentBrush"] is LinearGradientBrush { RelativeTransform: RotateTransform rotate })
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    public static DrawingBrush CreateSegmentedBrush(Brush blockBrush, Brush gapBrush, double blockSize = 6, double gapSize = 2, double height = 8)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(blockBrush, null, new RectangleGeometry(new Rect(0, 0, blockSize, height))));
        group.Children.Add(new GeometryDrawing(gapBrush, null, new RectangleGeometry(new Rect(blockSize, 0, gapSize, height))));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, blockSize + gapSize, height),
            ViewportUnits = BrushMappingMode.Absolute,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Center
        };
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    public static Brush NewBrush(string hex, Brush? fallback = null)
    {
        try
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }
        catch
        {
            return fallback ?? Brushes.Transparent;
        }
    }

    public static Brush DeriveAlpha(Brush baseBrush, byte alpha)
    {
        if (baseBrush is SolidColorBrush scb)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, scb.Color.R, scb.Color.G, scb.Color.B));
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }
        return baseBrush;
    }

    public static Brush DeriveTint(Brush baseBrush, int delta)
    {
        if (baseBrush is SolidColorBrush scb)
        {
            var r = (byte)Math.Clamp(scb.Color.R + delta, 0, 255);
            var g = (byte)Math.Clamp(scb.Color.G + delta, 0, 255);
            var b = (byte)Math.Clamp(scb.Color.B + delta, 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(scb.Color.A, r, g, b));
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }
        return baseBrush;
    }
}
