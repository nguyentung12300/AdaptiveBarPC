using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AdaptiveBarPC.Controls;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Designer;

public enum DirtyDecision
{
    Save,
    Discard,
    Cancel
}

public sealed class ProfileEntry : INotifyPropertyChanged
{
    private string _displayName;

    public ProfileEntry(string filePath, string id, string displayName)
    {
        FilePath = filePath;
        Id = id;
        _displayName = displayName;
    }

    public string FilePath { get; }
    public string Id { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string? Category { get; init; }
    public bool IsDefault { get; init; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;
            _displayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DesignerPage : INotifyPropertyChanged
{
    public DesignerPage(PageDefinition? definition, string id, string displayName, List<ControlDefinition> items)
    {
        Definition = definition;
        Id = id;
        _displayName = displayName;
        Items = items;
    }

    private string _displayName;
    public PageDefinition? Definition { get; }
    public string Id { get; }
    public List<ControlDefinition> Items { get; }
    public bool IsLegacyMain => Definition is null;

    public string DisplayName
    {
        get => Definition?.DisplayName ?? _displayName;
        set
        {
            if (Definition is not null)
                Definition.DisplayName = value;
            else
                _displayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class PreviewItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private object _content = null!;

    public required string WidgetId { get; init; }
    public required bool CanResize { get; init; }
    public required object Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;
            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record DesignerPlaceholderPreview(double Width, string Text);

public sealed class DesignerPreviewAdapter
{
    private static readonly ICommand NoOpCommand = new DelegateCommand(() => { });
    private readonly AssetPathResolver? _assetPaths;

    public DesignerPreviewAdapter(string? profileDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(profileDirectory))
            _assetPaths = AssetPathResolver.ForProfilesDirectory(profileDirectory);
    }

    public PreviewItemViewModel Create(ControlDefinition widget, bool selected, bool decorativeMediaEditing = false)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return new PreviewItemViewModel
        {
            WidgetId = widget.Id,
            IsSelected = selected,
            CanResize = WidgetDefinitionRegistry.TryGet(widget.Type, out var definition) &&
                        definition.IsPlaceable &&
                        definition.MaxWidth > definition.MinWidth,
            Content = CreateContent(widget, decorativeMediaEditing)
        };
    }

    public object CreateContent(ControlDefinition widget, bool decorativeMediaEditing = false) =>
        widget.Type.Trim().ToLowerInvariant() switch
        {
            WidgetTypes.Button or WidgetTypes.Emoji or WidgetTypes.Clipboard or WidgetTypes.TextSlot => new ButtonItemViewModel
            {
                Id = widget.Id,
                Width = EffectiveWidth(widget, 84),
                Label = string.IsNullOrWhiteSpace(widget.Label) ? "Button" : widget.Label,
                Icon = widget.Icon?.Value,
                IconKind = widget.Icon?.Kind,
                AssetIcon = LoadAssetIcon(widget.Icon),
                Command = NoOpCommand,
                SecondaryCommand = null,
                LongPressMilliseconds = widget.LongPressMs ?? 650
            },
            WidgetTypes.Sticker => new ButtonItemViewModel
            {
                Id = widget.Id,
                Width = EffectiveWidth(widget, 72),
                Label = string.IsNullOrWhiteSpace(widget.Label) ? "Sticker" : widget.Label,
                AssetIcon = LoadSticker(widget.Source),
                Command = NoOpCommand
            },
            WidgetTypes.Toggle => new ToggleItemViewModel
            {
                Id = widget.Id,
                Width = EffectiveWidth(widget, 96),
                Label = string.IsNullOrWhiteSpace(widget.Label) ? "Toggle" : widget.Label,
                Icon = widget.Icon?.Value,
                IconKind = widget.Icon?.Kind,
                AssetIcon = LoadAssetIcon(widget.Icon),
                IsChecked = widget.IsChecked,
                OnLabel = widget.OnLabel,
                OffLabel = widget.OffLabel,
                ToggleCommand = NoOpCommand,
                SecondaryCommand = null,
                LongPressMilliseconds = widget.LongPressMs ?? 650
            },
            WidgetTypes.MultiState => CreateMultiState(widget),
            WidgetTypes.Slider => new SliderItemViewModel(
                string.IsNullOrWhiteSpace(widget.Label) ? "Slider" : widget.Label,
                EffectiveWidth(widget, 180),
                widget.Min,
                widget.Max <= widget.Min ? 100 : widget.Max,
                Math.Clamp(68d, widget.Min, widget.Max <= widget.Min ? 100d : widget.Max),
                setter: null)
            {
                Id = widget.Id,
                Icon = widget.Icon?.Value,
                IconKind = widget.Icon?.Kind,
                AssetIcon = LoadAssetIcon(widget.Icon)
            },
            WidgetTypes.ExpandableSlider => CreateExpandableSlider(widget),
            WidgetTypes.Sensor => CreateSensor(widget),
            WidgetTypes.Spacer => new SpacerItemViewModel
            {
                Id = widget.Id,
                Width = EffectiveWidth(widget, 6)
            },
            WidgetTypes.Media => MediaItemViewModel.CreatePreview(
                widget.Width > 0 ? widget.Width : 360),
            WidgetTypes.DecorativeMedia => CreateDecorativeMedia(widget, !decorativeMediaEditing),
            WidgetTypes.AudioSession => new AudioSessionItemViewModel(
                string.IsNullOrWhiteSpace(widget.Label) ? "App Audio" : widget.Label,
                EffectiveWidth(widget, 180),
                widget.Min,
                widget.Max <= widget.Min ? 100 : widget.Max,
                Math.Clamp(72d, widget.Min, widget.Max <= widget.Min ? 100d : widget.Max),
                isMuted: false,
                valueSetter: null,
                muteSetter: null)
            {
                Id = widget.Id
            },
            WidgetTypes.DynamicList => CreateDynamic(widget),
            _ => new DesignerPlaceholderPreview(
                widget.Width > 0 ? widget.Width : 120,
                $"Unknown: {widget.Type}")
        };

    private DecorativeMediaItemViewModel CreateDecorativeMedia(ControlDefinition widget, bool clipToBounds) =>
        DecorativeMediaItemViewModel.Create(widget, _assetPaths, showDiagnostics: true, clipToBounds: clipToBounds);

    private ImageSource? LoadSticker(string? source)
    {
        var path = _assetPaths?.ResolveExistingFile(source);
        return path is null ? null : StickerImageLoader.Load(path, 32);
    }

    internal string? ResolveMediaAsset(string? source) => _assetPaths?.ResolveExistingFile(source);

    private static ExpandableSliderItemViewModel CreateExpandableSlider(ControlDefinition widget)
    {
        var minimum = widget.Min;
        var maximum = widget.Max > minimum ? widget.Max : minimum + 100;
        var icon = widget.Icon?.Kind.Equals("glyph", StringComparison.OrdinalIgnoreCase) == true
            ? widget.Icon.Value
            : null;
        return new ExpandableSliderItemViewModel(
            string.IsNullOrWhiteSpace(widget.Label) ? "Expandable slider" : widget.Label,
            ExpandableTouchSlider.CollapsedWidthDip,
            minimum,
            maximum,
            Math.Clamp(widget.Value ?? 50d, minimum, maximum),
            setter: null,
            icon,
            widget.Enabled ?? true)
        {
            Id = widget.Id
        };
    }

    private static MultiStateItemViewModel CreateMultiState(ControlDefinition widget)
    {
        var states = widget.States.Count > 0
            ? widget.States
            : [new WidgetStateDefinition { Id = "preview", Label = string.IsNullOrWhiteSpace(widget.Label) ? "State" : widget.Label }];
        var index = Math.Max(0, states.FindIndex(s => string.Equals(s.Id, widget.State, StringComparison.OrdinalIgnoreCase)));
        return new MultiStateItemViewModel
        {
            Id = widget.Id,
            Width = EffectiveWidth(widget, 110),
            States = states,
            CurrentIndex = index,
            CycleCommand = NoOpCommand
        };
    }

    private SensorItemViewModel CreateSensor(ControlDefinition widget)
    {
        var metric = widget.Metric?.Trim().ToLowerInvariant() ?? "";
        var (value, unit) = metric switch
        {
            "gpu_load" => (61d, "%"),
            "gpu_temp" => (58d, "°C"),
            "memory_load" or "ram_load" => (73d, "%"),
            _ => (42d, widget.Unit ?? "%")
        };

        return new SensorItemViewModel
        {
            Id = widget.Id,
            Width = EffectiveWidth(widget, 96),
            Label = string.IsNullOrWhiteSpace(widget.Label) ? FriendlyMetric(metric) : widget.Label,
            Icon = widget.Icon?.Value,
            IconKind = widget.Icon?.Kind,
            AssetIcon = LoadAssetIcon(widget.Icon),
            Metric = widget.Metric ?? "",
            Value = value,
            Unit = widget.Unit ?? unit,
            Format = string.IsNullOrWhiteSpace(widget.Format) ? "{0:F0}" : widget.Format,
            Status = "normal"
        };
    }

    private ImageSource? LoadAssetIcon(IconDefinition? icon) =>
        _assetPaths is not null && string.Equals(icon?.Kind, "asset", StringComparison.OrdinalIgnoreCase)
            ? AssetImageLoader.LoadIcon(_assetPaths, icon?.Value)
            : null;

    private static object CreateDynamic(ControlDefinition widget)
    {
        var provider = widget.Provider?.Trim() ?? "";
        if (provider.Equals("hardware", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicListItemViewModel.CreatePreview(
                provider,
                [
                    new SensorItemViewModel { Id = "preview-cpu", Width = 82, Label = "CPU", Metric = "cpu_load", Value = 42, Unit = "%", Format = "{0:F0}" },
                    new SensorItemViewModel { Id = "preview-gpu", Width = 82, Label = "GPU", Metric = "gpu_load", Value = 61, Unit = "%", Format = "{0:F0}" },
                    new SensorItemViewModel { Id = "preview-temp", Width = 82, Label = "GPU", Metric = "gpu_temp", Value = 58, Unit = "°C", Format = "{0:F0}" },
                    new SensorItemViewModel { Id = "preview-ram", Width = 82, Label = "RAM", Metric = "memory_load", Value = 73, Unit = "%", Format = "{0:F0}" }
                ],
                widget.Width > 0 ? widget.Width : double.NaN);
        }

        if (provider.Equals("audioSessions", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicListItemViewModel.CreatePreview(
                provider,
                [
                    new AudioSessionItemViewModel("Brave", 180, 0, 100, 72, false, null, null) { Id = "preview-brave" },
                    new AudioSessionItemViewModel("VLC", 180, 0, 100, 48, false, null, null) { Id = "preview-vlc" }
                ],
                widget.Width > 0 ? widget.Width : double.NaN);
        }

        return new DesignerPlaceholderPreview(
            widget.Width > 0 ? widget.Width : 150,
            $"Dynamic: {(string.IsNullOrWhiteSpace(provider) ? "provider" : provider)}");
    }

    private static double EffectiveWidth(ControlDefinition widget, double fallback) =>
        widget.Width > 0 && double.IsFinite(widget.Width) ? widget.Width : fallback;

    private static string FriendlyMetric(string metric) => metric switch
    {
        "gpu_load" => "GPU",
        "gpu_temp" => "GPU",
        "memory_load" or "ram_load" => "RAM",
        _ => "CPU"
    };
}

public sealed class WidgetEditorViewModel : INotifyPropertyChanged
{
    private readonly DesignerViewModel _owner;
    private string _widthText;
    private string? _widthInputError;

    public WidgetEditorViewModel(DesignerViewModel owner, ControlDefinition model)
    {
        _owner = owner;
        Model = model;
        _widthText = model.Width.ToString("0.##", CultureInfo.CurrentCulture);
        WidgetDefinitionRegistry.TryGet(model.Type, out var definition);
        Definition = definition;
        PlacementOptions = model.Type is WidgetTypes.TextSlot or WidgetTypes.Sticker
            ? new[] { "Current Page", "Fixed Left", "Fixed Right", "Fixed Left (All Profiles)", "Fixed Right (All Profiles)", "Input Tools (All Profiles)" }
            : new[] { "Current Page", "Fixed Left", "Fixed Right", "Fixed Left (All Profiles)", "Fixed Right (All Profiles)" };
    }

    public ControlDefinition Model { get; }
    public WidgetDefinition? Definition { get; }
    public string Id => Model.Id;
    public string Type => Model.Type;
    public string Description => Type switch
    {
        WidgetTypes.Spacer => "Empty space to separate items",
        WidgetTypes.Media => "Playback controls and current track",
        WidgetTypes.DecorativeMedia => "An image, animation or video on the bar",
        WidgetTypes.DynamicList => "Live items from the selected provider",
        WidgetTypes.Slider or WidgetTypes.ExpandableSlider => "Adjust a value with a touch slider",
        WidgetTypes.Sensor => "Display a live sensor value",
        WidgetTypes.TextSlot => "Insert reusable text",
        WidgetTypes.Sticker => "Send an image from the bar",
        WidgetTypes.Toggle => "Switch between two states",
        WidgetTypes.MultiState => "Cycle through configured states",
        _ => "A configurable action on your AdaptiveBar"
    };
    public string PlacementDescription => Placement == "Current Page"
        ? "Widget placed on the currently active page."
        : $"Widget stays in {Placement.ToLowerInvariant()} when switching pages.";
    public IReadOnlyList<string> PlacementOptions { get; }
    public string Placement
    {
        get => _owner.GetWidgetPlacement(Model);
        set
        {
            if (_owner.MoveWidgetToPlacement(Model, value))
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlacementDescription));
            }
        }
    }
    public string WidthHint => Definition is null
        ? ""
        : Definition.AllowsFlexibleWidth
            ? $"Auto (0) or {Definition.MinWidth:0}–{Definition.MaxWidth:0} DIP"
            : $"{Definition.MinWidth:0}–{Definition.MaxWidth:0} DIP";

    public bool HasLabel => Model.Type != WidgetTypes.Spacer && Model.Type != WidgetTypes.Media && Model.Type != WidgetTypes.DynamicList && Model.Type != WidgetTypes.DecorativeMedia;
    public bool HasIcon => Model.Type is WidgetTypes.Button or WidgetTypes.Emoji or WidgetTypes.Clipboard or WidgetTypes.TextSlot or WidgetTypes.Toggle or WidgetTypes.MultiState or WidgetTypes.ExpandableSlider or WidgetTypes.Slider or WidgetTypes.Sensor;
    public bool HasProvider => Definition?.RequiresProvider == true;
    public bool HasAction => Model.Type == WidgetTypes.Button;
    public bool HasInputContent => Model.Type == WidgetTypes.TextSlot;
    public bool HasStickerSettings => Model.Type == WidgetTypes.Sticker;
    public bool HasExpandableSliderSettings => Model.Type == WidgetTypes.ExpandableSlider;
    public bool HasDecorativeMediaSettings => Model.Type == WidgetTypes.DecorativeMedia;
    public bool HasEditableWidth => Model.Type != WidgetTypes.ExpandableSlider;

    public string Label
    {
        get => Model.Label;
        set
        {
            if (Model.Label == value) return;
            Model.Label = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public double SliderValue
    {
        get => Model.Value ?? 50d;
        set
        {
            if (Model.Value == value) return;
            Model.Value = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public double SliderMinimum
    {
        get => Model.Min;
        set
        {
            if (Model.Min == value) return;
            Model.Min = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public double SliderMaximum
    {
        get => Model.Max;
        set
        {
            if (Model.Max == value) return;
            Model.Max = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public bool SliderEnabled
    {
        get => Model.Enabled ?? true;
        set
        {
            if (Model.Enabled == value) return;
            Model.Enabled = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public string MediaSource
    {
        get => Model.Source ?? "";
        set
        {
            if (Model.Source == value) return;
            Model.Source = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public string MediaFit
    {
        get => Model.Fit ?? "contain";
        set
        {
            var fit = value?.Trim().ToLowerInvariant() switch
            {
                "cover" => "cover",
                "fill" => "fill",
                _ => "contain"
            };
            if (Model.Fit == fit) return;
            Model.Fit = fit;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public bool MediaLoop
    {
        get => Model.Loop ?? true;
        set
        {
            if (Model.Loop == value) return;
            Model.Loop = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public double MediaOpacity
    {
        get => Model.Opacity is { } value && double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 1d;
        set
        {
            var opacity = double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 1d;
            if (Model.Opacity == opacity) return;
            Model.Opacity = opacity;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public bool MediaEnabled
    {
        get => Model.Enabled ?? true;
        set
        {
            if (Model.Enabled == value) return;
            Model.Enabled = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public double MediaOffsetX
    {
        get => Model.OffsetX is { } value && double.IsFinite(value) ? Math.Clamp(value, -4096d, 4096d) : 0d;
        set
        {
            var offset = double.IsFinite(value) ? Math.Clamp(value, -4096d, 4096d) : 0d;
            if (Model.OffsetX == offset) return;
            Model.OffsetX = offset;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public double MediaOffsetY
    {
        get => Model.OffsetY is { } value && double.IsFinite(value) ? Math.Clamp(value, -4096d, 4096d) : 0d;
        set
        {
            var offset = double.IsFinite(value) ? Math.Clamp(value, -4096d, 4096d) : 0d;
            if (Model.OffsetY == offset) return;
            Model.OffsetY = offset;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public double MediaZoom
    {
        get => Model.Zoom is { } value && double.IsFinite(value) ? Math.Clamp(value, 0.1d, 4d) : 1d;
        set
        {
            var zoom = double.IsFinite(value) ? Math.Clamp(value, 0.1d, 4d) : 1d;
            if (Model.Zoom == zoom) return;
            Model.Zoom = zoom;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public ICommand ResetMediaZoomCommand => new EditorCommand(_ => MediaZoom = 1d);
    public ICommand ResetMediaHorizontalCommand => new EditorCommand(_ => MediaOffsetX = 0d);
    public ICommand ResetMediaVerticalCommand => new EditorCommand(_ => MediaOffsetY = 0d);

    public string WidthText
    {
        get => _widthText;
        set
        {
            if (_widthText == value) return;
            _widthText = value;
            OnPropertyChanged();
            NotifyWidthState();

            if (TryParseWidth(value, out var width))
            {
                _widthInputError = null;
                if (Model.Width != width)
                {
                    Model.Width = width;
                    _owner.OnWidgetEdited(Model);
                    return;
                }
            }
            else
            {
                _widthInputError = "Width must be a finite number.";
            }

            _owner.ValidateCurrent();
        }
    }

    internal void RefreshWidthFromModel()
    {
        _widthInputError = null;
        var value = Model.Width.ToString("0.##", CultureInfo.CurrentCulture);
        if (_widthText == value) return;
        _widthText = value;
        OnPropertyChanged(nameof(WidthText));
        NotifyWidthState();
    }

    public string WidthError => !TryParseWidth(WidthText, out var width)
        ? "Enter a finite number."
        : Definition is { } d && !(d.AllowsFlexibleWidth && width == 0) && (width < d.MinWidth || width > d.MaxWidth)
            ? $"Use {WidthHint}." : "";
    public bool CanIncreaseWidth => HasEditableWidth && Definition is { } d &&
        TryParseWidth(WidthText, out var width) && width < d.MaxWidth;
    public bool CanDecreaseWidth => HasEditableWidth && Definition is { } d &&
        TryParseWidth(WidthText, out var width) && width > (d.AllowsFlexibleWidth ? 0 : d.MinWidth);

    public void StepWidth(int direction)
    {
        if (!HasEditableWidth || Definition is not { } d || !TryParseWidth(WidthText, out var width)) return;
        var next = direction > 0 ? Math.Clamp(width + 1, d.MinWidth, d.MaxWidth)
            : d.AllowsFlexibleWidth && width <= d.MinWidth ? 0 : Math.Clamp(width - 1, d.MinWidth, d.MaxWidth);
        WidthText = next.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void NotifyWidthState()
    {
        OnPropertyChanged(nameof(WidthError));
        OnPropertyChanged(nameof(CanIncreaseWidth));
        OnPropertyChanged(nameof(CanDecreaseWidth));
    }

    private bool _isIconPickerOpen;
    private string _iconSearchText = "";

    public string IconKind
    {
        get => Model.Icon?.Kind ?? "none";
        set
        {
            var icon = Model.Icon ??= new IconDefinition();
            if (icon.Kind == value) return;
            icon.Kind = value;
            if (!string.Equals(value, "glyph", StringComparison.OrdinalIgnoreCase) && _isIconPickerOpen)
            {
                _isIconPickerOpen = false;
                OnPropertyChanged(nameof(IsIconPickerOpen));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGlyphIcon));
            OnPropertyChanged(nameof(IsAssetIcon));
            OnPropertyChanged(nameof(SelectedCatalogItem));
            OnPropertyChanged(nameof(SelectedIconDisplayName));
            OnPropertyChanged(nameof(SelectedIconGlyph));
            OnPropertyChanged(nameof(HasSelectedIcon));
            OnPropertyChanged(nameof(FilteredIconGroups));
            _owner.OnWidgetEdited(Model);
        }
    }

    public string IconValue
    {
        get => Model.Icon?.Value ?? "";
        set
        {
            var icon = Model.Icon ??= new IconDefinition();
            if (icon.Value == value) return;
            icon.Value = value;
            if (!string.IsNullOrWhiteSpace(value) && string.Equals(icon.Kind, "none", StringComparison.OrdinalIgnoreCase))
            {
                icon.Kind = "glyph";
                OnPropertyChanged(nameof(IconKind));
                OnPropertyChanged(nameof(IsGlyphIcon));
                OnPropertyChanged(nameof(IsAssetIcon));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCatalogItem));
            OnPropertyChanged(nameof(SelectedIconDisplayName));
            OnPropertyChanged(nameof(SelectedIconGlyph));
            OnPropertyChanged(nameof(HasSelectedIcon));
            OnPropertyChanged(nameof(FilteredIconGroups));
            _owner.OnWidgetEdited(Model);
        }
    }

    public IconCatalogItem? SelectedCatalogItem =>
        string.Equals(IconKind, "glyph", StringComparison.OrdinalIgnoreCase)
            ? IconCatalog.FindByGlyph(IconValue)
            : null;

    public bool IsGlyphIcon => string.Equals(IconKind, "glyph", StringComparison.OrdinalIgnoreCase);
    public bool IsAssetIcon => string.Equals(IconKind, "asset", StringComparison.OrdinalIgnoreCase);

    public string SelectedIconDisplayName
    {
        get
        {
            if (string.Equals(IconKind, "asset", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(IconValue) ? "No asset path" : IconValue;

            if (!string.Equals(IconKind, "glyph", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(IconValue))
            {
                return "None (Use Label)";
            }

            var item = SelectedCatalogItem;
            if (item is not null)
                return item.Name;

            var glyph = GlyphHelper.Resolve(IconValue);
            return !string.IsNullOrWhiteSpace(glyph) ? "Custom Glyph" : "None (Use Label)";
        }
    }

    public string SelectedIconGlyph =>
        string.Equals(IconKind, "glyph", StringComparison.OrdinalIgnoreCase)
            ? GlyphHelper.Resolve(IconValue)
            : "";

    public bool HasSelectedIcon =>
        string.Equals(IconKind, "asset", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(IconValue)
            : !string.IsNullOrWhiteSpace(SelectedIconGlyph);

    public bool IsIconPickerOpen
    {
        get => _isIconPickerOpen;
        set
        {
            if (_isIconPickerOpen == value) return;
            _isIconPickerOpen = value;
            OnPropertyChanged();
        }
    }

    public string IconSearchText
    {
        get => _iconSearchText;
        set
        {
            if (_iconSearchText == value) return;
            _iconSearchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredIconGroups));
        }
    }

    public IReadOnlyList<IconCategoryGroup> FilteredIconGroups =>
        IconCatalog.GroupByCategory(
            IconCatalog.Search(_iconSearchText),
            string.Equals(IconKind, "glyph", StringComparison.OrdinalIgnoreCase) ? IconValue : null);

    public ICommand ToggleIconPickerCommand => new EditorCommand(_ =>
    {
        if (!_isIconPickerOpen)
        {
            _iconSearchText = "";
            OnPropertyChanged(nameof(IconSearchText));
            OnPropertyChanged(nameof(FilteredIconGroups));
        }
        IsIconPickerOpen = !_isIconPickerOpen;
    });

    public ICommand SelectCatalogIconCommand => new EditorCommand(param =>
    {
        if (param is IconPickerTile tile)
        {
            SelectCatalogIcon(tile.Item);
        }
        else if (param is IconCatalogItem item)
        {
            SelectCatalogIcon(item);
        }
        else if (param is null)
        {
            ClearIcon();
        }
    });

    public ICommand ClearIconCommand => new EditorCommand(_ => ClearIcon());

    public void SelectCatalogIcon(IconCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _owner.BeginHistoryGesture();
        try
        {
            IconKind = "glyph";
            IconValue = item.GlyphValue;
            IsIconPickerOpen = false;
        }
        finally { _owner.EndHistoryGesture(); }
    }

    public void ClearIcon()
    {
        _owner.BeginHistoryGesture();
        try
        {
            IconKind = "none";
            IconValue = "";
            IsIconPickerOpen = false;
        }
        finally { _owner.EndHistoryGesture(); }
    }

    public string Provider
    {
        get => Model.Provider ?? "";
        set
        {
            if (Model.Provider == value) return;
            Model.Provider = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public string ActionType
    {
        get => Model.Action?.Type ?? "";
        set
        {
            var action = Model.Action ??= new ActionDefinition();
            if (action.Type == value) return;
            action.Type = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public string ActionValue
    {
        get => Model.Action?.Value ?? "";
        set
        {
            var action = Model.Action ??= new ActionDefinition();
            if (action.Value == value) return;
            action.Value = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    public string InputContent
    {
        get => Model.Action?.Value ?? "";
        set
        {
            const string actionType = "clipboard_text";
            var action = Model.Action ??= new ActionDefinition { Type = actionType };
            if (action.Value == value) return;
            action.Type = actionType;
            action.Value = value;
            OnPropertyChanged();
            _owner.OnWidgetEdited(Model);
        }
    }

    internal ProfileDiagnostic? GetEditorDiagnostic(string profileId, string? pageId) =>
        _widthInputError is null
            ? null
            : new ProfileDiagnostic(
                "DesignerInvalidWidthInput",
                ProfileDiagnosticSeverity.Error,
                _widthInputError,
                profileId,
                pageId,
                Model.Id);

    private static bool TryParseWidth(string text, out double value)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return double.IsFinite(value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class DesignerViewModel : INotifyPropertyChanged
{
    private readonly ProfileDocumentSerializer _serializer = new();
    private readonly DesignerPreviewAdapter _previewAdapter;
    private readonly string _appearanceFilePath;
    private readonly DispatcherTimer _appearanceSaveTimer;
    private bool _appearanceSavePending;
    private GlobalAppearanceSettings _globalAppearance = new();
    private ProfileEntry? _selectedProfile;
    private DesignerPage? _selectedPage;
    private ControlDefinition? _selectedWidget;
    private WidgetEditorViewModel? _selectedWidgetEditor;
    private ProfileDefinition? _document;
    private WidgetDefinition? _selectedPaletteDefinition;
    private bool _isDirty;
    private string _statusMessage = "Ready";
    private bool _isValidationExpanded;
    private int _selectedLeftTabIndex;
    private int _selectedRightTabIndex;
    private bool _isDecorativeMediaEditMode;
    private bool _isImportingMedia;

    public bool IsMediaImportAvailable => !_isImportingMedia;

    public DesignerViewModel(string profileDirectory, string? appearanceFilePath = null)
    {
        ProfileDirectory = profileDirectory;
        _appearanceFilePath = appearanceFilePath ?? GlobalAppearanceStore.DefaultPath;
        _appearanceSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _appearanceSaveTimer.Tick += (_, _) => FlushPendingAppearanceSave();
        _previewAdapter = new DesignerPreviewAdapter(profileDirectory);
        AppearanceEditor = new AppearanceEditorViewModel(this);
        ThemeLibrary = new ThemeLibraryViewModel(this, ResolveThemeLibraryDirectory(_appearanceFilePath));
        Palette = new ObservableCollection<WidgetDefinition>(
            WidgetDefinitionRegistry.All
                .Where(x => x.IsPlaceable && !x.Category.Equals("Input", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase));

        SaveCommand = new EditorCommand(_ => SaveCurrent(), _ => _document is not null);
        UndoCommand = new EditorCommand(_ => Undo(), _ => CanUndo);
        RedoCommand = new EditorCommand(_ => Redo(), _ => CanRedo);
        ThemeLibrary.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeLibrary.IsAvailable)) NotifyHistoryChanged();
        };
        ValidateCommand = new EditorCommand(_ => ValidateCurrent(), _ => _document is not null);
        AddWidgetCommand = new EditorCommand(_ => AddSelectedPaletteWidget(), _ => SelectedPage is not null && SelectedPaletteDefinition is not null);
        AddTextSlotCommand = new EditorCommand(_ => AddInputItem(WidgetTypes.TextSlot));
        AddStickerCommand = new EditorCommand(_ => AddInputItem(WidgetTypes.Sticker));
        DeleteSelectedWidgetCommand = new EditorCommand(_ => DeleteSelectedWidget(), _ => SelectedWidget is not null);
        MoveSelectedWidgetLeftCommand = new EditorCommand(_ => MoveSelectedWidget(-1), _ => CanMoveSelectedWidget(-1));
        MoveSelectedWidgetRightCommand = new EditorCommand(_ => MoveSelectedWidget(1), _ => CanMoveSelectedWidget(1));
        AddPageCommand = new EditorCommand(_ => AddPage(), _ => _document is not null);
        MovePageLeftCommand = new EditorCommand(_ => MoveSelectedPage(-1), _ => CanMoveSelectedPage(-1));
        MovePageRightCommand = new EditorCommand(_ => MoveSelectedPage(1), _ => CanMoveSelectedPage(1));
        ToggleValidationCommand = new EditorCommand(_ => IsValidationExpanded = !IsValidationExpanded);
        OpenAppearanceTabCommand = new EditorCommand(_ => SelectedRightTabIndex = 1);

        RefreshProfiles();
        if (Profiles.Count > 0)
            LoadProfile(Profiles[0]);

        if (File.Exists(_appearanceFilePath))
        {
            _globalAppearance = GlobalAppearanceStore.Load(_appearanceFilePath);
        }
        else
        {
            _globalAppearance = GlobalAppearanceStore.FromProfile(_document);
            TrySaveGlobalAppearance();
        }

        AppearanceEditor.LoadFromSettings(_globalAppearance);
        RefreshPreview();
        NotifyAppearanceChanged();
        ResetHistory();
    }

    private static string ResolveThemeLibraryDirectory(string appearanceFilePath)
    {
        var fullAppearancePath = Path.GetFullPath(appearanceFilePath);
        var currentDirectory = Path.Combine(Path.GetDirectoryName(fullAppearancePath)!, "Themes");
        if (!string.Equals(fullAppearancePath, GlobalAppearanceStore.DefaultPath, StringComparison.OrdinalIgnoreCase))
            return currentDirectory;

        // Keep existing user theme libraries visible after the product data folder was renamed.
        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlexBarPC",
            "Themes");
        return !Directory.Exists(currentDirectory) && Directory.Exists(legacyDirectory)
            ? legacyDirectory
            : currentDirectory;
    }

    public string ProfileDirectory { get; }
    internal GlobalAppearanceSettings GlobalAppearance => _globalAppearance;
    public AppearanceEditorViewModel AppearanceEditor { get; }
    public ThemeLibraryViewModel ThemeLibrary { get; }
    public event Action<ResolvedAppearance>? AppearanceChanged;
    public ObservableCollection<ProfileEntry> Profiles { get; } = new();
    public ObservableCollection<DesignerPage> Pages { get; } = new();
    public ObservableCollection<WidgetDefinition> Palette { get; }
    public IReadOnlyList<ControlDefinition> InputItems => _globalAppearance.InputItems.ToList();
    public ObservableCollection<ProfileDiagnostic> Diagnostics { get; } = new();
    public ObservableCollection<PreviewItemViewModel> PreviewItems { get; } = new();
    public ObservableCollection<PreviewItemViewModel> FixedLeftPreviewItems { get; } = new();
    public ObservableCollection<PreviewItemViewModel> FixedRightPreviewItems { get; } = new();
    public ObservableCollection<PreviewItemViewModel> SystemPreviewItems { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand AddWidgetCommand { get; }
    public ICommand AddTextSlotCommand { get; }
    public ICommand AddStickerCommand { get; }
    public ICommand DeleteSelectedWidgetCommand { get; }
    public ICommand MoveSelectedWidgetLeftCommand { get; }
    public ICommand MoveSelectedWidgetRightCommand { get; }
    public ICommand AddPageCommand { get; }
    public ICommand MovePageLeftCommand { get; }
    public ICommand MovePageRightCommand { get; }
    public ICommand ToggleValidationCommand { get; }
    public ICommand OpenAppearanceTabCommand { get; }

    public bool IsValidationExpanded
    {
        get => _isValidationExpanded;
        set => SetField(ref _isValidationExpanded, value);
    }

    public int SelectedLeftTabIndex
    {
        get => _selectedLeftTabIndex;
        set => SetField(ref _selectedLeftTabIndex, value);
    }

    public int SelectedRightTabIndex
    {
        get => _selectedRightTabIndex;
        set => SetField(ref _selectedRightTabIndex, value);
    }

    public bool IsDecorativeMediaEditMode
    {
        get => _isDecorativeMediaEditMode;
        set
        {
            if (!SetField(ref _isDecorativeMediaEditMode, value)) return;
            RefreshPreview();
        }
    }

    public bool HasErrors => Diagnostics.Any(x => x.Severity == ProfileDiagnosticSeverity.Error);
    public bool HasWarnings => Diagnostics.Any(x => x.Severity == ProfileDiagnosticSeverity.Warning);
    public string ValidationSummary => Diagnostics.Count == 0
        ? "No issues"
        : $"{Diagnostics.Count(x => x.Severity == ProfileDiagnosticSeverity.Error)} error(s), {Diagnostics.Count(x => x.Severity == ProfileDiagnosticSeverity.Warning)} warning(s)";

    public ProfileEntry? SelectedProfile
    {
        get => _selectedProfile;
        private set
        {
            if (SetField(ref _selectedProfile, value)) OnPropertyChanged(nameof(DirtyLabel));
        }
    }

    public DesignerPage? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (!SetField(ref _selectedPage, value)) return;
            OnPropertyChanged(nameof(CurrentWidgets));
            OnPropertyChanged(nameof(PageDisplayName));
            OnPropertyChanged(nameof(CurrentPageIdentity));
            var fixedSelected = SelectedWidget is not null &&
                                ((_document?.FixedLeft.Contains(SelectedWidget) == true) ||
                                 (_document?.FixedRight.Contains(SelectedWidget) == true) ||
                                 _globalAppearance.FixedLeft.Contains(SelectedWidget) ||
                                 _globalAppearance.FixedRight.Contains(SelectedWidget) ||
                                 _globalAppearance.InputItems.Contains(SelectedWidget));
            if (!fixedSelected)
                SelectedWidget = value?.Items.FirstOrDefault();
            RefreshPreview();
            ValidateCurrent();
            UpdateHistorySelection();
        }
    }

    public IReadOnlyList<ControlDefinition> CurrentWidgets => SelectedPage?.Items.ToList() ?? [];

    public ControlDefinition? SelectedWidget
    {
        get => _selectedWidget;
        set
        {
            if (!SetField(ref _selectedWidget, value)) return;
            if (_isDecorativeMediaEditMode)
            {
                _isDecorativeMediaEditMode = false;
                OnPropertyChanged(nameof(IsDecorativeMediaEditMode));
            }
            SelectedWidgetEditor = value is null ? null : new WidgetEditorViewModel(this, value);
            RefreshPreview();
            UpdateHistorySelection();
        }
    }

    public WidgetEditorViewModel? SelectedWidgetEditor
    {
        get => _selectedWidgetEditor;
        private set => SetField(ref _selectedWidgetEditor, value);
    }

    public WidgetDefinition? SelectedPaletteDefinition
    {
        get => _selectedPaletteDefinition;
        set => SetField(ref _selectedPaletteDefinition, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetField(ref _isDirty, value)) return;
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(DirtyLabel));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string WindowTitle => IsDirty ? "AdaptiveBar Studio • Unsaved" : "AdaptiveBar Studio";
    public string DirtyLabel => SelectedProfile is null ? "No profile" : IsDirty ? "Unsaved" : "Saved";
    public string ActiveContext => _document is null
        ? "No profile"
        : $"{_document.DisplayName}  /  {SelectedPage?.DisplayName ?? "No page"}";
    public string CurrentPageIdentity => SelectedPage?.Id ?? "";
    public ProfileDefinition? Document => _document;

    public string ProfileDisplayName
    {
        get => _document?.DisplayName ?? "";
        set
        {
            if (_document is null || _document.DisplayName == value) return;
            _document.DisplayName = value;
            if (SelectedProfile is not null)
                SelectedProfile.DisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveContext));
            OnDocumentEdited();
        }
    }

    public string ProfileId => _document?.Id ?? "";
    public string SchemaVersion => _document?.SchemaVersion.ToString(CultureInfo.InvariantCulture) ?? "";
    public string MatchSummary
    {
        get
        {
            if (_document is null) return "";
            var parts = new List<string>();
            if (_document.Match.ExecutablePaths.Count > 0)
                parts.Add("app: " + string.Join(", ", _document.Match.ExecutablePaths.Select(Path.GetFileName)));
            if (_document.Match.ProcessNames.Count > 0)
                parts.Add(string.Join(", ", _document.Match.ProcessNames));
            if (_document.Match.Categories.Count > 0)
                parts.Add(string.Join(", ", _document.Match.Categories));
            if (!string.IsNullOrWhiteSpace(_document.Match.Category))
                parts.Add(_document.Match.Category);
            if (!string.IsNullOrWhiteSpace(_document.Match.WindowTitleContains))
                parts.Add($"title: {_document.Match.WindowTitleContains}");
            return parts.Count == 0 ? "Default / no match rule" : string.Join(" • ", parts);
        }
    }

    public string PageDisplayName
    {
        get => SelectedPage?.DisplayName ?? "";
        set
        {
            if (SelectedPage is null || SelectedPage.IsLegacyMain || SelectedPage.DisplayName == value) return;
            SelectedPage.DisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveContext));
            OnDocumentEdited();
        }
    }

    public Brush PrimaryTextBrush => Resource("PrimaryText", Brushes.WhiteSmoke);
    public Brush SecondaryTextBrush => Resource("SecondaryText", Brushes.Gray);
    public Brush AccentBrush => Resource("AccentBrush", Brushes.MediumPurple);
    public Brush AccentSubtleBrush => Resource("AccentSubtle", Brushes.Transparent);
    public Brush ControlWellBrush => Resource("ControlWell", Brushes.Black);
    public bool EnableAnimations => true;
    public bool BackgroundMediaMotionEnabled => AppearanceEditor.MotionEnabled;

    public void RefreshProfiles()
    {
        Profiles.Clear();
        if (!Directory.Exists(ProfileDirectory))
        {
            StatusMessage = $"Profile folder not found: {ProfileDirectory}";
            return;
        }

        foreach (var path in Directory.EnumerateFiles(ProfileDirectory, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var result = _serializer.LoadFile(path);
            var name = result.Document?.DisplayName;
            var id = result.Document?.Id ?? Path.GetFileNameWithoutExtension(path);
            Profiles.Add(new ProfileEntry(
                path,
                id,
                string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name)
            {
                Category = result.Document?.Match.Category ?? result.Document?.Match.Categories.FirstOrDefault(),
                IsDefault = result.Document?.IsDefault == true
            });
        }
    }

    public bool TrySwitchProfile(ProfileEntry target, DirtyDecision decision)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(target, SelectedProfile)) return true;
        if (!ResolveDirty(decision)) return false;
        return LoadProfile(target);
    }

    public bool TryReload(DirtyDecision decision)
    {
        if (SelectedProfile is null) return false;
        if (!ResolveDirty(decision)) return false;
        return LoadProfile(SelectedProfile);
    }

    public bool DeleteSelectedProfile()
    {
        if (SelectedProfile is null)
            return false;

        var selected = SelectedProfile;
        var index = Profiles.IndexOf(selected);
        try
        {
            File.Delete(selected.FilePath);
            RefreshProfiles();

            if (Profiles.Count > 0)
            {
                var next = Profiles[Math.Clamp(index, 0, Profiles.Count - 1)];
                var loaded = LoadProfile(next);
                if (loaded)
                    StatusMessage = $"Deleted {selected.DisplayName}";
                return loaded;
            }

            _document = null;
            SelectedProfile = null;
            Pages.Clear();
            SelectedPage = null;
            SelectedWidget = null;
            IsDirty = false;
            ReplaceDiagnostics([]);
            OnPropertyChanged(nameof(Document));
            OnPropertyChanged(nameof(ProfileDisplayName));
            OnPropertyChanged(nameof(ProfileId));
            OnPropertyChanged(nameof(SchemaVersion));
            OnPropertyChanged(nameof(MatchSummary));
            OnPropertyChanged(nameof(ActiveContext));
            RefreshPreview();
            StatusMessage = $"Deleted {selected.DisplayName}";
            ResetHistory();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
            return false;
        }
    }

    public bool ResolveDirty(DirtyDecision decision)
    {
        if (!IsDirty) return true;
        return decision switch
        {
            DirtyDecision.Save => SaveCurrent(),
            DirtyDecision.Discard => true,
            _ => false
        };
    }

    public bool LoadProfile(ProfileEntry entry)
    {
        try
        {
            var result = _serializer.LoadFile(entry.FilePath);
            if (result.Document is null)
            {
                StatusMessage = $"Could not load {entry.DisplayName}.";
                ReplaceDiagnostics(result.Diagnostics);
                return false;
            }

            _document = result.Document;
            SelectedProfile = entry;
            entry.DisplayName = string.IsNullOrWhiteSpace(_document.DisplayName) ? entry.DisplayName : _document.DisplayName;
            BuildPages();
            IsDirty = false;
            OnPropertyChanged(nameof(Document));
            OnPropertyChanged(nameof(ProfileDisplayName));
            OnPropertyChanged(nameof(ProfileId));
            OnPropertyChanged(nameof(SchemaVersion));
            OnPropertyChanged(nameof(MatchSummary));
            OnPropertyChanged(nameof(ActiveContext));
            ValidateCurrent();
            StatusMessage = $"Loaded {entry.DisplayName}";
            ResetHistory();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
            return false;
        }
    }

    public bool AddSelectedPaletteWidget() =>
        SelectedPaletteDefinition is not null && AddWidget(SelectedPaletteDefinition);

    public bool AddWidget(WidgetDefinition definition)
        => AddWidget(definition, SelectedPage?.Items.Count ?? 0);

    public bool AddWidget(WidgetDefinition definition, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (SelectedPage is null)
            return false;
        if (!WidgetDefinitionRegistry.TryGet(definition.TypeId, out var registered) || !registered.IsPlaceable)
        {
            StatusMessage = $"{definition.DisplayName} is runtime-generated and cannot be added.";
            return false;
        }

        var widget = CreateWidget(registered);

        insertionIndex = Math.Clamp(insertionIndex, 0, SelectedPage.Items.Count);
        SelectedPage.Items.Insert(insertionIndex, widget);
        OnPropertyChanged(nameof(CurrentWidgets));
        SelectedWidget = widget;
        OnDocumentEdited();
        StatusMessage = $"Added {registered.DisplayName}";
        return true;
    }

    public bool AddInputItem(string type)
    {
        if (type is not (WidgetTypes.TextSlot or WidgetTypes.Sticker) ||
            !WidgetDefinitionRegistry.TryGet(type, out var definition))
        {
            StatusMessage = "Only text slots and stickers can be configured as shared input tools.";
            return false;
        }

        var widget = CreateWidget(definition);
        _globalAppearance.InputItems.Add(widget);
        OnPropertyChanged(nameof(InputItems));
        SelectedWidget = widget;
        TrySaveGlobalAppearance();
        StatusMessage = $"Added shared {definition.DisplayName}";
        RecordHistory();
        return true;
    }

    public bool AssignApplication(string executablePath)
    {
        if (_document is null || string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            StatusMessage = "Application executable not found.";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            _document.Match.ExecutablePaths = [fullPath];
            _document.Match.ProcessNames = [Path.GetFileNameWithoutExtension(fullPath)];
            OnPropertyChanged(nameof(MatchSummary));
            OnDocumentEdited();
            StatusMessage = $"Assigned {Path.GetFileName(fullPath)}";
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusMessage = $"Application assignment failed: {ex.Message}";
            return false;
        }
    }

    public bool CreateProfileForApplication(string executablePath, DirtyDecision decision)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Choose an existing application executable (.exe).";
            return false;
        }
        if (!ResolveDirty(decision)) return false;
        string? temporaryPath = null;
        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            var name = Path.GetFileNameWithoutExtension(fullPath);
            var stem = string.Concat(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')).Trim('-');
            if (stem.Length == 0) stem = "application";
            var id = stem;
            for (var suffix = 2; Profiles.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                 File.Exists(Path.Combine(ProfileDirectory, id + ".json")); suffix++) id = $"{stem}-{suffix}";
            var document = new ProfileDefinition
            {
                Id = id, DisplayName = name, IsDefault = false,
                Match = new MatchDefinition { ExecutablePaths = [fullPath], ProcessNames = [name] },
                Pages = [new PageDefinition { Id = "main", DisplayName = "Main" }]
            };
            Directory.CreateDirectory(ProfileDirectory);
            temporaryPath = Path.Combine(ProfileDirectory, $".{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, _serializer.Save(document));
            var filePath = Path.Combine(ProfileDirectory, id + ".json");
            File.Move(temporaryPath, filePath, overwrite: false);
            temporaryPath = null;
            var entry = new ProfileEntry(filePath, id, name);
            Profiles.Add(entry);
            return LoadProfile(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusMessage = $"Create profile failed: {ex.Message}";
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Best effort for an uncommitted temporary file. */ }
            }
        }
    }

    public async Task<string?> ImportMediaAssetAsync(string sourcePath, bool sticker = false)
    {
        if (_isImportingMedia) return null;
        _isImportingMedia = true;
        OnPropertyChanged(nameof(IsMediaImportAvailable));
        StatusMessage = "Importing media…";
        try
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(ProfileDirectory)) ?? Path.GetFullPath(ProfileDirectory);
            var imported = await Task.Run(() => CopyMediaAsset(root, sourcePath, sticker));
            StatusMessage = "Media imported.";
            return imported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusMessage = $"Media import failed: {ex.Message}";
            return null;
        }
        finally
        {
            _isImportingMedia = false;
            OnPropertyChanged(nameof(IsMediaImportAvailable));
        }
    }

    private static string CopyMediaAsset(string root, string sourcePath, bool sticker)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Media file not found.");
        if (sticker && StickerImageLoader.Load(sourcePath, 32) is null)
            throw new InvalidDataException("Sticker must be a valid PNG or JPEG up to 20 MB and 16 MP.");
        var source = Path.GetFullPath(sourcePath);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (source.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(root, source).Replace('\\', '/');

        var mediaDirectory = Path.Combine(root, "Assets", "media");
        Directory.CreateDirectory(mediaDirectory);
        var name = Path.GetFileNameWithoutExtension(source);
        var extension = Path.GetExtension(source);
        var destination = Path.Combine(mediaDirectory, name + extension);
        for (var suffix = 2; File.Exists(destination); suffix++)
            destination = Path.Combine(mediaDirectory, $"{name}-{suffix}{extension}");

        File.Copy(source, destination);
        return Path.GetRelativePath(root, destination).Replace('\\', '/');
    }

    internal string? ResolveMediaAsset(string? source) => _previewAdapter.ResolveMediaAsset(source);

    public bool MoveWidget(ControlDefinition widget, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(widget);
        var items = GetWidgetCollection(widget);
        if (items is null)
            return false;
        var oldIndex = items.IndexOf(widget);
        if (oldIndex < 0)
            return false;

        insertionIndex = Math.Clamp(insertionIndex, 0, items.Count);
        if (oldIndex < insertionIndex)
            insertionIndex--;
        if (insertionIndex == oldIndex)
            return false;

        items.RemoveAt(oldIndex);
        items.Insert(insertionIndex, widget);
        if (ReferenceEquals(items, SelectedPage?.Items))
            OnPropertyChanged(nameof(CurrentWidgets));
        if (ReferenceEquals(items, _globalAppearance.InputItems))
            OnPropertyChanged(nameof(InputItems));
        SelectedWidget = widget;
        if (IsGlobalWidget(widget))
        {
            TrySaveGlobalAppearance();
            RefreshPreview();
        }
        else
        {
            OnDocumentEdited();
        }
        StatusMessage = $"Moved {widget.Id}";
        RecordHistory();
        return true;
    }

    public bool ResizeWidget(ControlDefinition widget, double logicalWidth, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (GetWidgetCollection(widget) is null ||
            !WidgetDefinitionRegistry.TryGet(widget.Type, out var definition) ||
            !definition.IsPlaceable || definition.MaxWidth <= definition.MinWidth ||
            !double.IsFinite(logicalWidth))
        {
            return false;
        }

        var width = Math.Clamp(logicalWidth, definition.MinWidth, definition.MaxWidth);
        if (Math.Abs(widget.Width - width) < 0.01)
            return false;

        widget.Width = width;
        if (ReferenceEquals(widget, SelectedWidget))
            SelectedWidgetEditor?.RefreshWidthFromModel();
        RefreshWidgetPreview(widget);
        if (IsGlobalWidget(widget))
            TrySaveGlobalAppearance();
        else
            IsDirty = true;
        OnPropertyChanged(nameof(ActiveContext));
        if (validate)
            ValidateCurrent();
        StatusMessage = $"Width {width:0.##} DIP";
        RecordHistory($"widget:{GetWidgetPlacement(widget)}:{widget.Id}:WidthText");
        return true;
    }

    public bool MoveSelectedWidget(int delta)
    {
        if (SelectedWidget is null || delta == 0)
            return false;
        var items = GetWidgetCollection(SelectedWidget);
        if (items is null)
            return false;
        var oldIndex = items.IndexOf(SelectedWidget);
        var newIndex = oldIndex + Math.Sign(delta);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= items.Count)
            return false;
        return MoveWidget(SelectedWidget, newIndex < oldIndex ? newIndex : newIndex + 1);
    }

    public bool DeleteSelectedWidget()
    {
        if (SelectedWidget is null)
            return false;

        var widget = SelectedWidget;
        var id = widget.Id;
        var items = GetWidgetCollection(widget);
        if (items is null)
            return false;
        var wasGlobal = IsGlobalWidget(widget);
        var index = items.IndexOf(widget);
        if (index < 0)
            return false;

        items.RemoveAt(index);
        if (ReferenceEquals(items, SelectedPage?.Items))
            OnPropertyChanged(nameof(CurrentWidgets));
        if (ReferenceEquals(items, _globalAppearance.InputItems))
            OnPropertyChanged(nameof(InputItems));
        SelectedWidget = index < items.Count ? items[index] : items.LastOrDefault() ?? SelectedPage?.Items.FirstOrDefault();
        if (wasGlobal)
        {
            TrySaveGlobalAppearance();
            RefreshPreview();
            ValidateCurrent();
        }
        else
        {
            OnDocumentEdited();
        }
        StatusMessage = $"Deleted {id}";
        RecordHistory();
        return true;
    }

    public bool AddPage()
    {
        if (_document is null)
            return false;

        EnsurePageDefinitions();
        var index = SelectedPage is null ? Pages.Count : Pages.IndexOf(SelectedPage) + 1;
        index = Math.Clamp(index, 0, Pages.Count);
        var page = new PageDefinition
        {
            Id = NewPageId(),
            DisplayName = "New Page",
            Items = []
        };
        _document.Pages.Insert(index, page);
        var designerPage = new DesignerPage(page, page.Id, page.DisplayName, page.Items);
        Pages.Insert(index, designerPage);
        SelectedPage = designerPage;
        OnDocumentEdited();
        StatusMessage = "Added page";
        return true;
    }

    public bool DeleteSelectedPage()
    {
        if (_document is null || SelectedPage is null)
            return false;

        EnsurePageDefinitions();
        if (SelectedPage?.Definition is null)
            return false;

        var index = Pages.IndexOf(SelectedPage);
        if (index < 0)
            return false;

        _document.Pages.RemoveAt(index);
        Pages.RemoveAt(index);
        SelectedPage = index < Pages.Count ? Pages[index] : Pages.LastOrDefault();
        OnDocumentEdited();
        StatusMessage = "Deleted page";
        return true;
    }

    public bool MoveSelectedPage(int delta)
    {
        if (_document is null || SelectedPage is null || delta == 0)
            return false;

        EnsurePageDefinitions();
        if (SelectedPage?.Definition is null)
            return false;

        var oldIndex = Pages.IndexOf(SelectedPage);
        var newIndex = oldIndex + Math.Sign(delta);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Pages.Count)
            return false;

        var definition = _document.Pages[oldIndex];
        _document.Pages.RemoveAt(oldIndex);
        _document.Pages.Insert(newIndex, definition);
        Pages.Move(oldIndex, newIndex);
        OnDocumentEdited();
        StatusMessage = $"Moved page {SelectedPage.Id}";
        return true;
    }

    public bool SaveCurrent()
    {
        if (_document is null || SelectedProfile is null)
            return false;

        ValidateCurrent();
        if (Diagnostics.Any(x => x.Severity == ProfileDiagnosticSeverity.Error))
        {
            StatusMessage = "Save blocked: fix validation errors first.";
            return false;
        }

        try
        {
            _serializer.SaveFile(SelectedProfile.FilePath, _document);
            IsDirty = false;
            MarkHistorySaved();
            StatusMessage = $"Saved {SelectedProfile.FileName}";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            return false;
        }
    }

    public IReadOnlyList<ProfileDiagnostic> ValidateCurrent()
    {
        if (_document is null)
        {
            ReplaceDiagnostics([]);
            return [];
        }

        var diagnostics = _serializer.Validate(_document).ToList();
        var editorDiagnostic = SelectedWidgetEditor?.GetEditorDiagnostic(_document.Id, SelectedPage?.Id);
        if (editorDiagnostic is not null)
            diagnostics.Add(editorDiagnostic);
        ReplaceDiagnostics(diagnostics);
        StatusMessage = diagnostics.Count == 0
            ? "No validation issues"
            : $"{diagnostics.Count(x => x.Severity == ProfileDiagnosticSeverity.Error)} error(s), {diagnostics.Count(x => x.Severity == ProfileDiagnosticSeverity.Warning)} warning(s)";
        return diagnostics;
    }

    public void SelectWidgetById(string widgetId)
    {
        SelectedWidget = FindWidgetById(widgetId);
    }

    internal ControlDefinition? FindWidgetById(string widgetId)
    {
        if (SelectedPage?.Items.FirstOrDefault(x =>
                string.Equals(x.Id, widgetId, StringComparison.OrdinalIgnoreCase)) is { } pageWidget)
            return pageWidget;
        if (_globalAppearance.FixedLeft.FirstOrDefault(x =>
                string.Equals(x.Id, widgetId, StringComparison.OrdinalIgnoreCase)) is { } globalLeftWidget)
            return globalLeftWidget;
        if (_globalAppearance.InputItems.FirstOrDefault(x =>
                string.Equals(x.Id, widgetId, StringComparison.OrdinalIgnoreCase)) is { } inputItem)
            return inputItem;
        if (_document?.FixedLeft.FirstOrDefault(x =>
                string.Equals(x.Id, widgetId, StringComparison.OrdinalIgnoreCase)) is { } leftWidget)
            return leftWidget;
        if (_document?.FixedRight.FirstOrDefault(x =>
                string.Equals(x.Id, widgetId, StringComparison.OrdinalIgnoreCase)) is { } rightWidget)
            return rightWidget;
        return _globalAppearance.FixedRight.FirstOrDefault(x =>
            string.Equals(x.Id, widgetId, StringComparison.OrdinalIgnoreCase));
    }

    internal string GetWidgetPlacement(ControlDefinition widget)
    {
        if (_globalAppearance.FixedLeft.Contains(widget)) return "Fixed Left (All Profiles)";
        if (_globalAppearance.FixedRight.Contains(widget)) return "Fixed Right (All Profiles)";
        if (_globalAppearance.InputItems.Contains(widget)) return "Input Tools (All Profiles)";
        if (_document?.FixedLeft.Contains(widget) == true) return "Fixed Left";
        if (_document?.FixedRight.Contains(widget) == true) return "Fixed Right";
        return "Current Page";
    }

    internal bool MoveWidgetToPlacement(ControlDefinition widget, string placement)
    {
        if (_document is null)
            return false;

        var source = GetWidgetCollection(widget);
        var sourceIsGlobal = IsGlobalWidget(widget);
        var target = placement switch
        {
            "Fixed Left" => _document.FixedLeft,
            "Fixed Right" => _document.FixedRight,
            "Fixed Left (All Profiles)" => _globalAppearance.FixedLeft,
            "Fixed Right (All Profiles)" => _globalAppearance.FixedRight,
            "Input Tools (All Profiles)" when widget.Type is WidgetTypes.TextSlot or WidgetTypes.Sticker => _globalAppearance.InputItems,
            "Current Page" => SelectedPage?.Items,
            _ => null
        };
        if (source is null || target is null || ReferenceEquals(source, target))
            return false;

        source.Remove(widget);
        widget.System = false;
        target.Add(widget);
        var targetIsGlobal = ReferenceEquals(target, _globalAppearance.FixedLeft) ||
                             ReferenceEquals(target, _globalAppearance.FixedRight) ||
                             ReferenceEquals(target, _globalAppearance.InputItems);
        if (ReferenceEquals(source, SelectedPage?.Items) || ReferenceEquals(target, SelectedPage?.Items))
            OnPropertyChanged(nameof(CurrentWidgets));
        if (ReferenceEquals(source, _globalAppearance.InputItems) || ReferenceEquals(target, _globalAppearance.InputItems))
            OnPropertyChanged(nameof(InputItems));
        if (sourceIsGlobal || targetIsGlobal)
            TrySaveGlobalAppearance();
        if (!sourceIsGlobal || !targetIsGlobal)
            IsDirty = true;
        OnPropertyChanged(nameof(ActiveContext));
        RefreshPreview();
        ValidateCurrent();
        StatusMessage = $"Moved {widget.Id} to {placement}";
        RecordHistory();
        return true;
    }

    public void SelectDiagnostic(ProfileDiagnostic diagnostic)
    {
        var page = Pages.FirstOrDefault(x =>
            string.Equals(x.Id, diagnostic.PageId, StringComparison.OrdinalIgnoreCase));
        if (page is not null)
            SelectedPage = page;
        if (!string.IsNullOrWhiteSpace(diagnostic.WidgetId))
            SelectWidgetById(diagnostic.WidgetId);
    }

    internal void OnDocumentEdited([CallerMemberName] string? property = null)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(ActiveContext));
        RefreshPreview();
        ValidateCurrent();
        RecordHistory(property is nameof(ProfileDisplayName) or nameof(PageDisplayName) ? property : null);
    }

    internal void OnWidgetEdited(ControlDefinition widget, [CallerMemberName] string? property = null)
    {
        if (IsGlobalWidget(widget))
            TrySaveGlobalAppearance();
        else
            IsDirty = true;
        OnPropertyChanged(nameof(ActiveContext));
        RefreshWidgetPreview(widget);
        ValidateCurrent();
        RecordHistory($"widget:{GetWidgetPlacement(widget)}:{widget.Id}:{property}");
    }

    internal void OnAppearanceEdited(bool deferSave = false, bool notifyAppearance = true, [CallerMemberName] string? property = null)
    {
        if (deferSave)
        {
            _appearanceSavePending = true;
            _appearanceSaveTimer.Stop();
            _appearanceSaveTimer.Start();
        }
        else
        {
            _appearanceSaveTimer.Stop();
            _appearanceSavePending = false;
            TrySaveGlobalAppearance();
        }
        if (notifyAppearance)
            NotifyAppearanceChanged();
        RecordHistory($"appearance:{property}");
    }

    internal void FlushPendingAppearanceSave()
    {
        _appearanceSaveTimer.Stop();
        if (!_appearanceSavePending)
            return;
        _appearanceSavePending = false;
        TrySaveGlobalAppearance();
    }

    public void NotifyAppearanceChanged()
    {
        OnPropertyChanged(nameof(BackgroundMediaMotionEnabled));
        var resolved = AppearanceResolver.Resolve(_globalAppearance.Theme, _globalAppearance.Appearance);
        AppearanceChanged?.Invoke(resolved);
    }

    public bool SaveTheme(string path)
    {
        try
        {
            GlobalAppearanceStore.SaveTheme(_globalAppearance, path);
            StatusMessage = $"Saved theme: {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Theme save failed: {ex.Message}";
            return false;
        }
    }

    public bool LoadTheme(string path)
    {
        try
        {
            var settings = GlobalAppearanceStore.LoadTheme(path);
            ApplyTheme(settings, Path.GetFileName(path));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Theme load failed: {ex.Message}";
            return false;
        }
    }

    internal void ApplyTheme(GlobalAppearanceSettings settings, string name)
    {
        settings.FixedLeft = _globalAppearance.FixedLeft;
        settings.FixedRight = _globalAppearance.FixedRight;
        settings.InputItems = _globalAppearance.InputItems;
        // Persist before applying so a failed import leaves the current theme intact.
        GlobalAppearanceStore.Save(settings, _appearanceFilePath);
        _appearanceSaveTimer.Stop();
        _appearanceSavePending = false;
        _globalAppearance = settings;
        AppearanceEditor.LoadFromSettings(settings);
        NotifyAppearanceChanged();
        StatusMessage = $"Loaded theme: {name}";
        RecordHistory();
    }

    private void TrySaveGlobalAppearance()
    {
        try
        {
            GlobalAppearanceStore.Save(_globalAppearance, _appearanceFilePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Appearance save failed: {ex.Message}";
        }
    }

    private void BuildPages()
    {
        Pages.Clear();
        if (_document is null)
        {
            SelectedPage = null;
            return;
        }

        foreach (var page in _document.Pages)
            Pages.Add(new DesignerPage(page, page.Id, page.DisplayName, page.Items));

        if (Pages.Count == 0)
            Pages.Add(new DesignerPage(null, "main", "Main", _document.Items));

        SelectedPage = Pages.FirstOrDefault();
    }

    private void RefreshPreview()
    {
        PreviewItems.Clear();
        FixedLeftPreviewItems.Clear();
        FixedRightPreviewItems.Clear();
        SystemPreviewItems.Clear();

        if (_document is not null)
        {
            foreach (var widget in _globalAppearance.FixedLeft)
                FixedLeftPreviewItems.Add(CreatePreviewItem(widget));
            foreach (var widget in _document.FixedLeft)
                FixedLeftPreviewItems.Add(CreatePreviewItem(widget));
            foreach (var widget in _document.FixedRight)
                FixedRightPreviewItems.Add(CreatePreviewItem(widget));
            foreach (var widget in _globalAppearance.FixedRight)
                FixedRightPreviewItems.Add(CreatePreviewItem(widget));
        }
        else
        {
            foreach (var widget in _globalAppearance.FixedLeft)
                FixedLeftPreviewItems.Add(CreatePreviewItem(widget));
            foreach (var widget in _globalAppearance.FixedRight)
                FixedRightPreviewItems.Add(CreatePreviewItem(widget));
        }

        if (SelectedPage is null)
            return;

        foreach (var widget in SelectedPage.Items)
        {
            var item = CreatePreviewItem(widget);
            if (widget.System)
                SystemPreviewItems.Add(item);
            else
                PreviewItems.Add(item);
        }
    }

    private void RefreshWidgetPreview(ControlDefinition widget)
    {
        var item = FixedLeftPreviewItems.Concat(PreviewItems).Concat(FixedRightPreviewItems).Concat(SystemPreviewItems).FirstOrDefault(x =>
            string.Equals(x.WidgetId, widget.Id, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            item.Content = _previewAdapter.CreateContent(widget, IsDecorativeMediaEditing(widget));
    }

    private PreviewItemViewModel CreatePreviewItem(ControlDefinition widget) =>
        _previewAdapter.Create(widget, ReferenceEquals(widget, SelectedWidget), IsDecorativeMediaEditing(widget));

    private bool IsDecorativeMediaEditing(ControlDefinition widget) =>
        _isDecorativeMediaEditMode && ReferenceEquals(widget, SelectedWidget) && widget.Type == WidgetTypes.DecorativeMedia;

    private bool CanMoveSelectedWidget(int delta)
    {
        if (SelectedWidget is null)
            return false;
        var items = GetWidgetCollection(SelectedWidget);
        if (items is null)
            return false;
        var index = items.IndexOf(SelectedWidget);
        var target = index + Math.Sign(delta);
        return delta != 0 && index >= 0 && target >= 0 && target < items.Count;
    }

    internal List<ControlDefinition>? GetWidgetCollection(ControlDefinition widget)
    {
        if (SelectedPage?.Items.Contains(widget) == true) return SelectedPage.Items;
        if (_globalAppearance.FixedLeft.Contains(widget)) return _globalAppearance.FixedLeft;
        if (_globalAppearance.FixedRight.Contains(widget)) return _globalAppearance.FixedRight;
        if (_globalAppearance.InputItems.Contains(widget)) return _globalAppearance.InputItems;
        if (_document?.FixedLeft.Contains(widget) == true) return _document.FixedLeft;
        if (_document?.FixedRight.Contains(widget) == true) return _document.FixedRight;
        return null;
    }

    private bool IsGlobalWidget(ControlDefinition widget) =>
        _globalAppearance.FixedLeft.Contains(widget) ||
        _globalAppearance.FixedRight.Contains(widget) ||
        _globalAppearance.InputItems.Contains(widget);

    private ControlDefinition CreateWidget(WidgetDefinition definition)
    {
        var widget = new ControlDefinition
        {
            Type = definition.TypeId,
            Id = NewWidgetId(),
            Width = definition.DefaultWidth,
            Label = definition.TypeId is WidgetTypes.Spacer or WidgetTypes.Media or WidgetTypes.DynamicList or WidgetTypes.DecorativeMedia
                ? ""
                : definition.DisplayName
        };
        if (definition.TypeId == WidgetTypes.ExpandableSlider)
        {
            widget.Value = 50;
            widget.Enabled = true;
        }
        else if (definition.TypeId == WidgetTypes.DecorativeMedia)
        {
            widget.Source = "";
            widget.Fit = "contain";
            widget.Loop = true;
            widget.Opacity = 1d;
            widget.Enabled = true;
        }
        else if (definition.TypeId == WidgetTypes.TextSlot)
        {
            widget.Label = "Text slot";
            widget.Action = new ActionDefinition { Type = "clipboard_text", Value = "Saved text" };
        }
        else if (definition.TypeId == WidgetTypes.Sticker)
        {
            widget.Label = "Sticker";
            widget.Source = "";
        }

        return widget;
    }

    private bool CanMoveSelectedPage(int delta)
    {
        if (SelectedPage is null)
            return false;
        var index = Pages.IndexOf(SelectedPage);
        var target = index + Math.Sign(delta);
        return delta != 0 && index >= 0 && target >= 0 && target < Pages.Count;
    }

    private void EnsurePageDefinitions()
    {
        if (_document is null || _document.Pages.Count != 0 || Pages.Count != 1 || !Pages[0].IsLegacyMain)
            return;

        var legacy = Pages[0];
        var page = new PageDefinition
        {
            Id = legacy.Id,
            DisplayName = legacy.DisplayName,
            Items = legacy.Items
        };
        _document.Pages.Add(page);
        _document.Items = [];
        var replacement = new DesignerPage(page, page.Id, page.DisplayName, page.Items);
        Pages[0] = replacement;
        SelectedPage = replacement;
    }

    internal void ReplaceDiagnostics(IEnumerable<ProfileDiagnostic> diagnostics)
    {
        Diagnostics.Clear();
        foreach (var diagnostic in diagnostics)
            Diagnostics.Add(diagnostic);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(ValidationSummary));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private static string NewWidgetId() => $"widget-{Guid.NewGuid():N}";
    private static string NewPageId() => $"page-{Guid.NewGuid():N}";

    private static Brush Resource(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class DesignerPaths
{
    public static string FindProfileDirectory()
    {
        var starts = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var start in starts)
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            for (var i = 0; current is not null && i < 9; i++, current = current.Parent)
            {
                var repoProfiles = Path.Combine(current.FullName, "src", "AdaptiveBarPC.App", "Profiles");
                if (Directory.Exists(repoProfiles))
                    return repoProfiles;

                var siblingProfiles = Path.Combine(current.FullName, "AdaptiveBarPC.App", "Profiles");
                if (Directory.Exists(siblingProfiles))
                    return siblingProfiles;

                var localProfiles = Path.Combine(current.FullName, "Profiles");
                if (Directory.Exists(localProfiles))
                    return localProfiles;
            }
        }

        return Path.Combine(Environment.CurrentDirectory, "Profiles");
    }
}

public sealed class EditorCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public EditorCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
