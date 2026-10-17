using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.ViewModels;

public abstract class BarItemViewModel
{
    public string Id { get; init; } = "";
    public double Width { get; set; } = 84;
    public ImageSource? AssetIcon { get; set; }
    public bool HasAssetIcon => AssetIcon is not null;
}

public abstract class ObservableBarItemViewModel : BarItemViewModel, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public static class GlyphHelper
{
    public static string Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();

        if (trimmed.StartsWith("&#x", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(";"))
        {
            var hex = trimmed.Substring(3, trimmed.Length - 4);
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var codePoint) &&
                codePoint >= 0x20 && codePoint <= 0x10FFFF)
                return char.ConvertFromUtf32(codePoint);
        }
        else if (trimmed.StartsWith(@"\u", StringComparison.OrdinalIgnoreCase) && trimmed.Length == 6)
        {
            var hex = trimmed.Substring(2);
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var codePoint) &&
                codePoint >= 0x20 && codePoint <= 0x10FFFF)
                return char.ConvertFromUtf32(codePoint);
        }
        else if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && (trimmed.Length == 6 || trimmed.Length == 7))
        {
            var hex = trimmed.Substring(2);
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var codePoint) &&
                codePoint >= 0x20 && codePoint <= 0x10FFFF)
                return char.ConvertFromUtf32(codePoint);
        }
        else if ((trimmed.Length == 4 || trimmed.Length == 5) &&
                 int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var codePoint) &&
                 codePoint >= 0x20 && codePoint <= 0x10FFFF)
        {
            return char.ConvertFromUtf32(codePoint);
        }

        return trimmed;
    }
}

internal sealed class InteractionValueWriter
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(33);
    private readonly DispatcherTimer _timer;
    private Action<double>? _setter;
    private double _value;

    public InteractionValueWriter()
    {
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Flush();
    }

    public void Queue(Action<double>? setter, double value)
    {
        if (setter is null)
            return;

        _setter = setter;
        _value = value;
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Flush()
    {
        _timer.Stop();
        var setter = _setter;
        _setter = null;
        setter?.Invoke(_value);
    }
}

public sealed class ButtonItemViewModel : ObservableBarItemViewModel
{
    public bool IsEmoji { get; init; }

