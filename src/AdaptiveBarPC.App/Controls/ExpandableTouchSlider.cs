using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace AdaptiveBarPC.Controls;

internal enum ExpandableSliderState
{
    Collapsed,
    ExpandedIdle,
    Dragging
}

internal static class ExpandableSliderInteraction
{
    internal static ExpandableSliderState PointerDown(ExpandableSliderState state) =>
        state == ExpandableSliderState.Collapsed
            ? ExpandableSliderState.ExpandedIdle
            : state;

    internal static ExpandableSliderState PointerMove(
        ExpandableSliderState state,
        double horizontalDelta,
        double threshold) =>
        state != ExpandableSliderState.Collapsed && Math.Abs(horizontalDelta) >= threshold
            ? ExpandableSliderState.Dragging
            : state;

    internal static ExpandableSliderState PointerUp(ExpandableSliderState state) =>
        state == ExpandableSliderState.Collapsed
            ? state
            : ExpandableSliderState.ExpandedIdle;

    internal static ExpandableSliderState IdleElapsed(ExpandableSliderState state) =>
        state == ExpandableSliderState.ExpandedIdle
            ? ExpandableSliderState.Collapsed
            : state;

    internal static double MapPosition(double x, double width, double minimum, double maximum)
    {
        if (width <= 0 || maximum <= minimum)
            return minimum;

        var ratio = Math.Clamp(x / width, 0.0, 1.0);
        return minimum + ((maximum - minimum) * ratio);
    }
}

public sealed class ExpandableTouchSlider : Slider
{
    public const double CollapsedWidthDip = 56;
    public const double ExpandedWidthDip = 220;
    public const double DragThresholdDip = 6;
    public const int ExpandAnimationMs = 140;
    public const int CollapseAnimationMs = 140;
    public const int CollapseDelayMs = 850;

    private const double TrackLeftInsetDip = 30;
    private const double TrackRightInsetDip = 42;

