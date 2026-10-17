using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Designer;

public sealed class QuickColorItem
{
    public string Hex { get; }
    public string Name { get; }
    public Brush Brush { get; }
    public string Tooltip => string.IsNullOrWhiteSpace(Name) ? Hex : $"{Name} ({Hex})";

    public QuickColorItem(string hex, string name)
    {
        Hex = hex;
        Name = name;
        Brush = AppearanceEditorViewModel.CreateFrozenBrush(hex);
    }
}

public static class QuickColorPalette
{
    public static IReadOnlyList<QuickColorItem> Colors { get; } = new List<QuickColorItem>
    {
        // 10 Neutrals
        new("#FF0A0A0D", "Deep Black"),
        new("#FF0F172A", "Dark Navy"),
        new("#FF181C20", "Dark Gray"),
        new("#FF1C1C24", "Surface Gray"),
        new("#FF282834", "Control Gray"),
        new("#FF71717A", "Zinc Gray"),
        new("#FFA1A1AA", "Muted Gray"),
        new("#FFE4E4E7", "Light Gray"),
        new("#FFF5F5F7", "Off White"),
        new("#FFFFFFFF", "Pure White"),

        // 10 Accents
        new("#FF7C5CFC", "Fluid Violet"),
        new("#FF3B82F6", "Electric Blue"),
        new("#FF00F0FF", "Cyber Cyan"),
        new("#FF00E5FF", "Pixel Cyan"),
        new("#FF11998E", "Teal"),
        new("#FF10B981", "Emerald Green"),
        new("#FF38EF7D", "Pixel Green"),
        new("#FFF59E0B", "Amber"),
        new("#FFEF4444", "Red"),
        new("#FFFF007F", "Cyber Magenta")
    };
}

public sealed class ColorFieldViewModel : INotifyPropertyChanged
{
    private readonly Action<string> _onColorChanged;
    private readonly Action _onReset;
    private string _value = "";
    private string _text = "";
    private Brush _brush = Brushes.Transparent;
    private bool _hasCustom;
    private bool _isFlyoutOpen;
    private bool _isUpdatingInternally;
    private DateTime _lastClosedTime = DateTime.MinValue;

    private byte _a = 0xFF;
    private byte _r;
    private byte _g;
    private byte _b;
    private double _hue;
    private double _saturation = 1.0;
    private double _brightness = 1.0;
    private Brush _hueBrush = Brushes.Red;

    public string Label { get; }
    public string? SubLabel { get; }
    public bool HasSubLabel => !string.IsNullOrWhiteSpace(SubLabel);
    public string? SubLabelClean => string.IsNullOrWhiteSpace(SubLabel)
        ? null
        : SubLabel.Trim().TrimStart('(').TrimEnd(')');
    public string FullLabelTooltip => HasSubLabel
        ? $"{Label} — {SubLabelClean}"
        : Label;

    public string ResetLabel { get; }
    public string ResetTooltip { get; }

    public IReadOnlyList<QuickColorItem> QuickColors => QuickColorPalette.Colors;

    public ICommand SelectColorCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ToggleFlyoutCommand { get; }
    public ICommand CopyHexCommand { get; }

    public ColorFieldViewModel(
        string label,
        string? subLabel,
        string resetLabel,
        string resetTooltip,
        Action<string> onColorChanged,
        Action onReset)
    {
        Label = label;
        SubLabel = subLabel;
        ResetLabel = resetLabel;
        ResetTooltip = resetTooltip;
        _onColorChanged = onColorChanged ?? throw new ArgumentNullException(nameof(onColorChanged));
        _onReset = onReset ?? throw new ArgumentNullException(nameof(onReset));

        SelectColorCommand = new EditorCommand(param =>
        {
            if (param is string hex)
                SelectColor(hex);
        });

        ResetCommand = new EditorCommand(_ => Reset());
        ToggleFlyoutCommand = new EditorCommand(_ => ToggleFlyout());
        CopyHexCommand = new EditorCommand(_ =>
        {
            if (!string.IsNullOrWhiteSpace(Value))
            {
                try { Clipboard.SetText(Value); } catch { /* ignore clipboard locks */ }
            }
        });

        UpdateHueBrush();
    }

