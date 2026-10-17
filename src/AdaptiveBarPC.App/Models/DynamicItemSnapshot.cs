namespace AdaptiveBarPC.Models;

/// <summary>
/// Immutable snapshot representing a single item emitted by a runtime dynamic widget provider.
/// Items are matched by stable Id during reconciliation to avoid UI churn.
/// </summary>
public sealed class DynamicItemSnapshot
{
    /// <summary>
    /// Stable identifier for this item (e.g. "audio:pid:1234", "sensor:cpu", "obs:scene:main").
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// Item widget type (button, slider, toggle, sensor). Defaults to button.
    /// </summary>
    public string Type { get; init; } = WidgetTypes.Button;

    /// <summary>
    /// Display label or title.
    /// </summary>
    public string Label { get; init; } = "";

    /// <summary>
    /// Width in DIPs. Use <= 0 or double.NaN for flexible width.
    /// </summary>
    public double Width { get; init; } = 84;

    /// <summary>
    /// Current numeric value for sliders or sensors.
    /// </summary>
    public double Value { get; init; } = 0;

    /// <summary>
    /// Minimum value for sliders or sensors.
    /// </summary>
    public double Min { get; init; } = 0;

    /// <summary>
    /// Maximum value for sliders or sensors.
    /// </summary>
    public double Max { get; init; } = 100;

    /// <summary>
    /// Checked/toggled state for toggle widgets.
    /// </summary>
    public bool IsChecked { get; init; } = false;

    public string? OnLabel { get; init; }
    public string? OffLabel { get; init; }

    /// <summary>
    /// Sensor metric identifier (e.g. "cpu_load", "gpu_temp").
    /// </summary>
    public string? Metric { get; init; }

    /// <summary>
    /// Measurement unit (e.g. "%", "°C", "RPM").
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// String format specification for numeric readouts (e.g. "{0:F0}").
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// Status level for styling (e.g. "normal", "warning", "critical").
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Standard declarative action definition.
    /// </summary>
    public ActionDefinition? Action { get; init; }

    /// <summary>
    /// Secondary declarative action definition (e.g. long press).
    /// </summary>
    public ActionDefinition? SecondaryAction { get; init; }

    /// <summary>
    /// Optional programmatic click handler.
    /// </summary>
    public Action? OnClick { get; init; }

    /// <summary>
    /// Optional programmatic secondary/long-press handler.
    /// </summary>
    public Action? OnSecondaryClick { get; init; }

    /// <summary>
    /// Optional programmatic toggle state handler.
    /// </summary>
    public Action<bool>? OnToggle { get; init; }

    /// <summary>
    /// Optional programmatic value setter for sliders.
    /// </summary>
    public Action<double>? ValueSetter { get; init; }

    /// <summary>
    /// Arbitrary backend metadata or reference.
    /// </summary>
    public object? CustomData { get; init; }
}