    private string _label = "";
    private string? _icon;
    private string? _iconKind;
    private ICommand _command = null!;
    private ICommand? _secondaryCommand;
    private int _longPressMilliseconds = 650;

    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value))
                OnPropertyChanged(nameof(IconFallbackLabel));
        }
    }

    public string? Icon
    {
        get => _icon;
        set
        {
            if (SetField(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public string? IconKind
    {
        get => _iconKind;
        set
        {
            if (SetField(ref _iconKind, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public bool HasGlyphIcon => !string.Equals(_iconKind, "asset", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(_iconKind, "none", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(_icon) &&
                                !string.IsNullOrWhiteSpace(GlyphHelper.Resolve(_icon));

    public bool HasIcon => HasAssetIcon || HasGlyphIcon;
    public string DisplayGlyph => HasGlyphIcon ? GlyphHelper.Resolve(_icon) : "";
    public string IconFallbackLabel => Label;

    public ICommand Command
    {
        get => _command;
        set => SetField(ref _command, value);
    }

    public ICommand? SecondaryCommand
    {
        get => _secondaryCommand;
        set
        {
            if (SetField(ref _secondaryCommand, value))
                OnPropertyChanged(nameof(HasSecondaryAction));
        }
    }

    public int LongPressMilliseconds
    {
        get => _longPressMilliseconds;
        set => SetField(ref _longPressMilliseconds, value);
    }

    public bool HasSecondaryAction => SecondaryCommand is not null;
}

public sealed class SpacerItemViewModel : BarItemViewModel
{
}

public class SliderItemViewModel : ObservableBarItemViewModel
{
    private readonly InteractionValueWriter _valueWriter = new();
    private Action<double>? _setter;
    private double _value;
    private string _label = "";
    private double _minimum;
    private double _maximum = 100;
    private bool _isInteracting;

    public Action<double>? Setter
    {
        get => _setter;
        set => _setter = value;
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value))
            {
                OnPropertyChanged(nameof(DisplayGlyph));
                OnPropertyChanged(nameof(IconFallbackLabel));
            }
        }
    }

    public double Minimum
    {
        get => _minimum;
        set => SetField(ref _minimum, value);
    }

    public double Maximum
    {
        get => _maximum;
        set => SetField(ref _maximum, value);
    }

    public double Value
    {
        get => _value;
        set
        {
            if (Math.Abs(_value - value) < 0.01)
                return;

            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValuePercentText));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(DisplayGlyph));
            if (_isInteracting)
                _valueWriter.Queue(_setter, value);
            else
                _setter?.Invoke(value);
        }
    }

    public bool IsInteracting
    {
        get => _isInteracting;
        set
        {
            if (!SetField(ref _isInteracting, value))
                return;
            if (!value)
                _valueWriter.Flush();
        }
    }

    public string ValuePercentText => $"{(int)Math.Round(_value)}%";

    public string Glyph => _value switch
    {
        <= 0.01 => "\uE74F",
        < 33.0  => "\uE993",
        < 66.0  => "\uE994",
        _       => "\uE767"
    };

    private string? _icon;
    private string? _iconKind;

    public string? Icon
    {
        get => _icon;
        set
        {
            if (SetField(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public string? IconKind
    {
        get => _iconKind;
        set
        {
            if (SetField(ref _iconKind, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    private bool HasConfiguredGlyph => !string.Equals(_iconKind, "asset", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(_iconKind, "none", StringComparison.OrdinalIgnoreCase) &&
                                       !string.IsNullOrWhiteSpace(_icon) &&
                                       !string.IsNullOrWhiteSpace(GlyphHelper.Resolve(_icon));

    public bool HasIcon => HasAssetIcon || HasConfiguredGlyph;
    public bool HasGlyphIcon => !string.Equals(_iconKind, "asset", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(DisplayGlyph);
    public string IconFallbackLabel => Label;

    public string DisplayGlyph
    {
        get
        {
            if (HasConfiguredGlyph)
                return GlyphHelper.Resolve(_icon);

            var label = _label?.Trim().ToLowerInvariant() ?? "";
            if (label.Contains("bright"))
                return "\uE706";
            if (label.Contains("mic"))
                return "\uE720";

            return Glyph;
        }
    }

    public SliderItemViewModel(
        string label,
        double width,
        double minimum,
        double maximum,
        double initialValue,
        Action<double>? setter)
    {
        Label = label;
        Width = width;
        Minimum = minimum;
        Maximum = maximum;
        _value = initialValue;
        _setter = setter;
    }
}

public sealed class ExpandableSliderItemViewModel : SliderItemViewModel
{
    public ExpandableSliderItemViewModel(
        string label,
        double width,
        double minimum,
        double maximum,
        double initialValue,
        Action<double>? setter,
        string? icon,
        bool isEnabled)
        : base(label, width, minimum, maximum, initialValue, setter)
    {
        Icon = !string.IsNullOrWhiteSpace(icon)
            ? GlyphHelper.Resolve(icon)
            : DeriveDefaultIcon(label);
        IsEnabled = isEnabled;
    }

    private static string DeriveDefaultIcon(string? label)
    {
        var text = label?.Trim().ToLowerInvariant() ?? "";
        if (text.Contains("vol") || text.Contains("sound"))
            return "\uE767";
        if (text.Contains("bright"))
            return "\uE706";
        if (text.Contains("mic"))
            return "\uE720";
        return !string.IsNullOrWhiteSpace(label) ? label.Trim()[0].ToString() : "S";
    }

    public bool IsEnabled { get; }
}

public sealed class MasterVolumeItemViewModel : SliderItemViewModel
{
    public MasterVolumeItemViewModel(
        string label,
        double width,
        double minimum,
        double maximum,
        double initialValue,
        Action<double>? setter)
        : base(label, width, minimum, maximum, initialValue, setter)
    {
    }
}

public sealed class ToggleItemViewModel : ObservableBarItemViewModel
{
    private bool _isChecked;
    private string _label = "";
    private string? _onLabel;
    private string? _offLabel;
    private string? _icon;
    private string? _iconKind;
    private ICommand _toggleCommand = null!;
    private ICommand? _secondaryCommand;
    private int _longPressMilliseconds = 650;

    public string? Icon
    {
        get => _icon;
        set
        {
            if (SetField(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public string? IconKind
    {
        get => _iconKind;
        set
        {
            if (SetField(ref _iconKind, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public bool HasGlyphIcon => !string.Equals(_iconKind, "asset", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(_iconKind, "none", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(_icon) &&
                                !string.IsNullOrWhiteSpace(GlyphHelper.Resolve(_icon));

    public bool HasIcon => HasAssetIcon || HasGlyphIcon;
    public string DisplayGlyph => HasGlyphIcon ? GlyphHelper.Resolve(_icon) : "";
    public string IconFallbackLabel => DisplayLabel;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(IconFallbackLabel));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(IconFallbackLabel));
            }
        }
    }

    public string? OnLabel
    {
        get => _onLabel;
        set
        {
            if (SetField(ref _onLabel, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(IconFallbackLabel));
            }
        }
    }

    public string? OffLabel
    {
        get => _offLabel;
        set
        {
            if (SetField(ref _offLabel, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(IconFallbackLabel));
            }
        }
    }

    public string DisplayLabel => _isChecked ? (_onLabel ?? _label) : (_offLabel ?? _label);

    public ICommand ToggleCommand
    {
        get => _toggleCommand;
        set => SetField(ref _toggleCommand, value);
    }

    public ICommand? SecondaryCommand
    {
        get => _secondaryCommand;
        set
        {
            if (SetField(ref _secondaryCommand, value))
                OnPropertyChanged(nameof(HasSecondaryAction));
        }
    }

    public int LongPressMilliseconds
    {
        get => _longPressMilliseconds;
        set => SetField(ref _longPressMilliseconds, value);
    }

    public bool HasSecondaryAction => SecondaryCommand is not null;
}

public sealed class MultiStateItemViewModel : ObservableBarItemViewModel
{
    private int _currentIndex;
    private IReadOnlyList<WidgetStateDefinition> _states = Array.Empty<WidgetStateDefinition>();
    private ICommand _cycleCommand = null!;

    public IReadOnlyList<WidgetStateDefinition> States
    {
        get => _states;
        set
        {
            _states = value ?? Array.Empty<WidgetStateDefinition>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentState));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(HasGlyph));
            OnPropertyChanged(nameof(DisplayGlyph));
        }
    }

    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (SetField(ref _currentIndex, value))
            {
                OnPropertyChanged(nameof(CurrentState));
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(HasGlyph));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public WidgetStateDefinition? CurrentState =>
        _states.Count > 0 && _currentIndex >= 0 && _currentIndex < _states.Count
            ? _states[_currentIndex]
            : null;

    public bool HasGlyph => !string.IsNullOrWhiteSpace(CurrentState?.Glyph);
    public string DisplayGlyph => GlyphHelper.Resolve(CurrentState?.Glyph);

    public string DisplayLabel => CurrentState?.Label ?? "";

    public ICommand CycleCommand
    {
        get => _cycleCommand;
        set => SetField(ref _cycleCommand, value);
    }
}

public sealed class SensorItemViewModel : ObservableBarItemViewModel
{
    private string _label = "";
    private string? _icon;
    private string? _iconKind;
    private double? _value;
    private string _metric = "";
    private string _unit = "";
    private string _format = "{0}";
    private string _status = "normal";

    public string? Icon
    {
        get => _icon;
        set
        {
            if (SetField(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public string? IconKind
    {
        get => _iconKind;
        set
        {
            if (SetField(ref _iconKind, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasGlyphIcon));
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    private bool HasConfiguredGlyph => !string.Equals(_iconKind, "asset", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(_iconKind, "none", StringComparison.OrdinalIgnoreCase) &&
                                       !string.IsNullOrWhiteSpace(_icon) &&
                                       !string.IsNullOrWhiteSpace(GlyphHelper.Resolve(_icon));

    public bool HasIcon => HasAssetIcon || HasConfiguredGlyph;
    public bool HasGlyphIcon => !string.Equals(_iconKind, "asset", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(Glyph);
    public string DisplayGlyph => HasGlyphIcon ? Glyph : "";
    public string IconFallbackLabel => Label;

    public string Glyph
    {
        get
        {
            if (HasConfiguredGlyph)
                return GlyphHelper.Resolve(_icon);

            var metric = (_metric + " " + _label).ToLowerInvariant();
            if (metric.Contains("temp"))
                return "\uE7A3";
            if (metric.Contains("cpu") || metric.Contains("proc"))
                return "\uE950";
            if (metric.Contains("gpu") || metric.Contains("display") || metric.Contains("vram"))
                return "\uE7F4";
            if (metric.Contains("ram") || metric.Contains("mem"))
                return "\uE964";
            if (metric.Contains("fan") || metric.Contains("rpm"))
                return "\uE9D9";
            if (metric.Contains("net") || metric.Contains("wifi"))
                return "\uE839";
            if (metric.Contains("batt") || metric.Contains("power"))
                return "\uE83F";

            return "\uE950";
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value))
            {
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(DisplayGlyph));
                OnPropertyChanged(nameof(IconFallbackLabel));
            }
        }
    }

    public double? Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
                OnPropertyChanged(nameof(ValueText));
        }
    }

    public string Metric
    {
        get => _metric;
        set
        {
            if (SetField(ref _metric, value))
            {
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (SetField(ref _unit, value))
                OnPropertyChanged(nameof(ValueText));
        }
    }

    public string Format
    {
        get => _format;
        set
        {
            if (SetField(ref _format, value))
                OnPropertyChanged(nameof(ValueText));
        }
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string ValueText
    {
        get
        {
            if (!_value.HasValue) return "--";
            var formatted = string.Format(_format, _value.Value);
            if (string.IsNullOrEmpty(_unit)) return formatted;
            if (_unit.StartsWith("%") || _unit.StartsWith("°"))
                return $"{formatted}{_unit}";
            return $"{formatted} {_unit}";
        }
    }
}

public sealed class AudioSessionItemViewModel : ObservableBarItemViewModel
{
    private const double VolumeAckTolerance = 1.0;
    private static readonly TimeSpan PendingReadbackWindow = TimeSpan.FromMilliseconds(750);
    private string _label = "";
    private double _value;
    private double _minimum = 0;
    private double _maximum = 100;
    private bool _isMuted;
    private Action<double>? _valueSetter;
    private Action<bool>? _muteSetter;
    private ICommand _toggleMuteCommand = null!;
    private bool _isInternalUpdate;
    private bool _isInteracting;
    private readonly InteractionValueWriter _valueWriter = new();
    private double? _pendingValue;
    private DateTime _pendingSinceUtc;

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public double Minimum
    {
        get => _minimum;
        set => SetField(ref _minimum, value);
    }

    public double Maximum
    {
        get => _maximum;
        set => SetField(ref _maximum, value);
    }

    public double Value
    {
        get => _value;
        set
        {
            if (Math.Abs(_value - value) < 0.01)
                return;

            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValuePercentText));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(DisplayGlyph));

            if (!_isInternalUpdate)
            {
                if (_isInteracting)
                    _valueWriter.Queue(_valueSetter, value);
                else
                    _valueSetter?.Invoke(value);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetField(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(DisplayGlyph));
            }
        }
    }

    public bool IsInteracting
    {
        get => _isInteracting;
        set
        {
            if (!SetField(ref _isInteracting, value)) return;
            if (!value)
            {
                _valueWriter.Flush();
                _pendingValue = _value;
                _pendingSinceUtc = DateTime.UtcNow;
            }
        }
    }

    public string ValuePercentText => $"{(int)Math.Round(_value)}%";

    public string Glyph => _value switch
    {
        <= 0.01 => "\uE74F",
        < 33.0  => "\uE993",
        < 66.0  => "\uE994",
        _       => "\uE767"
    };

    public string DisplayGlyph => _isMuted ? "\uE74F" : Glyph;

    public string AppGlyph
    {
        get
        {
            var label = _label?.Trim().ToLowerInvariant() ?? "";
            if (label.Contains("spotify") || label.Contains("music"))
                return "\uE8D6";
            if (label.Contains("chrome") || label.Contains("edge") || label.Contains("brave") || label.Contains("browser") || label.Contains("opera"))
                return "\uE774";
            if (label.Contains("discord") || label.Contains("chat") || label.Contains("slack") || label.Contains("teams"))
                return "\uE720";
            if (label.Contains("game") || label.Contains("steam"))
                return "\uE7FC";
            if (label.Contains("system"))
                return "\uE770";

            return "\uE71D";
        }
    }

    public ICommand ToggleMuteCommand
    {
        get => _toggleMuteCommand;
        set => SetField(ref _toggleMuteCommand, value);
    }

    public Action<double>? ValueSetter
    {
        get => _valueSetter;
        set => _valueSetter = value;
    }

    public Action<bool>? MuteSetter
    {
        get => _muteSetter;
        set => _muteSetter = value;
    }

    public AudioSessionItemViewModel(
        string label,
        double width,
        double minimum,
        double maximum,
        double initialValue,
        bool isMuted,
        Action<double>? valueSetter,
        Action<bool>? muteSetter)
    {
        Label = label;
        Width = width;
        Minimum = minimum;
        Maximum = maximum;
        _value = initialValue;
        _isMuted = isMuted;
        _valueSetter = valueSetter;
        _muteSetter = muteSetter;

        ToggleMuteCommand = new DelegateCommand(() =>
        {
            IsMuted = !IsMuted;
            _muteSetter?.Invoke(IsMuted);
        });
    }

    public void UpdateFromSnapshot(DynamicItemSnapshot snapshot)
    {
        Width = snapshot.Width;
        Label = snapshot.Label;
        Minimum = snapshot.Min;
        Maximum = snapshot.Max;
        IsMuted = snapshot.IsChecked;
        _valueSetter = snapshot.ValueSetter;
        _muteSetter = snapshot.OnToggle;

        if (_isInteracting)
            return;

        if (_pendingValue.HasValue)
        {
            if (Math.Abs(_pendingValue.Value - snapshot.Value) < VolumeAckTolerance)
            {
                _pendingValue = null;
            }
            else if (DateTime.UtcNow - _pendingSinceUtc < PendingReadbackWindow)
            {
                return;
            }
            else
            {
                _pendingValue = null;
            }
        }

        // Provider-originated updates never write back to Core Audio.
        if (Math.Abs(_value - snapshot.Value) >= VolumeAckTolerance)
        {
            _isInternalUpdate = true;
            try
            {
                Value = snapshot.Value;
            }
            finally
            {
                _isInternalUpdate = false;
            }
        }
    }
}

/// <summary>
/// Generic host container for runtime-generated dynamic items emitted by an IWidgetProvider.
/// Supports stable-identity diffing, in-place updates, adding, removing, and reordering.
/// </summary>
public sealed class DynamicListItemViewModel : ObservableBarItemViewModel, IDisposable
{
    private readonly IWidgetProvider? _provider;
    private readonly bool _ownsProvider;
    private readonly string _emptyBehavior;
    private readonly int? _maxItems;
    private readonly ActionExecutor? _actions;
    private readonly AppLogger? _logger;
    private readonly Dispatcher _dispatcher;
    private volatile bool _disposed;
    private long _latestRevision;
    private Visibility _visibility = Visibility.Visible;

    public string ProviderId { get; }
    public string EmptyBehavior => _emptyBehavior;
    public ObservableCollection<BarItemViewModel> Items { get; } = new();

    public Visibility Visibility
    {
        get => _visibility;
        private set => SetField(ref _visibility, value);
    }

    public bool IsEmpty => Items.Count == 0;

    public static DynamicListItemViewModel CreatePreview(
        string providerId,
        IEnumerable<BarItemViewModel> previewItems,
        double width = double.NaN)
    {
        return new DynamicListItemViewModel(providerId, previewItems, width);
    }

    private DynamicListItemViewModel(
        string providerId,
        IEnumerable<BarItemViewModel> previewItems,
        double width)
    {
        _provider = null;
        ProviderId = providerId ?? string.Empty;
        _ownsProvider = false;
        Width = width <= 0 ? double.NaN : width;
        _emptyBehavior = "show";
        _maxItems = null;
        _actions = null;
        _logger = null;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _visibility = Visibility.Visible;

        if (previewItems != null)
        {
            foreach (var item in previewItems)
            {
                Items.Add(item);
            }
        }
    }

    public DynamicListItemViewModel(
        IWidgetProvider provider,
        bool ownsProvider = false,
        double width = double.NaN,
        string emptyBehavior = "collapse",
        int? maxItems = null,
        ActionExecutor? actions = null,
        AppLogger? logger = null,
        Dispatcher? dispatcher = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        ProviderId = _provider.Id;
        _ownsProvider = ownsProvider;
        Width = width <= 0 ? double.NaN : width;
        _emptyBehavior = string.IsNullOrWhiteSpace(emptyBehavior) ? "collapse" : emptyBehavior.ToLowerInvariant();
        _maxItems = maxItems;
        _actions = actions;
        _logger = logger;
        _dispatcher = dispatcher
            ?? System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        _provider.ItemsChanged += OnProviderItemsChanged;
        ReconcileCore(_provider.CurrentItems);
    }

    private void OnProviderItemsChanged(object? sender, WidgetItemsChangedEventArgs e)
    {
        if (_disposed) return;

        // Revision semantics:
        // Revision > 0: Monotonic versioned update; stale or repeated revisions are dropped.
        // Revision == 0: Legacy / unversioned update; processed unconditionally.
        if (e.Revision > 0 && e.Revision <= Volatile.Read(ref _latestRevision))
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            ApplyEvent(e);
        }
        else
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                    ApplyEvent(e);
            });
        }
    }

    private void ApplyEvent(WidgetItemsChangedEventArgs e)
    {
        if (_disposed) return;

        // Revision semantics:
        // Revision > 0: Enforce monotonic sequence; ignore stale/repeated revisions.
        // Revision == 0: Legacy / unversioned update; applied unconditionally without advancing revision sequence.
        if (e.Revision > 0)
        {
            if (e.Revision <= _latestRevision)
                return;
            _latestRevision = e.Revision;
        }

        ReconcileCore(e.Items);
    }

    public void ReconcileItems(IReadOnlyList<DynamicItemSnapshot> snapshots)
    {
        if (_disposed) return;

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                    ReconcileCore(snapshots);
            });
            return;
        }

        ReconcileCore(snapshots);
    }

    private void ReconcileCore(IReadOnlyList<DynamicItemSnapshot> snapshots)
    {
        if (_disposed) return;

        var source = snapshots ?? Array.Empty<DynamicItemSnapshot>();
        if (_maxItems.HasValue && _maxItems.Value > 0 && source.Count > _maxItems.Value)
        {
            source = source.Take(_maxItems.Value).ToList();
        }

        // Deduplicate snapshots by ID deterministically: first occurrence wins, duplicate IDs rejected
        var uniqueSnapshots = new List<DynamicItemSnapshot>(source.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in source)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Id))
                continue;

            if (!seenIds.Add(s.Id))
            {
                _logger?.Warn($"Dynamic widget provider '{ProviderId}' emitted duplicate item ID '{s.Id}'. Deterministically ignoring duplicate entry.");
                continue;
            }

            uniqueSnapshots.Add(s);
        }

        // 1. Map new snapshots by Id
        var newSnapshotMap = new Dictionary<string, DynamicItemSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in uniqueSnapshots)
        {
            newSnapshotMap[s.Id] = s;
        }

        // 2. Remove items no longer in source
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var item = Items[i];
            if (string.IsNullOrEmpty(item.Id) || !newSnapshotMap.ContainsKey(item.Id))
            {
                CleanupItem(item);
                Items.RemoveAt(i);
            }
        }

        // 3. Map current surviving items by Id
        var currentMap = new Dictionary<string, BarItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            if (!string.IsNullOrEmpty(item.Id))
                currentMap[item.Id] = item;
        }

        // 4. Update in-place, replace on type mismatch, insert new, or reorder
        for (int targetIndex = 0; targetIndex < uniqueSnapshots.Count; targetIndex++)
        {
            var snapshot = uniqueSnapshots[targetIndex];

            if (currentMap.TryGetValue(snapshot.Id, out var existingItem))
            {
                if (IsMatchingType(existingItem, snapshot.Type))
                {
                    // Same ID + Same Type -> update existing ViewModel in-place
                    UpdateExistingItem(existingItem, snapshot);

                    // Move if position shifted
                    int currentIndex = Items.IndexOf(existingItem);
                    if (currentIndex >= 0 && currentIndex != targetIndex)
                    {
                        Items.Move(currentIndex, targetIndex);
                    }
                }
                else
                {
                    // Same ID + Different Type -> dispose old VM, create correct replacement VM
                    int currentIndex = Items.IndexOf(existingItem);
                    CleanupItem(existingItem);

                    var newItem = CreateItemViewModel(snapshot);
                    if (currentIndex >= 0)
                    {
                        Items[currentIndex] = newItem;
                    }
                    else
                    {
                        Items.Insert(targetIndex, newItem);
                    }

                    currentMap[snapshot.Id] = newItem;

                    if (currentIndex >= 0 && currentIndex != targetIndex)
                    {
                        Items.Move(currentIndex, targetIndex);
                    }
                }
            }
            else
            {
                // New item
                var newItem = CreateItemViewModel(snapshot);
                Items.Insert(targetIndex, newItem);
                currentMap[snapshot.Id] = newItem;
            }
        }

        UpdateVisibilityState();
    }

    private static bool IsMatchingType(BarItemViewModel vm, string? snapshotType)
    {
        var type = snapshotType?.Trim().ToLowerInvariant() ?? WidgetTypes.Button;
        return type switch
        {
            WidgetTypes.AudioSession => vm is AudioSessionItemViewModel,
            WidgetTypes.Slider => vm is SliderItemViewModel,
            WidgetTypes.Toggle => vm is ToggleItemViewModel,
            WidgetTypes.Sensor => vm is SensorItemViewModel,
            WidgetTypes.Button => vm is ButtonItemViewModel,
            _ => vm is ButtonItemViewModel
        };
    }

    private static void CleanupItem(BarItemViewModel item)
    {
        if (item is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (item is AudioSessionItemViewModel audioApp)
        {
            audioApp.ValueSetter = null;
            audioApp.MuteSetter = null;
            audioApp.ToggleMuteCommand = new DelegateCommand(() => { });
        }
        else if (item is ButtonItemViewModel btn)
        {
            btn.Command = new DelegateCommand(() => { });
            btn.SecondaryCommand = null;
        }
        else if (item is SliderItemViewModel slider)
        {
            slider.Setter = null;
        }
        else if (item is ToggleItemViewModel toggle)
        {
            toggle.ToggleCommand = new DelegateCommand(() => { });
            toggle.SecondaryCommand = null;
        }
    }

    private void UpdateExistingItem(BarItemViewModel existingItem, DynamicItemSnapshot snapshot)
    {
        existingItem.Width = snapshot.Width;

        switch (existingItem)
        {
            case ButtonItemViewModel btn:
                btn.Label = snapshot.Label;
                TrySetCommands(btn, snapshot);
                break;

            case SliderItemViewModel slider:
                slider.Label = snapshot.Label;
                slider.Minimum = snapshot.Min;
                slider.Maximum = snapshot.Max;
                slider.Setter = snapshot.ValueSetter;
                slider.Value = snapshot.Value;
                break;

            case ToggleItemViewModel toggle:
                toggle.Label = snapshot.Label;
                toggle.OnLabel = snapshot.OnLabel;
                toggle.OffLabel = snapshot.OffLabel;
                toggle.IsChecked = snapshot.IsChecked;
                if (!TrySetCommands(toggle, snapshot))
                {
                    // Preserve the update-only local toggle when no action is supplied.
                    toggle.ToggleCommand = new DelegateCommand(() =>
                    {
                        toggle.IsChecked = !toggle.IsChecked;
                    });
                }
                break;

            case SensorItemViewModel sensor:
                sensor.Label = snapshot.Label;
                sensor.Value = snapshot.Value;
                sensor.Metric = snapshot.Metric ?? "";
                sensor.Unit = snapshot.Unit ?? "";
                if (!string.IsNullOrEmpty(snapshot.Format))
                    sensor.Format = snapshot.Format;
                sensor.Status = snapshot.Status ?? "normal";
                break;

            case AudioSessionItemViewModel audioApp:
                audioApp.UpdateFromSnapshot(snapshot);
                break;
        }
    }

    private BarItemViewModel CreateItemViewModel(DynamicItemSnapshot snapshot)
    {
        var type = snapshot.Type?.Trim().ToLowerInvariant() ?? WidgetTypes.Button;
        switch (type)
        {
            case WidgetTypes.AudioSession:
                return new AudioSessionItemViewModel(
                    snapshot.Label,
                    snapshot.Width,
                    snapshot.Min,
                    snapshot.Max,
                    snapshot.Value,
                    snapshot.IsChecked,
                    snapshot.ValueSetter,
                    snapshot.OnToggle)
                {
                    Id = snapshot.Id
                };

            case WidgetTypes.Slider:
                return new SliderItemViewModel(
                    snapshot.Label,
                    snapshot.Width,
                    snapshot.Min,
                    snapshot.Max,
                    snapshot.Value,
                    snapshot.ValueSetter)
                {
                    Id = snapshot.Id
                };

            case WidgetTypes.Toggle:
            {
                var toggle = new ToggleItemViewModel
                {
                    Id = snapshot.Id,
                    Label = snapshot.Label,
                    OnLabel = snapshot.OnLabel,
                    OffLabel = snapshot.OffLabel,
                    Width = snapshot.Width,
                    IsChecked = snapshot.IsChecked
                };
                TrySetCommands(toggle, snapshot);
                return toggle;
            }

            case WidgetTypes.Sensor:
                return new SensorItemViewModel
                {
                    Id = snapshot.Id,
                    Label = snapshot.Label,
                    Width = snapshot.Width,
                    Value = snapshot.Value,
                    Metric = snapshot.Metric ?? "",
                    Unit = snapshot.Unit ?? "",
                    Format = snapshot.Format ?? "{0}",
                    Status = snapshot.Status ?? "normal"
                };

            case WidgetTypes.Button:
            default:
            {
                var button = new ButtonItemViewModel
                {
                    Id = snapshot.Id,
                    Label = snapshot.Label,
                    Width = snapshot.Width
                };
                TrySetCommands(button, snapshot);
                return button;
            }
        }
    }

    private bool TrySetCommands(BarItemViewModel item, DynamicItemSnapshot snapshot)
    {
        switch (item)
        {
            case ButtonItemViewModel button:
                if (snapshot.OnClick != null)
                    button.Command = new DelegateCommand(snapshot.OnClick);
                else if (snapshot.Action != null && _actions != null)
                    button.Command = new DelegateCommand(() => _actions.Execute(snapshot.Action));
                else
                    button.Command = new DelegateCommand(() => { });

                if (snapshot.OnSecondaryClick != null)
                    button.SecondaryCommand = new DelegateCommand(snapshot.OnSecondaryClick);
                else if (snapshot.SecondaryAction != null && _actions != null)
                    button.SecondaryCommand = new DelegateCommand(() => _actions.Execute(snapshot.SecondaryAction));
                else
                    button.SecondaryCommand = null;
                return true;

            case ToggleItemViewModel toggle when snapshot.OnToggle != null:
                toggle.ToggleCommand = new DelegateCommand(() =>
                {
                    toggle.IsChecked = !toggle.IsChecked;
                    snapshot.OnToggle(toggle.IsChecked);
                });
                return true;

            case ToggleItemViewModel toggle when snapshot.Action != null && _actions != null:
                toggle.ToggleCommand = new DelegateCommand(() =>
                {
                    toggle.IsChecked = !toggle.IsChecked;
                    _actions.Execute(snapshot.Action);
                });
                return true;

            default:
                return false;
        }
    }

    private void UpdateVisibilityState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        if (Items.Count == 0 && _emptyBehavior == "collapse")
        {
            Visibility = Visibility.Collapsed;
        }
        else if (Items.Count == 0 && _emptyBehavior == "hidden")
        {
            Visibility = Visibility.Hidden;
        }
        else
        {
            Visibility = Visibility.Visible;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_provider != null)
        {
            _provider.ItemsChanged -= OnProviderItemsChanged;
        }

        foreach (var item in Items)
        {
            CleanupItem(item);
        }
        Items.Clear();

        if (_ownsProvider && _provider != null)
        {
            _provider.Dispose();
        }
    }
}