    private static readonly DependencyPropertyKey IsExpandedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsExpanded),
            typeof(bool),
            typeof(ExpandableTouchSlider),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsExpandedProperty = IsExpandedPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(string),
            typeof(ExpandableTouchSlider),
            new PropertyMetadata(""));

    public static readonly DependencyProperty ValueTextProperty =
        DependencyProperty.Register(
            nameof(ValueText),
            typeof(string),
            typeof(ExpandableTouchSlider),
            new PropertyMetadata(""));

    public static readonly DependencyProperty EnableAnimationsProperty =
        DependencyProperty.Register(
            nameof(EnableAnimations),
            typeof(bool),
            typeof(ExpandableTouchSlider),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsInteractingProperty =
        DependencyProperty.Register(
            nameof(IsInteracting),
            typeof(bool),
            typeof(ExpandableTouchSlider),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private readonly DispatcherTimer _collapseTimer;
    private ExpandableSliderState _state = ExpandableSliderState.Collapsed;
    private int? _activeTouchDeviceId;
    private bool _mouseActive;
    private Point _pointerStart;

    public bool IsExpanded => (bool)GetValue(IsExpandedProperty);

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public bool EnableAnimations
    {
        get => (bool)GetValue(EnableAnimationsProperty);
        set => SetValue(EnableAnimationsProperty, value);
    }

    public bool IsInteracting
    {
        get => (bool)GetValue(IsInteractingProperty);
        set => SetValue(IsInteractingProperty, value);
    }

    public ExpandableTouchSlider()
    {
        Width = CollapsedWidthDip;
        Height = 56;
        Focusable = false;
        IsTabStop = false;

        _collapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(CollapseDelayMs)
        };
        _collapseTimer.Tick += OnCollapseTimerTick;
        Unloaded += OnUnloaded;
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (_activeTouchDeviceId.HasValue)
        {
            e.Handled = true;
            return;
        }

        var root = GetCoordinateRoot();
        var wasExpanded = IsExpanded;
        _collapseTimer.Stop();
        _pointerStart = e.GetTouchPoint(root).Position;
        _activeTouchDeviceId = e.TouchDevice.Id;
        IsInteracting = true;
        _state = ExpandableSliderInteraction.PointerDown(_state);
        SetExpanded(true);
        CaptureTouch(e.TouchDevice);

        if (wasExpanded)
            UpdateValueFromRootX(_pointerStart.X, root);

        e.Handled = true;
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        if (_activeTouchDeviceId != e.TouchDevice.Id)
            return;

        var root = GetCoordinateRoot();
        var point = e.GetTouchPoint(root).Position;
        _state = ExpandableSliderInteraction.PointerMove(
            _state,
            point.X - _pointerStart.X,
            DragThresholdDip);

        if (_state == ExpandableSliderState.Dragging)
            UpdateValueFromRootX(point.X, root);

        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        if (_activeTouchDeviceId != e.TouchDevice.Id)
            return;

        if (_state == ExpandableSliderState.Dragging)
        {
            var root = GetCoordinateRoot();
            UpdateValueFromRootX(e.GetTouchPoint(root).Position.X, root);
        }

        _activeTouchDeviceId = null;
        if (e.TouchDevice.Captured == this)
            ReleaseTouchCapture(e.TouchDevice);

        EndInteraction();
        e.Handled = true;
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        if (_activeTouchDeviceId == e.TouchDevice.Id)
        {
            _activeTouchDeviceId = null;
            EndInteraction();
        }

        base.OnLostTouchCapture(e);
    }

    protected override void OnTouchLeave(TouchEventArgs e)
    {
        if (_activeTouchDeviceId == e.TouchDevice.Id && e.TouchDevice.Captured != this)
        {
            _activeTouchDeviceId = null;
            EndInteraction();
        }

        base.OnTouchLeave(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled || _mouseActive)
            return;

        var root = GetCoordinateRoot();
        var wasExpanded = IsExpanded;
        _collapseTimer.Stop();
        _pointerStart = e.GetPosition(root);
        _mouseActive = true;
        IsInteracting = true;
        _state = ExpandableSliderInteraction.PointerDown(_state);
        SetExpanded(true);
        Mouse.Capture(this, CaptureMode.Element);

        if (wasExpanded)
            UpdateValueFromRootX(_pointerStart.X, root);

        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (!_mouseActive)
            return;

        var root = GetCoordinateRoot();
        var point = e.GetPosition(root);
        _state = ExpandableSliderInteraction.PointerMove(
            _state,
            point.X - _pointerStart.X,
            DragThresholdDip);

        if (_state == ExpandableSliderState.Dragging)
            UpdateValueFromRootX(point.X, root);

        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_mouseActive)
            return;

        if (_state == ExpandableSliderState.Dragging)
        {
            var root = GetCoordinateRoot();
            UpdateValueFromRootX(e.GetPosition(root).X, root);
        }

        _mouseActive = false;
        if (IsMouseCaptured)
            Mouse.Capture(null);

        EndInteraction();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_mouseActive)
        {
            _mouseActive = false;
            EndInteraction();
        }

        base.OnLostMouseCapture(e);
    }

    private UIElement GetCoordinateRoot() =>
        (UIElement?)Window.GetWindow(this) ?? this;

    private void UpdateValueFromRootX(double rootX, UIElement root)
    {
        var rightEdge = TranslatePoint(new Point(ActualWidth, 0), root).X;
        var trackLeft = rightEdge - ExpandedWidthDip + TrackLeftInsetDip;
        var trackWidth = ExpandedWidthDip - TrackLeftInsetDip - TrackRightInsetDip;

        var value = ExpandableSliderInteraction.MapPosition(
            rootX - trackLeft,
            trackWidth,
            Minimum,
            Maximum);

        Value = IsDirectionReversed
            ? Maximum - (value - Minimum)
            : value;
    }

    private void EndInteraction()
    {
        IsInteracting = false;
        _state = ExpandableSliderInteraction.PointerUp(_state);
        if (_state == ExpandableSliderState.ExpandedIdle && IsLoaded)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        _state = ExpandableSliderInteraction.IdleElapsed(_state);
        if (_state == ExpandableSliderState.Collapsed)
            SetExpanded(false);
    }

    private void SetExpanded(bool expanded)
    {
        SetValue(IsExpandedPropertyKey, expanded);

        var target = expanded ? ExpandedWidthDip : CollapsedWidthDip;
        var durationMs = expanded ? ExpandAnimationMs : CollapseAnimationMs;

        BeginAnimation(WidthProperty, null);

        if (!EnableAnimations || !IsLoaded)
        {
            Width = target;
            return;
        }

        var current = ActualWidth > 0 ? ActualWidth : Width;
        Width = target;
        BeginAnimation(
            WidthProperty,
            new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(durationMs))
            {
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _collapseTimer.Stop();

        _activeTouchDeviceId = null;
        _mouseActive = false;
        ReleaseAllTouchCaptures();

        if (IsMouseCaptured)
            Mouse.Capture(null);

        IsInteracting = false;
        _state = ExpandableSliderState.Collapsed;
        SetValue(IsExpandedPropertyKey, false);
        BeginAnimation(WidthProperty, null);
        Width = CollapsedWidthDip;
    }
}