    public double Hue
    {
        get => _hue;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 360.0);
            if (Math.Abs(_hue - clamped) < 0.001) return;
            _hue = clamped;
            OnPropertyChanged();
            UpdateHueBrush();
            if (!_isUpdatingInternally)
            {
                ApplyHsv();
            }
        }
    }

    public double Saturation
    {
        get => _saturation;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_saturation - clamped) < 0.001) return;
            _saturation = clamped;
            OnPropertyChanged();
            if (!_isUpdatingInternally)
            {
                ApplyHsv();
            }
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_brightness - clamped) < 0.001) return;
            _brightness = clamped;
            OnPropertyChanged();
            if (!_isUpdatingInternally)
            {
                ApplyHsv();
            }
        }
    }

    public Brush HueBrush
    {
        get => _hueBrush;
        private set => SetField(ref _hueBrush, value);
    }

    private void UpdateHueBrush()
    {
        AppearanceEditorViewModel.HsvToRgb(_hue, 1.0, 1.0, out var r, out var g, out var b);
        HueBrush = AppearanceEditorViewModel.CreateFrozenBrush($"#FF{r:X2}{g:X2}{b:X2}");
    }

    public int R
    {
        get => _r;
        set => SetChannel(ref _r, value, nameof(R), nameof(RText));
    }

    public int G
    {
        get => _g;
        set => SetChannel(ref _g, value, nameof(G), nameof(GText));
    }

    public int B
    {
        get => _b;
        set => SetChannel(ref _b, value, nameof(B), nameof(BText));
    }

    public string RText
    {
        get => _r.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
            {
                R = parsed;
            }
        }
    }

    public string GText
    {
        get => _g.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
            {
                G = parsed;
            }
        }
    }

    public string BText
    {
        get => _b.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
            {
                B = parsed;
            }
        }
    }

    private void SetChannel(ref byte channel, int value, string channelName, string textName)
    {
        byte clamped = (byte)Math.Clamp(value, 0, 255);
        if (channel == clamped) return;
        channel = clamped;
        OnPropertyChanged(channelName);
        OnPropertyChanged(textName);
        if (!_isUpdatingInternally)
        {
            ApplyRgb();
        }
    }

    private void ApplyHsv()
    {
        AppearanceEditorViewModel.HsvToRgb(_hue, _saturation, _brightness, out var r, out var g, out var b);
        _a = 0xFF;
        _r = r;
        _g = g;
        _b = b;

        OnPropertyChanged(nameof(R));
        OnPropertyChanged(nameof(G));
        OnPropertyChanged(nameof(B));
        OnPropertyChanged(nameof(RText));
        OnPropertyChanged(nameof(GText));
        OnPropertyChanged(nameof(BText));

        var newHex = $"#{_a:X2}{_r:X2}{_g:X2}{_b:X2}";
        if (string.Equals(_value, newHex, StringComparison.OrdinalIgnoreCase))
            return;

        _onColorChanged(newHex);
    }

    private void ApplyRgb()
    {
        AppearanceEditorViewModel.RgbToHsv(_r, _g, _b, out var h, out var s, out var v);
        _isUpdatingInternally = true;
        try
        {
            if (s > 0.001)
            {
                _hue = h;
                OnPropertyChanged(nameof(Hue));
                UpdateHueBrush();
            }
            _saturation = s;
            _brightness = v;
            OnPropertyChanged(nameof(Saturation));
            OnPropertyChanged(nameof(Brightness));
        }
        finally
        {
            _isUpdatingInternally = false;
        }

        var newHex = $"#{_a:X2}{_r:X2}{_g:X2}{_b:X2}";
        if (string.Equals(_value, newHex, StringComparison.OrdinalIgnoreCase))
            return;

        _onColorChanged(newHex);
    }

    public string Value
    {
        get => _value;
        private set
        {
            if (SetField(ref _value, value))
            {
                Brush = AppearanceEditorViewModel.CreateFrozenBrush(value);
            }
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                if (_isUpdatingInternally) return;
                if (AppearanceEditorViewModel.TryParseHexColor(value, out var validHex))
                {
                    if (AppearanceEditorViewModel.TryParseRgb(validHex, out var a, out var r, out var g, out var b))
                    {
                        _isUpdatingInternally = true;
                        try
                        {
                            _a = a;
                            _r = r;
                            _g = g;
                            _b = b;
                            AppearanceEditorViewModel.RgbToHsv(r, g, b, out var h, out var s, out var v);
                            if (s > 0.001)
                            {
                                _hue = h;
                                OnPropertyChanged(nameof(Hue));
                                UpdateHueBrush();
                            }
                            _saturation = s;
                            _brightness = v;
                            OnPropertyChanged(nameof(Saturation));
                            OnPropertyChanged(nameof(Brightness));

                            OnPropertyChanged(nameof(R));
                            OnPropertyChanged(nameof(G));
                            OnPropertyChanged(nameof(B));
                            OnPropertyChanged(nameof(RText));
                            OnPropertyChanged(nameof(GText));
                            OnPropertyChanged(nameof(BText));
                        }
                        finally
                        {
                            _isUpdatingInternally = false;
                        }
                    }

                    if (!string.Equals(_value, validHex, StringComparison.OrdinalIgnoreCase))
                    {
                        _onColorChanged(validHex);
                    }
                }
            }
        }
    }

    public Brush Brush
    {
        get => _brush;
        private set => SetField(ref _brush, value);
    }

    public bool HasCustom
    {
        get => _hasCustom;
        private set => SetField(ref _hasCustom, value);
    }

    public bool IsFlyoutOpen
    {
        get => _isFlyoutOpen;
        set
        {
            if (SetField(ref _isFlyoutOpen, value))
            {
                if (!value)
                {
                    _lastClosedTime = DateTime.UtcNow;
                    if (!AppearanceEditorViewModel.TryParseHexColor(_text, out _))
                    {
                        _isUpdatingInternally = true;
                        Text = _value;
                        if (AppearanceEditorViewModel.TryParseRgb(_value, out var a, out var r, out var g, out var b))
                        {
                            _a = a;
                            _r = r;
                            _g = g;
                            _b = b;
                            AppearanceEditorViewModel.RgbToHsv(r, g, b, out var h, out var s, out var v);
                            if (s > 0.001)
                            {
                                _hue = h;
                                OnPropertyChanged(nameof(Hue));
                                UpdateHueBrush();
                            }
                            _saturation = s;
                            _brightness = v;
                            OnPropertyChanged(nameof(Saturation));
                            OnPropertyChanged(nameof(Brightness));

                            OnPropertyChanged(nameof(R));
                            OnPropertyChanged(nameof(G));
                            OnPropertyChanged(nameof(B));
                            OnPropertyChanged(nameof(RText));
                            OnPropertyChanged(nameof(GText));
                            OnPropertyChanged(nameof(BText));
                        }
                        _isUpdatingInternally = false;
                    }
                }
            }
        }
    }

    public void ToggleFlyout()
    {
        if (IsFlyoutOpen)
        {
            IsFlyoutOpen = false;
            return;
        }

        if ((DateTime.UtcNow - _lastClosedTime).TotalMilliseconds < 250)
        {
            return;
        }

        IsFlyoutOpen = true;
    }

    public void SelectColor(string hex)
    {
        IsFlyoutOpen = false;
        if (AppearanceEditorViewModel.TryParseHexColor(hex, out var validHex))
        {
            if (!string.Equals(_value, validHex, StringComparison.OrdinalIgnoreCase))
            {
                _onColorChanged(validHex);
            }
        }
    }

    public void Reset()
    {
        IsFlyoutOpen = false;
        _onReset();
    }

    public void UpdateState(string hex, bool hasCustom)
    {
        _isUpdatingInternally = true;
        try
        {
            if (AppearanceEditorViewModel.TryParseRgb(hex, out var a, out var r, out var g, out var b))
            {
                _a = a;
                _r = r;
                _g = g;
                _b = b;
                AppearanceEditorViewModel.RgbToHsv(r, g, b, out var h, out var s, out var v);
                if (s > 0.001)
                {
                    _hue = h;
                    OnPropertyChanged(nameof(Hue));
                    UpdateHueBrush();
                }
                _saturation = s;
                _brightness = v;
                OnPropertyChanged(nameof(Saturation));
                OnPropertyChanged(nameof(Brightness));

                OnPropertyChanged(nameof(R));
                OnPropertyChanged(nameof(G));
                OnPropertyChanged(nameof(B));
                OnPropertyChanged(nameof(RText));
                OnPropertyChanged(nameof(GText));
                OnPropertyChanged(nameof(BText));
            }

            Value = hex;
            Text = hex;
            HasCustom = hasCustom;
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PresetCardViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required Brush CardBackground { get; init; }
    public required Brush PrimaryDotBrush { get; init; }
    public required Brush SecondaryDotBrush { get; init; }
    public required CornerRadius CardCornerRadius { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AppearanceEditorViewModel : INotifyPropertyChanged
{
    private readonly DesignerViewModel _designer;
    private bool _isLoading;

    private string _selectedPresetId = AppearanceResolver.Fluid;
    private double _intensity = 1.0;
    private bool _motionEnabled = true;

    public ColorFieldViewModel AccentEditor { get; }
    public ColorFieldViewModel SecondaryAccentEditor { get; }
    public ColorFieldViewModel BackgroundEditor { get; }
    public ColorFieldViewModel SurfaceEditor { get; }
    public ColorFieldViewModel PrimaryTextEditor { get; }
    public ColorFieldViewModel SecondaryTextEditor { get; }

    public AppearanceEditorViewModel(DesignerViewModel designer)
    {
        _designer = designer ?? throw new ArgumentNullException(nameof(designer));

        AccentEditor = new ColorFieldViewModel(
            label: "Primary Accent",
            subLabel: null,
            resetLabel: "↺ Theme",
            resetTooltip: "Reset primary accent to inherited theme",
            onColorChanged: hex => SetAccent(hex),
            onReset: () => ResetAccent());

        SecondaryAccentEditor = new ColorFieldViewModel(
            label: "Secondary Accent",
            subLabel: null,
            resetLabel: "↺ Auto",
            resetTooltip: "Reset secondary accent to auto/inherited",
            onColorChanged: hex => SetSecondaryAccent(hex),
            onReset: () => ResetSecondaryAccent());

        BackgroundEditor = new ColorFieldViewModel(
            label: "Background",
            subLabel: "(Bar Base)",
            resetLabel: "↺ Auto",
            resetTooltip: "Reset background to inherited preset default",
            onColorChanged: hex => SetBackground(hex),
            onReset: () => ResetBackground());

        SurfaceEditor = new ColorFieldViewModel(
            label: "Surface",
            subLabel: "(Buttons & Tiles)",
            resetLabel: "↺ Auto",
            resetTooltip: "Reset surface to inherited preset default",
            onColorChanged: hex => SetSurface(hex),
            onReset: () => ResetSurface());

        PrimaryTextEditor = new ColorFieldViewModel(
            label: "Primary Text",
            subLabel: "(Labels & Values)",
            resetLabel: "↺ Auto",
            resetTooltip: "Reset primary text to inherited preset default",
            onColorChanged: hex => SetPrimaryText(hex),
            onReset: () => ResetPrimaryText());

        SecondaryTextEditor = new ColorFieldViewModel(
            label: "Secondary Text",
            subLabel: "(Muted & Units)",
            resetLabel: "↺ Auto",
            resetTooltip: "Reset secondary text to inherited preset default",
            onColorChanged: hex => SetSecondaryText(hex),
            onReset: () => ResetSecondaryText());

        WireEditorNotifications(AccentEditor, nameof(Accent), nameof(AccentText), nameof(AccentBrush), nameof(HasCustomAccent));
        WireEditorNotifications(SecondaryAccentEditor, nameof(SecondaryAccent), nameof(SecondaryAccentText), nameof(SecondaryAccentBrush), nameof(HasCustomSecondaryAccent));
        WireEditorNotifications(BackgroundEditor, nameof(Background), nameof(BackgroundText), nameof(BackgroundBrush), nameof(HasCustomBackground));
        WireEditorNotifications(SurfaceEditor, nameof(Surface), nameof(SurfaceText), nameof(SurfaceBrush), nameof(HasCustomSurface));
        WireEditorNotifications(PrimaryTextEditor, nameof(PrimaryText), nameof(PrimaryTextText), nameof(PrimaryTextBrush), nameof(HasCustomPrimaryText));
        WireEditorNotifications(SecondaryTextEditor, nameof(SecondaryText), nameof(SecondaryTextText), nameof(SecondaryTextBrush), nameof(HasCustomSecondaryText));

        // Initialize default fluid values
        AccentEditor.UpdateState("#FF7C5CFC", false);
        SecondaryAccentEditor.UpdateState("#FF7C5CFC", false);
        BackgroundEditor.UpdateState("#FF0A0A0D", false);
        SurfaceEditor.UpdateState("#FF1C1C24", false);
        PrimaryTextEditor.UpdateState("#FFF5F5F7", false);
        SecondaryTextEditor.UpdateState("#FFA1A1AA", false);

        Presets = new ObservableCollection<PresetCardViewModel>
        {
            new()
            {
                Id = AppearanceResolver.Fluid,
                DisplayName = "Fluid",
                Description = "Rounded tactile surface with single accent",
                CardBackground = CreateFrozenBrush("#FF14141A"),
                PrimaryDotBrush = CreateFrozenBrush("#FF7C5CFC"),
                SecondaryDotBrush = CreateFrozenBrush("#FF7C5CFC"),
                CardCornerRadius = new CornerRadius(8)
            },
            new()
            {
                Id = AppearanceResolver.Oled,
                DisplayName = "OLED",
                Description = "Pure black base with high contrast",
                CardBackground = CreateFrozenBrush("#FF000000"),
                PrimaryDotBrush = CreateFrozenBrush("#FFFFFFFF"),
                SecondaryDotBrush = CreateFrozenBrush("#FF888899"),
                CardCornerRadius = new CornerRadius(8)
            },
            new()
            {
                Id = AppearanceResolver.Cyber,
                DisplayName = "Cyber",
                Description = "Dual accent neon cyan & magenta edge",
                CardBackground = CreateFrozenBrush("#FF080A12"),
                PrimaryDotBrush = CreateFrozenBrush("#FF00F0FF"),
                SecondaryDotBrush = CreateFrozenBrush("#FFFF007F"),
                CardCornerRadius = new CornerRadius(6)
            },
            new()
            {
                Id = AppearanceResolver.Rgb,
                DisplayName = "RGB",
                Description = "Multi-accent spectrum identity",
                CardBackground = CreateFrozenBrush("#FF0C0A14"),
                PrimaryDotBrush = CreateFrozenBrush("#FFFF3366"),
                SecondaryDotBrush = CreateFrozenBrush("#FF00D4FF"),
                CardCornerRadius = new CornerRadius(8)
            },
            new()
            {
                Id = AppearanceResolver.Pixel,
                DisplayName = "Pixel",
                Description = "Arcade retro HUD, segmented tracks & hard outlines",
                CardBackground = CreateFrozenBrush("#FF0A0B10"),
                PrimaryDotBrush = CreateFrozenBrush("#FF00E5FF"),
                SecondaryDotBrush = CreateFrozenBrush("#FFFF0055"),
                CardCornerRadius = new CornerRadius(0)
            }
        };

        SelectPresetCommand = new EditorCommand(param =>
        {
            if (param is string id)
                SelectPreset(id);
        });

        ResetPresetCommand = new EditorCommand(_ => SelectPreset(AppearanceResolver.Fluid));
        ResetIntensityCommand = new EditorCommand(_ => SetIntensity(1.0));
        ResetMotionCommand = new EditorCommand(_ => SetMotion(true));
        ResetAppearanceCommand = new EditorCommand(_ => ResetAppearance());
        ClearBackgroundMediaCommand = new EditorCommand(_ => BackgroundMediaSource = "");
        ResetBackgroundMediaZoomCommand = new EditorCommand(_ => BackgroundMediaZoom = 1d);
        ResetBackgroundMediaHorizontalCommand = new EditorCommand(_ => BackgroundMediaOffsetX = 0d);
        ResetBackgroundMediaVerticalCommand = new EditorCommand(_ => BackgroundMediaOffsetY = 0d);
    }

    private void WireEditorNotifications(ColorFieldViewModel editor, string valProp, string textProp, string brushProp, string customProp)
    {
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ColorFieldViewModel.Value)) OnPropertyChanged(valProp);
            else if (e.PropertyName == nameof(ColorFieldViewModel.Text)) OnPropertyChanged(textProp);
            else if (e.PropertyName == nameof(ColorFieldViewModel.Brush)) OnPropertyChanged(brushProp);
            else if (e.PropertyName == nameof(ColorFieldViewModel.HasCustom)) OnPropertyChanged(customProp);
        };
    }

    public ObservableCollection<PresetCardViewModel> Presets { get; }

    public ICommand SelectPresetCommand { get; }
    public ICommand ResetPresetCommand { get; }
    public ICommand ResetAppearanceCommand { get; }
    public ICommand ResetIntensityCommand { get; }
    public ICommand ResetMotionCommand { get; }
    public ICommand ClearBackgroundMediaCommand { get; }
    public ICommand ResetBackgroundMediaZoomCommand { get; }
    public ICommand ResetBackgroundMediaHorizontalCommand { get; }
    public ICommand ResetBackgroundMediaVerticalCommand { get; }

    public ICommand SetAccentCommand => AccentEditor.SelectColorCommand;
    public ICommand ResetAccentCommand => AccentEditor.ResetCommand;

    public ICommand SetSecondaryAccentCommand => SecondaryAccentEditor.SelectColorCommand;
    public ICommand ResetSecondaryAccentCommand => SecondaryAccentEditor.ResetCommand;

    public ICommand SetBackgroundCommand => BackgroundEditor.SelectColorCommand;
    public ICommand ResetBackgroundCommand => BackgroundEditor.ResetCommand;

    public ICommand SetSurfaceCommand => SurfaceEditor.SelectColorCommand;
    public ICommand ResetSurfaceCommand => SurfaceEditor.ResetCommand;

    public ICommand SetPrimaryTextCommand => PrimaryTextEditor.SelectColorCommand;
    public ICommand ResetPrimaryTextCommand => PrimaryTextEditor.ResetCommand;

    public ICommand SetSecondaryTextCommand => SecondaryTextEditor.SelectColorCommand;
    public ICommand ResetSecondaryTextCommand => SecondaryTextEditor.ResetCommand;

    public string SelectedPresetId
    {
        get => _selectedPresetId;
        private set
        {
            if (SetField(ref _selectedPresetId, value))
            {
                foreach (var card in Presets)
                    card.IsSelected = string.Equals(card.Id, value, StringComparison.OrdinalIgnoreCase);
                OnPropertyChanged(nameof(SelectedPresetName));
            }
        }
    }

    public string SelectedPresetName =>
        Presets.FirstOrDefault(x => string.Equals(x.Id, SelectedPresetId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? "Fluid";

    public string Accent => AccentEditor.Value;
    public string AccentText
    {
        get => AccentEditor.Text;
        set => AccentEditor.Text = value;
    }
    public Brush AccentBrush => AccentEditor.Brush;
    public bool HasCustomAccent => AccentEditor.HasCustom;

    public string SecondaryAccent => SecondaryAccentEditor.Value;
    public string SecondaryAccentText
    {
        get => SecondaryAccentEditor.Text;
        set => SecondaryAccentEditor.Text = value;
    }
    public Brush SecondaryAccentBrush => SecondaryAccentEditor.Brush;
    public bool HasCustomSecondaryAccent => SecondaryAccentEditor.HasCustom;

    public string Background => BackgroundEditor.Value;
    public string BackgroundText
    {
        get => BackgroundEditor.Text;
        set => BackgroundEditor.Text = value;
    }
    public Brush BackgroundBrush => BackgroundEditor.Brush;
    public bool HasCustomBackground => BackgroundEditor.HasCustom;

    public string Surface => SurfaceEditor.Value;
    public string SurfaceText
    {
        get => SurfaceEditor.Text;
        set => SurfaceEditor.Text = value;
    }
    public Brush SurfaceBrush => SurfaceEditor.Brush;
    public bool HasCustomSurface => SurfaceEditor.HasCustom;

    public string PrimaryText => PrimaryTextEditor.Value;
    public string PrimaryTextText
    {
        get => PrimaryTextEditor.Text;
        set => PrimaryTextEditor.Text = value;
    }
    public Brush PrimaryTextBrush => PrimaryTextEditor.Brush;
    public bool HasCustomPrimaryText => PrimaryTextEditor.HasCustom;

    public string SecondaryText => SecondaryTextEditor.Value;
    public string SecondaryTextText
    {
        get => SecondaryTextEditor.Text;
        set => SecondaryTextEditor.Text = value;
    }
    public Brush SecondaryTextBrush => SecondaryTextEditor.Brush;
    public bool HasCustomSecondaryText => SecondaryTextEditor.HasCustom;

    public double Intensity
    {
        get => _intensity;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0.0, 1.0), 2);
            if (Math.Abs(_intensity - clamped) < 0.001) return;
            _intensity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IntensityPercent));

            if (_isLoading) return;
            EnsureAppearanceDefinition();
            _designer.GlobalAppearance.Appearance!.Intensity = clamped;
            _designer.OnAppearanceEdited();
        }
    }

    public int IntensityPercent
    {
        get => (int)Math.Round(Intensity * 100);
        set => Intensity = value / 100.0;
    }

    public bool MotionEnabled
    {
        get => _motionEnabled;
        set
        {
            if (SetField(ref _motionEnabled, value))
            {
                OnPropertyChanged(nameof(MotionLabel));
                if (_isLoading) return;
                EnsureAppearanceDefinition();
                _designer.GlobalAppearance.Appearance!.MotionEnabled = value;
                _designer.OnAppearanceEdited();
            }
        }
    }

    public string MotionLabel => MotionEnabled ? "On" : "Off";

    public string BackgroundMediaSource
    {
        get => _designer.GlobalAppearance.BackgroundMedia.Source ?? "";
        set
        {
            var normalized = value?.Trim() ?? "";
            if (string.Equals(BackgroundMediaSource, normalized, StringComparison.Ordinal)) return;
            _designer.GlobalAppearance.BackgroundMedia.Source = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundMediaResolvedSource));
            _designer.OnAppearanceEdited(notifyAppearance: false);
        }
    }

    public string? BackgroundMediaResolvedSource => _designer.ResolveMediaAsset(BackgroundMediaSource);

    public string BackgroundMediaFit
    {
        get => _designer.GlobalAppearance.BackgroundMedia.Fit;
        set
        {
            var normalized = value?.Trim().ToLowerInvariant() is "contain" or "fill" ? value.Trim().ToLowerInvariant() : "cover";
            if (string.Equals(BackgroundMediaFit, normalized, StringComparison.Ordinal)) return;
            _designer.GlobalAppearance.BackgroundMedia.Fit = normalized;
            OnPropertyChanged();
            _designer.OnAppearanceEdited(notifyAppearance: false);
        }
    }

    public bool BackgroundMediaLoop
    {
        get => _designer.GlobalAppearance.BackgroundMedia.Loop;
        set
        {
            if (BackgroundMediaLoop == value) return;
            _designer.GlobalAppearance.BackgroundMedia.Loop = value;
            OnPropertyChanged();
            _designer.OnAppearanceEdited(notifyAppearance: false);
        }
    }

    public bool BackgroundMediaEnabled
    {
        get => _designer.GlobalAppearance.BackgroundMedia.Enabled;
        set
        {
            if (BackgroundMediaEnabled == value) return;
            _designer.GlobalAppearance.BackgroundMedia.Enabled = value;
            OnPropertyChanged();
            _designer.OnAppearanceEdited(notifyAppearance: false);
        }
    }

    public double BackgroundMediaOpacity
    {
        get => _designer.GlobalAppearance.BackgroundMedia.Opacity;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Round(Math.Clamp(value, 0d, 1d), 2) : 1d;
            if (Math.Abs(BackgroundMediaOpacity - normalized) < 0.001) return;
            _designer.GlobalAppearance.BackgroundMedia.Opacity = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundMediaOpacityPercent));
            _designer.OnAppearanceEdited(deferSave: true, notifyAppearance: false);
        }
    }

    public int BackgroundMediaOpacityPercent => (int)Math.Round(BackgroundMediaOpacity * 100);

    public double BackgroundMediaOffsetX
    {
        get => _designer.GlobalAppearance.BackgroundMedia.OffsetX;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Round(Math.Clamp(value, -4096d, 4096d)) : 0d;
            if (Math.Abs(BackgroundMediaOffsetX - normalized) < 0.001) return;
            _designer.GlobalAppearance.BackgroundMedia.OffsetX = normalized;
            OnPropertyChanged();
            _designer.OnAppearanceEdited(deferSave: true, notifyAppearance: false);
        }
    }

    public double BackgroundMediaOffsetY
    {
        get => _designer.GlobalAppearance.BackgroundMedia.OffsetY;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Round(Math.Clamp(value, -4096d, 4096d)) : 0d;
            if (Math.Abs(BackgroundMediaOffsetY - normalized) < 0.001) return;
            _designer.GlobalAppearance.BackgroundMedia.OffsetY = normalized;
            OnPropertyChanged();
            _designer.OnAppearanceEdited(deferSave: true, notifyAppearance: false);
        }
    }

    public double BackgroundMediaZoom
    {
        get => _designer.GlobalAppearance.BackgroundMedia.Zoom;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Round(Math.Clamp(value, 0.1d, 4d), 2) : 1d;
            if (Math.Abs(BackgroundMediaZoom - normalized) < 0.001) return;
            _designer.GlobalAppearance.BackgroundMedia.Zoom = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundMediaZoomPercent));
            _designer.OnAppearanceEdited(deferSave: true, notifyAppearance: false);
        }
    }

    public int BackgroundMediaZoomPercent => (int)Math.Round(BackgroundMediaZoom * 100);

    public void LoadFromSettings(GlobalAppearanceSettings settings)
    {
        _isLoading = true;
        try
        {
            var app = settings.Appearance;
            var theme = settings.Theme ?? new ThemeDefinition();

            SelectedPresetId = string.IsNullOrWhiteSpace(app?.Preset)
                ? AppearanceResolver.Fluid
                : app.Preset.Trim().ToLowerInvariant();

            var defaults = AppearanceResolver.GetPresetDefaults(SelectedPresetId);

            var hasCustomAccent = !string.IsNullOrWhiteSpace(app?.Accent);
            var effectiveAccent = hasCustomAccent ? app!.Accent! : (string.IsNullOrWhiteSpace(theme.Accent) ? defaults.Accent : theme.Accent);
            AccentEditor.UpdateState(effectiveAccent, hasCustomAccent);

            var hasCustomSecAccent = !string.IsNullOrWhiteSpace(app?.SecondaryAccent);
            var effectiveSecAccent = hasCustomSecAccent ? app!.SecondaryAccent! : (string.IsNullOrWhiteSpace(app?.Accent) ? defaults.SecondaryAccent : effectiveAccent);
            SecondaryAccentEditor.UpdateState(effectiveSecAccent, hasCustomSecAccent);

            var hasCustomBg = IsCustomThemeColor(theme.Background, "#FF0E0E11", "#FF0A0A0D");
            var effectiveBg = AppearanceResolver.GetEffectiveColor(theme.Background, defaults.Base, SelectedPresetId == AppearanceResolver.Fluid, "#FF0E0E11", "#FF0A0A0D");
            BackgroundEditor.UpdateState(effectiveBg, hasCustomBg);

            var hasCustomSurf = IsCustomThemeColor(theme.TileBackground, "#FF222228", "#FF1C1C24");
            var effectiveSurf = AppearanceResolver.GetEffectiveColor(theme.TileBackground, defaults.Control, SelectedPresetId == AppearanceResolver.Fluid, "#FF222228", "#FF1C1C24");
            SurfaceEditor.UpdateState(effectiveSurf, hasCustomSurf);

            var hasCustomPri = IsCustomThemeColor(theme.PrimaryText, "#E6F5F5F7", "#FFF5F5F7");
            var effectivePriTxt = AppearanceResolver.GetEffectiveColor(theme.PrimaryText, defaults.TextPrimary, SelectedPresetId == AppearanceResolver.Fluid, "#E6F5F5F7", "#FFF5F5F7");
            PrimaryTextEditor.UpdateState(effectivePriTxt, hasCustomPri);

            var hasCustomSec = IsCustomThemeColor(theme.SecondaryText, "#80F5F5F7", "#FFA1A1AA");
            var effectiveSecTxt = AppearanceResolver.GetEffectiveColor(theme.SecondaryText, defaults.TextSecondary, SelectedPresetId == AppearanceResolver.Fluid, "#80F5F5F7", "#FFA1A1AA");
            SecondaryTextEditor.UpdateState(effectiveSecTxt, hasCustomSec);

            Intensity = app?.Intensity ?? 1.0;
            MotionEnabled = app?.MotionEnabled ?? true;
            OnPropertyChanged(nameof(BackgroundMediaSource));
            OnPropertyChanged(nameof(BackgroundMediaResolvedSource));
            OnPropertyChanged(nameof(BackgroundMediaFit));
            OnPropertyChanged(nameof(BackgroundMediaLoop));
            OnPropertyChanged(nameof(BackgroundMediaEnabled));
            OnPropertyChanged(nameof(BackgroundMediaOpacity));
            OnPropertyChanged(nameof(BackgroundMediaOpacityPercent));
            OnPropertyChanged(nameof(BackgroundMediaOffsetX));
            OnPropertyChanged(nameof(BackgroundMediaOffsetY));
            OnPropertyChanged(nameof(BackgroundMediaZoom));
            OnPropertyChanged(nameof(BackgroundMediaZoomPercent));
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void SelectPreset(string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId)) return;
        presetId = presetId.Trim().ToLowerInvariant();

        if (string.Equals(SelectedPresetId, presetId, StringComparison.OrdinalIgnoreCase))
            return; // Selecting already-selected preset does NOT dirty it (Req 15)

        EnsureAppearanceDefinition();
        _designer.GlobalAppearance.Appearance!.Preset = presetId;
        SelectedPresetId = presetId;

        var defaults = AppearanceResolver.GetPresetDefaults(presetId);
        _isLoading = true;
        if (!HasCustomBackground)
            BackgroundEditor.UpdateState(defaults.Base, false);
        if (!HasCustomSurface)
            SurfaceEditor.UpdateState(defaults.Control, false);
        if (!HasCustomPrimaryText)
            PrimaryTextEditor.UpdateState(defaults.TextPrimary, false);
        if (!HasCustomSecondaryText)
            SecondaryTextEditor.UpdateState(defaults.TextSecondary, false);
        if (!HasCustomAccent)
            AccentEditor.UpdateState(defaults.Accent, false);
        if (!HasCustomSecondaryAccent)
            SecondaryAccentEditor.UpdateState(defaults.SecondaryAccent, false);
        _isLoading = false;

        CheckCleanAppearance();
        _designer.OnAppearanceEdited();
    }

    public void SetAccent(string hex)
    {
        if (TryParseHexColor(hex, out var validHex))
        {
            if (_isLoading)
            {
                AccentEditor.UpdateState(validHex, HasCustomAccent);
                return;
            }

            if (!string.Equals(Accent, validHex, StringComparison.OrdinalIgnoreCase))
            {
                EnsureAppearanceDefinition();
                _designer.GlobalAppearance.Appearance!.Accent = validHex;
                AccentEditor.UpdateState(validHex, true);
                _designer.OnAppearanceEdited();
            }
        }
    }

    public void ResetAccent()
    {
        if (_designer.GlobalAppearance.Appearance is not null)
        {
            _designer.GlobalAppearance.Appearance.Accent = null;
            var themeAccent = _designer.GlobalAppearance.Theme.Accent ?? "#FF7C5CFC";
            _isLoading = true;
            AccentEditor.UpdateState(themeAccent, false);
            if (!HasCustomSecondaryAccent)
            {
                SecondaryAccentEditor.UpdateState(themeAccent, false);
            }
            _isLoading = false;
            CheckCleanAppearance();
            _designer.OnAppearanceEdited();
        }
    }

    public void SetSecondaryAccent(string hex)
    {
        if (TryParseHexColor(hex, out var validHex))
        {
            if (_isLoading)
            {
                SecondaryAccentEditor.UpdateState(validHex, HasCustomSecondaryAccent);
                return;
            }

            if (!string.Equals(SecondaryAccent, validHex, StringComparison.OrdinalIgnoreCase))
            {
                EnsureAppearanceDefinition();
                _designer.GlobalAppearance.Appearance!.SecondaryAccent = validHex;
                SecondaryAccentEditor.UpdateState(validHex, true);
                _designer.OnAppearanceEdited();
            }
        }
    }

    public void ResetSecondaryAccent()
    {
        if (_designer.GlobalAppearance.Appearance is not null)
        {
            _designer.GlobalAppearance.Appearance.SecondaryAccent = null;
            _isLoading = true;
            SecondaryAccentEditor.UpdateState(Accent, false);
            _isLoading = false;
            CheckCleanAppearance();
            _designer.OnAppearanceEdited();
        }
    }

    public void SetIntensity(double val) => Intensity = val;

    public void SetMotion(bool enabled) => MotionEnabled = enabled;

    public void SetBackground(string hex)
    {
        if (TryParseHexColor(hex, out var validHex))
        {
            if (_isLoading)
            {
                BackgroundEditor.UpdateState(validHex, HasCustomBackground);
                return;
            }

            if (!string.Equals(Background, validHex, StringComparison.OrdinalIgnoreCase))
            {
                EnsureThemeDefinition();
                _designer.GlobalAppearance.Theme.Background = validHex;
                BackgroundEditor.UpdateState(validHex, true);
                _designer.OnAppearanceEdited();
            }
        }
    }

    public void ResetBackground()
    {
        EnsureThemeDefinition();
        _designer.GlobalAppearance.Theme.Background = null;
        var defaults = AppearanceResolver.GetPresetDefaults(SelectedPresetId);
        _isLoading = true;
        BackgroundEditor.UpdateState(defaults.Base, false);
        _isLoading = false;
        _designer.OnAppearanceEdited();
    }

    public void SetSurface(string hex)
    {
        if (TryParseHexColor(hex, out var validHex))
        {
            if (_isLoading)
            {
                SurfaceEditor.UpdateState(validHex, HasCustomSurface);
                return;
            }

            if (!string.Equals(Surface, validHex, StringComparison.OrdinalIgnoreCase))
            {
                EnsureThemeDefinition();
                _designer.GlobalAppearance.Theme.TileBackground = validHex;
                SurfaceEditor.UpdateState(validHex, true);
                _designer.OnAppearanceEdited();
            }
        }
    }

    public void ResetSurface()
    {
        EnsureThemeDefinition();
        _designer.GlobalAppearance.Theme.TileBackground = null;
        var defaults = AppearanceResolver.GetPresetDefaults(SelectedPresetId);
        _isLoading = true;
        SurfaceEditor.UpdateState(defaults.Control, false);
        _isLoading = false;
        _designer.OnAppearanceEdited();
    }

    public void SetPrimaryText(string hex)
    {
        if (TryParseHexColor(hex, out var validHex))
        {
            if (_isLoading)
            {
                PrimaryTextEditor.UpdateState(validHex, HasCustomPrimaryText);
                return;
            }

            if (!string.Equals(PrimaryText, validHex, StringComparison.OrdinalIgnoreCase))
            {
                EnsureThemeDefinition();
                _designer.GlobalAppearance.Theme.PrimaryText = validHex;
                PrimaryTextEditor.UpdateState(validHex, true);
                _designer.OnAppearanceEdited();
            }
        }
    }

    public void ResetPrimaryText()
    {
        EnsureThemeDefinition();
        _designer.GlobalAppearance.Theme.PrimaryText = null;
        var defaults = AppearanceResolver.GetPresetDefaults(SelectedPresetId);
        _isLoading = true;
        PrimaryTextEditor.UpdateState(defaults.TextPrimary, false);
        _isLoading = false;
        _designer.OnAppearanceEdited();
    }

    public void SetSecondaryText(string hex)
    {
        if (TryParseHexColor(hex, out var validHex))
        {
            if (_isLoading)
            {
                SecondaryTextEditor.UpdateState(validHex, HasCustomSecondaryText);
                return;
            }

            if (!string.Equals(SecondaryText, validHex, StringComparison.OrdinalIgnoreCase))
            {
                EnsureThemeDefinition();
                _designer.GlobalAppearance.Theme.SecondaryText = validHex;
                SecondaryTextEditor.UpdateState(validHex, true);
                _designer.OnAppearanceEdited();
            }
        }
    }

    public void ResetSecondaryText()
    {
        EnsureThemeDefinition();
        _designer.GlobalAppearance.Theme.SecondaryText = null;
        var defaults = AppearanceResolver.GetPresetDefaults(SelectedPresetId);
        _isLoading = true;
        SecondaryTextEditor.UpdateState(defaults.TextSecondary, false);
        _isLoading = false;
        _designer.OnAppearanceEdited();
    }

    public void ResetAppearance()
    {
        _designer.GlobalAppearance.Appearance = null;
        LoadFromSettings(_designer.GlobalAppearance);
        _designer.OnAppearanceEdited();
    }

    private void EnsureAppearanceDefinition()
    {
        _designer.GlobalAppearance.Appearance ??= new AppearanceDefinition();
    }

    private void EnsureThemeDefinition()
    {
        _designer.GlobalAppearance.Theme ??= new ThemeDefinition();
    }

    private static bool IsCustomThemeColor(string? val, string legacy1, string legacy2)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        if (string.Equals(val, legacy1, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, legacy2, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void CheckCleanAppearance()
    {
        if (_designer.GlobalAppearance.Appearance is null) return;
        var app = _designer.GlobalAppearance.Appearance;

        bool isDefaultPreset = string.IsNullOrWhiteSpace(app.Preset) ||
                               string.Equals(app.Preset, AppearanceResolver.Fluid, StringComparison.OrdinalIgnoreCase);
        bool isDefaultAccent = string.IsNullOrWhiteSpace(app.Accent);
        bool isDefaultSecondary = string.IsNullOrWhiteSpace(app.SecondaryAccent);
        bool isDefaultIntensity = app.Intensity is null || Math.Abs(app.Intensity.Value - 1.0) < 0.001;
        bool isDefaultMotion = app.MotionEnabled is null || app.MotionEnabled.Value;

        if (isDefaultPreset && isDefaultAccent && isDefaultSecondary && isDefaultIntensity && isDefaultMotion &&
            (app.ExtensionData is null || app.ExtensionData.Count == 0))
        {
            _designer.GlobalAppearance.Appearance = null;
        }
    }

    public static bool TryParseHexColor(string? input, out string canonicalHex)
    {
        canonicalHex = "";
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;

        if (text.Length is not (7 or 9))
            return false;

        for (int i = 1; i < text.Length; i++)
        {
            char c = text[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }

        canonicalHex = text.Length == 7
            ? $"#FF{text[1..].ToUpperInvariant()}"
            : text.ToUpperInvariant();
        return true;
    }

    public static bool TryParseRgb(string? hex, out byte a, out byte r, out byte g, out byte b)
    {
        a = 0xFF;
        r = 0;
        g = 0;
        b = 0;

        if (!TryParseHexColor(hex, out var canonicalHex))
            return false;

        try
        {
            a = Convert.ToByte(canonicalHex.Substring(1, 2), 16);
            r = Convert.ToByte(canonicalHex.Substring(3, 2), 16);
            g = Convert.ToByte(canonicalHex.Substring(5, 2), 16);
            b = Convert.ToByte(canonicalHex.Substring(7, 2), 16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        h = Math.Clamp(h, 0.0, 360.0);
        if (h >= 360.0) h = 0.0;
        s = Math.Clamp(s, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);

        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = v - c;

        double r1, g1, b1;
        if (h < 60.0)
        {
            r1 = c; g1 = x; b1 = 0;
        }
        else if (h < 120.0)
        {
            r1 = x; g1 = c; b1 = 0;
        }
        else if (h < 180.0)
        {
            r1 = 0; g1 = c; b1 = x;
        }
        else if (h < 240.0)
        {
            r1 = 0; g1 = x; b1 = c;
        }
        else if (h < 300.0)
        {
            r1 = x; g1 = 0; b1 = c;
        }
        else
        {
            r1 = c; g1 = 0; b1 = x;
        }

        r = (byte)Math.Clamp(Math.Round((r1 + m) * 255.0), 0, 255);
        g = (byte)Math.Clamp(Math.Round((g1 + m) * 255.0), 0, 255);
        b = (byte)Math.Clamp(Math.Round((b1 + m) * 255.0), 0, 255);
    }

    public static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rf = r / 255.0;
        double gf = g / 255.0;
        double bf = b / 255.0;

        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;

        v = max;
        s = max <= 0.00001 ? 0 : delta / max;

        if (delta <= 0.00001)
        {
            h = 0;
        }
        else if (Math.Abs(max - rf) < 0.00001)
        {
            h = 60.0 * (((gf - bf) / delta) % 6.0);
        }
        else if (Math.Abs(max - gf) < 0.00001)
        {
            h = 60.0 * (((bf - rf) / delta) + 2.0);
        }
        else
        {
            h = 60.0 * (((rf - gf) / delta) + 4.0);
        }

        if (h < 0)
        {
            h += 360.0;
        }

        h = Math.Clamp(Math.Round(h, 1), 0.0, 360.0);
        s = Math.Clamp(Math.Round(s, 3), 0.0, 1.0);
        v = Math.Clamp(Math.Round(v, 3), 0.0, 1.0);
    }

    internal static Brush CreateFrozenBrush(string hex)
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
            return Brushes.Transparent;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
