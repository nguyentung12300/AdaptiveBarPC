using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Threading;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace AdaptiveBarPC.Controls;

/// <summary>
/// Button with explicit mouse/touch activation for WS_EX_NOACTIVATE windows.
/// Short tap/click executes Command; long press executes SecondaryCommand.
///
/// Important: primary activation does NOT depend on ButtonBase.OnClick(),
/// mouse capture, or touch-to-mouse promotion.
/// </summary>
public sealed class LongPressButton : System.Windows.Controls.Button
{
    // Null means an ordinary action button; toggles bind their command-owned state.
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked), typeof(bool?), typeof(LongPressButton), new PropertyMetadata(null, (d, e) =>
        {
            if (UIElementAutomationPeer.FromElement((LongPressButton)d) is { } peer)
                peer.RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty,
                    e.OldValue is true ? ToggleState.On : ToggleState.Off,
                    e.NewValue is true ? ToggleState.On : ToggleState.Off);
        }));

    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public static readonly DependencyProperty SecondaryCommandProperty =
        DependencyProperty.Register(
            nameof(SecondaryCommand),
            typeof(ICommand),
            typeof(LongPressButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LongPressMillisecondsProperty =
        DependencyProperty.Register(
            nameof(LongPressMilliseconds),
            typeof(int),
            typeof(LongPressButton),
            new PropertyMetadata(650));

    public ICommand? SecondaryCommand
    {
        get => (ICommand?)GetValue(SecondaryCommandProperty);
        set => SetValue(SecondaryCommandProperty, value);
    }

    public int LongPressMilliseconds
    {
        get => (int)GetValue(LongPressMillisecondsProperty);
        set => SetValue(LongPressMillisecondsProperty, value);
    }

    // Suppress promoted synthetic mouse events that arrive within this
    // window after touch-up. WPF can synthesize a PreviewMouseLeftButtonDown
    // within tens of milliseconds of the touch sequence ending.
    private const long TouchToMouseSuppressMs = 250;

    private readonly DispatcherTimer _timer;
    private bool _touchActive;
    private bool _mouseActive;
    private bool _longPressFired;
    private bool _pressCancelled;

    // Timestamp (Environment.TickCount64) of the most recent OnTouchUp, used
    // to suppress WPF-promoted synthetic mouse events that arrive shortly after.
    private long _lastTouchUpTick = long.MinValue;

    public LongPressButton()
    {
        Focusable = false;
        IsManipulationEnabled = true;

        _timer = new DispatcherTimer();
        _timer.Tick += OnLongPressTimer;

        Unloaded += (_, _) => ResetPress();
    }

    // ------------------------------------------------------------------
    // Touch
    // ------------------------------------------------------------------

    protected override void OnTouchDown(TouchEventArgs e)
    {
        if (!IsEnabled)
            return;

        _touchActive = true;
        _mouseActive = false;
        BeginPress();
        CaptureTouch(e.TouchDevice);

        // We own touch activation. Marking handled also prevents WPF from
        // promoting this touch sequence into a second synthetic mouse click.
        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        if (!_touchActive)
        {
            e.Handled = true;
            return;
        }

        EndPress();

        var point = e.GetTouchPoint(this).Position;
        var releasedInside =
            point.X >= 0 &&
            point.Y >= 0 &&
            point.X <= ActualWidth &&
            point.Y <= ActualHeight;

        var executePrimary =
            !_pressCancelled &&
            !_longPressFired &&
            releasedInside;

        // Record timestamp BEFORE ResetPress() so the mouse guard is set
        // before WPF can immediately synthesize and deliver a mouse-down.
        _lastTouchUpTick = Environment.TickCount64;

        ResetPress();

        if (e.TouchDevice.Captured == this)
            ReleaseTouchCapture(e.TouchDevice);

        if (executePrimary)
            ExecutePrimaryCommand();

        e.Handled = true;
    }

    protected override void OnTouchLeave(TouchEventArgs e)
    {
        if (_touchActive)
        {
            // Cancel the in-progress press and clear touch-active so the
            // button does not remain stuck in touch-mode if WPF does not
            // deliver a subsequent OnLostTouchCapture (e.g. touch sequence
            // ends outside the element without capture).
            _pressCancelled = true;
            _touchActive = false;
            EndPress();
        }

        base.OnTouchLeave(e);
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        if (_touchActive)
        {
            // Mark cancelled so a concurrent timer tick cannot fire the
            // secondary action after capture is already gone.
            _pressCancelled = true;
            ResetPress();
        }

        base.OnLostTouchCapture(e);
    }

    // ------------------------------------------------------------------
    // Mouse
    // ------------------------------------------------------------------

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // Suppress WPF-promoted synthetic mouse events that arrive within
        // TouchToMouseSuppressMs after a touch sequence ends.
        var ticksSinceTouchUp = Environment.TickCount64 - _lastTouchUpTick;
        if (_lastTouchUpTick != long.MinValue && ticksSinceTouchUp < TouchToMouseSuppressMs)
        {
            e.Handled = true;
            return;
        }

        // If a touch is currently active, ignore any promoted mouse event.
        if (_touchActive || !IsEnabled)
        {
            e.Handled = true;
            return;
        }

        _mouseActive = true;
        BeginPress();

        // Do not let ButtonBase start its normal capture/click machinery.
        // WS_EX_NOACTIVATE windows are exactly where that path is unreliable.
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_mouseActive)
        {
            // A promoted mouse-up after touch has no matching mouse-down here.
            e.Handled = true;
            return;
        }

        EndPress();

        var executePrimary =
            !_pressCancelled &&
            !_longPressFired &&
            IsMouseOver;

        ResetPress();

        if (executePrimary)
            ExecutePrimaryCommand();

        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_mouseActive)
        {
            _pressCancelled = true;
            EndPress();
        }

        base.OnMouseLeave(e);
    }

    // ------------------------------------------------------------------
    // Explicit command execution
    // ------------------------------------------------------------------

    private void ExecutePrimaryCommand()
    {
        var command = Command;
        var parameter = CommandParameter;

        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
    }

    private void ExecuteSecondaryCommand()
    {
        var command = SecondaryCommand;
        var parameter = CommandParameter;

        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
    }

    // Intentionally suppress ButtonBase's normal click path.
    // Mouse/touch activation is handled explicitly above.
    protected override void OnClick()
    {
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LongPressButtonAutomationPeer(this);

    private sealed class LongPressButtonAutomationPeer(LongPressButton button) : ButtonAutomationPeer(button), IInvokeProvider, IToggleProvider
    {
        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ||
            (patternInterface == PatternInterface.Toggle && button.IsChecked.HasValue) ? this : base.GetPattern(patternInterface);

        ToggleState IToggleProvider.ToggleState => button.IsChecked is true ? ToggleState.On : ToggleState.Off;
        void IToggleProvider.Toggle() => ((IInvokeProvider)this).Invoke();

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();

            button.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                // Keep automation separate from the suppressed mouse/touch click path.
                if (!button.IsEnabled || button._touchActive || button._mouseActive) return;
                button.ExecutePrimaryCommand();
                RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
            }));
        }
    }

    // ------------------------------------------------------------------
    // Long press
    // ------------------------------------------------------------------

    private void BeginPress()
    {
        _pressCancelled = false;
        _longPressFired = false;

        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(
            Math.Clamp(LongPressMilliseconds, 250, 3000));
        _timer.Start();
    }

    private void EndPress()
    {
        _timer.Stop();
    }

    internal void CancelActivePress()
    {
        if (!_touchActive && !_mouseActive)
            return;

        _pressCancelled = true;
        EndPress();
    }

    private void ResetPress()
    {
        _timer.Stop();
        _touchActive = false;
        _mouseActive = false;
        _longPressFired = false;
        _pressCancelled = false;
    }

    private void OnLongPressTimer(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (_pressCancelled || (!_touchActive && !_mouseActive))
            return;

        _longPressFired = true;
        ExecuteSecondaryCommand();
    }
}
