using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AdaptiveBarPC.Controls;

/// <summary>
/// Slider with explicit touchscreen handling for WS_EX_NOACTIVATE windows.
/// Mouse behavior remains the normal WPF Slider behavior.
/// Touch position is mapped directly to Value instead of relying on Thumb mouse capture.
/// </summary>
public sealed class TouchSlider : Slider
{
    private int? _activeTouchDeviceId;

    public TouchSlider()
    {
        Unloaded += (_, _) =>
        {
            _activeTouchDeviceId = null;
            ReleaseAllTouchCaptures();
            if (IsMouseCaptureWithin)
                Mouse.Capture(null);
            IsInteracting = false;
        };
    }

    public static readonly DependencyProperty IsInteractingProperty =
        DependencyProperty.Register(
            nameof(IsInteracting),
            typeof(bool),
            typeof(TouchSlider),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsInteracting
    {
        get => (bool)GetValue(IsInteractingProperty);
        set => SetValue(IsInteractingProperty, value);
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

        _activeTouchDeviceId = e.TouchDevice.Id;
        IsInteracting = true;

        // Touch capture does not activate the window. If the driver refuses
        // capture, in-bounds TouchMove events still continue to update Value.
        CaptureTouch(e.TouchDevice);

        UpdateValueFromTouch(e);
        e.Handled = true;
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        if (_activeTouchDeviceId != e.TouchDevice.Id)
            return;

        UpdateValueFromTouch(e);
        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        if (_activeTouchDeviceId != e.TouchDevice.Id)
            return;

        UpdateValueFromTouch(e);

        _activeTouchDeviceId = null;
        if (e.TouchDevice.Captured == this)
            ReleaseTouchCapture(e.TouchDevice);

        IsInteracting = false;
        e.Handled = true;
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        if (_activeTouchDeviceId == e.TouchDevice.Id)
        {
            _activeTouchDeviceId = null;
            IsInteracting = false;
        }

        base.OnLostTouchCapture(e);
    }

    protected override void OnTouchLeave(TouchEventArgs e)
    {
        // If capture was refused, a finger sliding off the slider would otherwise
        // keep _activeTouchDeviceId alive and the control stuck in "active" state.
        if (_activeTouchDeviceId == e.TouchDevice.Id && e.TouchDevice.Captured != this)
        {
            _activeTouchDeviceId = null;
            IsInteracting = false;
        }

        base.OnTouchLeave(e);
    }

    private void UpdateValueFromTouch(TouchEventArgs e)
    {
        if (ActualWidth <= 0 || Maximum <= Minimum)
            return;

        var x = e.GetTouchPoint(this).Position.X;
        var ratio = Math.Clamp(x / ActualWidth, 0.0, 1.0);

        if (IsDirectionReversed)
            ratio = 1.0 - ratio;

        Value = Minimum + ((Maximum - Minimum) * ratio);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        IsInteracting = true;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        IsInteracting = false;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        IsInteracting = false;
    }
}
