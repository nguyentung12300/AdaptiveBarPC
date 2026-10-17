using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.ViewModels;

public sealed class DecorativeMediaItemViewModel : BarItemViewModel
{
    public string? Source { get; init; }
    public string? ResolvedSource { get; init; }
    public string Fit { get; init; } = "contain";
    public bool Loop { get; init; } = true;
    public double Opacity { get; init; } = 1d;
    public bool Enabled { get; init; } = true;
    public double HorizontalOffset { get; init; }
    public double VerticalOffset { get; init; }
    public double Zoom { get; init; } = 1d;
    public bool ClipToBounds { get; init; } = true;
    public bool ShowDiagnostics { get; init; }

    public static DecorativeMediaItemViewModel Create(
        ControlDefinition widget,
        AssetPathResolver? paths,
        bool showDiagnostics = false,
        bool clipToBounds = true) => new()
    {
        Id = widget.Id,
        Width = widget.Width > 0 ? widget.Width : 120,
        Source = widget.Source,
        ResolvedSource = paths?.ResolveExistingFile(widget.Source),
        Fit = widget.Fit ?? "contain",
        Loop = widget.Loop ?? true,
        Opacity = widget.Opacity is { } opacity && double.IsFinite(opacity)
            ? Math.Clamp(opacity, 0d, 1d)
            : 1d,
        Enabled = widget.Enabled ?? true,
        HorizontalOffset = widget.OffsetX is { } offsetX && double.IsFinite(offsetX)
            ? Math.Clamp(offsetX, -4096d, 4096d)
            : 0d,
        VerticalOffset = widget.OffsetY is { } offsetY && double.IsFinite(offsetY)
            ? Math.Clamp(offsetY, -4096d, 4096d)
            : 0d,
        Zoom = widget.Zoom is { } zoom && double.IsFinite(zoom)
            ? Math.Clamp(zoom, 0.1d, 4d)
            : 1d,
        ClipToBounds = clipToBounds,
        ShowDiagnostics = showDiagnostics
    };
}
