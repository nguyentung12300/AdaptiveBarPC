using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC;

public partial class MainWindow : Window
{
    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_DISPLAYCHANGE = 0x007E;

    private readonly AppConfig _config;
    private readonly MainViewModel _vm;
    private readonly AppLogger _logger;
    private readonly HashSet<uint> _touchPointers = new();
    private AdaptiveBarPC.Interop.TouchMouseFilter? _touchMouseFilter;
    private AdaptiveBarPC.Services.AppBarService? _appBarService;
    private System.Windows.Threading.DispatcherTimer? _inputDiagnostics;
    private int _lastBlockedMoves;
    private SwipeState _swipeState;
    private AdaptiveBarPC.Controls.LongPressButton? _swipeButtonCandidate;
    private bool _swipeStartedInItemsScroller;
    private double _swipeStartHorizontalOffset;
    private bool _suppressNextLayoutAnimation;

    internal AdaptiveBarPC.Services.AppBarService? AppBarService => _appBarService;

    public MainWindow(AppConfig config, MainViewModel vm, AppLogger logger)
    {
        InitializeComponent();
        _config = config;
        _vm = vm;
        _logger = logger;
        DataContext = vm;
        Topmost = config.Topmost;

        ItemsScroller.ScrollChanged += (_, _) => UpdateOverflowFades();
        ItemsScroller.SizeChanged += (_, _) => UpdateOverflowFades();

        _vm.LayoutChanged += (_, _) =>
        {
            if (_suppressNextLayoutAnimation)
            {
                _suppressNextLayoutAnimation = false;
                return;
            }

            if (_config.EnableAnimations)
                AnimateLayoutChange();

            Dispatcher.BeginInvoke(UpdateOverflowFades);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        _vm.ClipboardHistory.Attach(hwnd);

        if (_config.NoActivate)
        {
            try
            {
                AdaptiveBarPC.Interop.NativeWindow.SetNoActivate(hwnd);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to apply WS_EX_NOACTIVATE.", ex);
            }
        }

        try
        {
            _appBarService = new AdaptiveBarPC.Services.AppBarService(this, hwnd, _config, _logger);
            _appBarService.Register();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to register AppBar.", ex);
            try
            {
                MonitorDockService.Dock(this, hwnd, _config, _logger);
            }
            catch (Exception dockEx)
            {
                _logger.Error("Failed to dock bar to monitor.", dockEx);
            }
        }

        try
        {
            _touchMouseFilter = new AdaptiveBarPC.Interop.TouchMouseFilter(hwnd);
            _logger.Info($"Touch mouse-move filter installed. Build: {typeof(MainWindow).Assembly.ManifestModule.ModuleVersionId}");
            _inputDiagnostics = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _inputDiagnostics.Tick += (_, _) =>
            {
                var blocked = _touchMouseFilter.BlockedMoves;
                if (blocked != _lastBlockedMoves)
                {
                    _logger.Info($"Touch mouse-move filter: blocked={blocked}");
                    _lastBlockedMoves = blocked;
                }
            };
            _inputDiagnostics.Start();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to install touch mouse-move filter.", ex);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.ClipboardHistory.Detach();
        _inputDiagnostics?.Stop();
        _touchMouseFilter?.Dispose();
        _appBarService?.Dispose();
        base.OnClosed(e);
    }

    private void RootBorder_OnManipulationStarting(
        object sender,
        ManipulationStartingEventArgs e)
    {
        StopSwipeAnimation();

        e.ManipulationContainer = RootBorder;
        _swipeButtonCandidate = null;
        var inputSource = e.OriginalSource as DependencyObject;
        foreach (var manipulator in e.Manipulators)
        {
            inputSource = RootBorder.InputHitTest(manipulator.GetPosition(RootBorder)) as DependencyObject
                          ?? inputSource;
            break;
        }

        _swipeStartedInItemsScroller = ReferenceEquals(
            FindAncestor<System.Windows.Controls.ScrollViewer>(inputSource),
            ItemsScroller);
        _swipeStartHorizontalOffset = ItemsScroller.HorizontalOffset;
        var canScrollItems = _swipeStartedInItemsScroller &&
                             PageSwipeGesture.HasScrollableItems(ItemsScroller.ScrollableWidth);

        if ((!_config.EnableSwipePages || !_vm.HasMultiplePages) && !canScrollItems ||
            FindAncestor<System.Windows.Controls.Slider>(inputSource) is not null)
        {
            _swipeState = SwipeState.Rejected;
            e.Mode = ManipulationModes.None;
            return;
        }

        _swipeState = SwipeState.Tracking;
        _swipeButtonCandidate = FindAncestor<AdaptiveBarPC.Controls.LongPressButton>(
            inputSource);
        e.Mode = ManipulationModes.Translate;
    }

    private void RootBorder_OnManipulationDelta(
        object sender,
        ManipulationDeltaEventArgs e)
    {
        if (_swipeState is SwipeState.Rejected or SwipeState.Settling or SwipeState.Idle)
            return;

        var translation = e.CumulativeManipulation.Translation;

        if (_swipeState == SwipeState.Tracking)
        {
            var direction = PageSwipeGesture.ClassifyDirection(translation.X, translation.Y);
            if (direction == SwipeDirection.Undecided)
                return;

            if (direction == SwipeDirection.Vertical)
            {
                _swipeState = SwipeState.Rejected;
                return;
            }

            if (_swipeStartedInItemsScroller &&
                PageSwipeGesture.CanScrollItems(
                    _swipeStartHorizontalOffset,
                    ItemsScroller.ScrollableWidth,
                    translation.X))
            {
                _swipeState = SwipeState.ScrollingItems;
            }
            else if (_config.EnableSwipePages && _vm.HasMultiplePages)
            {
                _swipeState = SwipeState.Horizontal;
            }
            else
            {
                _swipeState = SwipeState.Rejected;
                return;
            }

            _swipeButtonCandidate?.CancelActivePress();
            _swipeButtonCandidate = null;
        }

        if (_swipeState == SwipeState.ScrollingItems)
        {
            ItemsScroller.ScrollToHorizontalOffset(
                Math.Clamp(
                    _swipeStartHorizontalOffset - translation.X,
                    0,
                    ItemsScroller.ScrollableWidth));
            return;
        }

        if (ItemsHost.RenderTransform is TranslateTransform translate)
        {
            translate.X = PageSwipeGesture.ApplyEdgeResistance(
                translation.X,
                _vm.CanSwipePreviousPage,
                _vm.CanSwipeNextPage);
        }
    }

    private void RootBorder_OnManipulationCompleted(
        object sender,
        ManipulationCompletedEventArgs e)
    {
        _swipeButtonCandidate = null;

        if (_swipeState != SwipeState.Horizontal)
        {
            _swipeState = SwipeState.Idle;
            return;
        }

        var dx = e.TotalManipulation.Translation.X;
        var velocityX = e.FinalVelocities.LinearVelocity.X * 1000.0;
        var pageDelta = PageSwipeGesture.GetPageDelta(
            dx,
            velocityX,
            Math.Max(PageSwipeGesture.DirectionDeadZoneDip, _config.SwipeThresholdPixels),
            _vm.CanSwipePreviousPage,
            _vm.CanSwipeNextPage);

        if (pageDelta == 0)
            AnimateSwipeBack();
        else
            AnimateCommittedSwipe(pageDelta);
    }

    private void RootBorder_OnManipulationInertiaStarting(
        object sender,
        ManipulationInertiaStartingEventArgs e)
    {
        if (_swipeState == SwipeState.ScrollingItems && _config.EnableAnimations)
            e.TranslationBehavior.DesiredDeceleration =
                PageSwipeGesture.ItemScrollInertiaDecelerationDipPerMsSquared;
    }

    private void AnimateSwipeBack()
    {
        if (ItemsHost.RenderTransform is not TranslateTransform translate)
        {
            _swipeState = SwipeState.Idle;
            return;
        }

        if (!_config.EnableAnimations)
        {
            translate.X = 0;
            _swipeState = SwipeState.Idle;
            return;
        }

        _swipeState = SwipeState.Settling;
        var animation = CreateSwipeAnimation(
            translate.X,
            0,
            PageSwipeGesture.SettleDurationMs,
            EasingMode.EaseOut);
        animation.Completed += (_, _) =>
        {
            if (_swipeState != SwipeState.Settling)
                return;

            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
            _swipeState = SwipeState.Idle;
        };
        translate.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void AnimateCommittedSwipe(int pageDelta)
    {
        if (ItemsHost.RenderTransform is not TranslateTransform translate)
        {
            CommitSwipe(pageDelta);
            _swipeState = SwipeState.Idle;
            return;
        }

        if (!_config.EnableAnimations)
        {
            translate.X = 0;
            CommitSwipe(pageDelta);
            _swipeState = SwipeState.Idle;
            return;
        }

        _swipeState = SwipeState.Settling;
        var direction = pageDelta > 0 ? -1.0 : 1.0;
        var width = Math.Max(160.0, ItemsScroller.ActualWidth);
        var halfDuration = PageSwipeGesture.SettleDurationMs / 2.0;
        var outAnimation = CreateSwipeAnimation(
            translate.X,
            direction * width,
            halfDuration,
            EasingMode.EaseIn);

        outAnimation.Completed += (_, _) =>
        {
            if (_swipeState != SwipeState.Settling)
                return;

            translate.BeginAnimation(TranslateTransform.XProperty, null);
            CommitSwipe(pageDelta);
            translate.X = -direction * width;

            var inAnimation = CreateSwipeAnimation(
                translate.X,
                0,
                halfDuration,
                EasingMode.EaseOut);
            inAnimation.Completed += (_, _) =>
            {
                if (_swipeState != SwipeState.Settling)
                    return;

                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = 0;
                _swipeState = SwipeState.Idle;
            };
            translate.BeginAnimation(TranslateTransform.XProperty, inAnimation);
        };

        translate.BeginAnimation(TranslateTransform.XProperty, outAnimation);
    }

    private void CommitSwipe(int pageDelta)
    {
        _suppressNextLayoutAnimation = true;
        if (pageDelta > 0)
            _vm.NextPage();
        else
            _vm.PreviousPage();
    }

    private static DoubleAnimation CreateSwipeAnimation(
        double from,
        double to,
        double durationMs,
        EasingMode easingMode) =>
        new()
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = easingMode }
        };

    private void StopSwipeAnimation()
    {
        if (ItemsHost.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
        }

        _swipeState = SwipeState.Idle;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;

            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void AnimateLayoutChange()
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ItemsHost.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation
                    {
                        From = 0.20,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(150),
                        EasingFunction = new QuadraticEase
                        {
                            EasingMode = EasingMode.EaseOut
                        }
                    });

                if (ItemsHost.RenderTransform is TranslateTransform translate)
                {
                    translate.BeginAnimation(
                        TranslateTransform.XProperty,
                        new DoubleAnimation
                        {
                            From = 14,
                            To = 0,
                            Duration = TimeSpan.FromMilliseconds(170),
                            EasingFunction = new QuadraticEase
                            {
                                EasingMode = EasingMode.EaseOut
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Layout animation failed.", ex);
            }
        });
    }

    private void UpdateOverflowFades()
    {
        if (ItemsScroller == null)
            return;

        var scrollable = ItemsScroller.ScrollableWidth;
        var width = ItemsScroller.ActualWidth;

        if (scrollable <= 1.0 || width <= 36.0)
        {
            if (ItemsScroller.OpacityMask != null)
                ItemsScroller.OpacityMask = null;
            return;
        }

        var showLeft = ItemsScroller.HorizontalOffset > 1.0;
        var showRight = ItemsScroller.HorizontalOffset < (scrollable - 1.0);

        if (!showLeft && !showRight)
        {
            if (ItemsScroller.OpacityMask != null)
                ItemsScroller.OpacityMask = null;
            return;
        }

        var leftFraction = showLeft ? Math.Min(0.2, 18.0 / width) : 0.0;
        var rightFraction = showRight ? Math.Max(0.8, 1.0 - (18.0 / width)) : 1.0;

        var mask = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 0)
        };

        if (showLeft)
        {
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, leftFraction));
        }
        else
        {
            mask.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
        }

        if (showRight)
        {
            mask.GradientStops.Add(new GradientStop(Colors.Black, rightFraction));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        }
        else
        {
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1.0));
        }

        if (mask.CanFreeze)
            mask.Freeze();

        ItemsScroller.OpacityMask = mask;
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        _vm.ClipboardHistory.ProcessWindowMessage(msg);

        if (_config.NoActivate && msg == AdaptiveBarPC.Interop.NativeWindow.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(AdaptiveBarPC.Interop.NativeWindow.MA_NOACTIVATE);
        }

        if (_appBarService != null && msg == _appBarService.CallbackMessage)
        {
            if ((int)wParam == AdaptiveBarPC.Interop.AppBarInterop.ABN_POSCHANGED)
            {
                _appBarService.UpdatePosition();
                handled = true;
                return IntPtr.Zero;
            }
        }

        if (msg is WM_DPICHANGED or WM_DISPLAYCHANGE)
        {
            if (_appBarService != null)
                _appBarService.UpdatePosition();
            else
                MonitorDockService.Dock(this, hwnd, _config, _logger);

            handled = true;
            return IntPtr.Zero;
        }

        if (AdaptiveBarPC.Interop.NativeWindow.IsPointerMessage(msg))
        {
            var pointerId = AdaptiveBarPC.Interop.NativeWindow.GetPointerId(wParam);
            var wasTouch = _touchPointers.Contains(pointerId);
            var isTouch = false;
            var isTerminal = AdaptiveBarPC.Interop.NativeWindow.IsTerminalPointerMessage(msg);

            if (AdaptiveBarPC.Interop.NativeWindow.GetPointerType(pointerId, out var pType))
            {
                if (pType == AdaptiveBarPC.Interop.NativeWindow.PT_TOUCH || pType == AdaptiveBarPC.Interop.NativeWindow.PT_PEN)
                    isTouch = true;
            }
            else if (wasTouch)
            {
                // The pointer may already be gone on UP/LEAVE/CAPTURECHANGED.
                // Keep consuming a sequence that was previously confirmed as touch.
                isTouch = true;
            }

            if (isTouch)
            {
                if (isTerminal)
                    _touchPointers.Remove(pointerId);
                else
                    _touchPointers.Add(pointerId);

                handled = true;

                if (msg == AdaptiveBarPC.Interop.NativeWindow.WM_POINTERACTIVATE)
                {
                    return new IntPtr(_config.NoActivate
                        ? AdaptiveBarPC.Interop.NativeWindow.PA_NOACTIVATE
                        : AdaptiveBarPC.Interop.NativeWindow.PA_ACTIVATE);
                }

                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private enum SwipeState
    {
        Idle,
        Tracking,
        Horizontal,
        ScrollingItems,
        Rejected,
        Settling
    }
}

internal enum SwipeDirection
{
    Undecided,
    Horizontal,
    Vertical
}

internal static class PageSwipeGesture
{
    internal const double DirectionDeadZoneDip = 8.0;
    internal const double HorizontalDominanceRatio = 1.3;
    internal const double DefaultCommitDistanceDip = 40.0;
    internal const double CommitVelocityDipPerSecond = 550.0;
    internal const double ItemScrollInertiaDecelerationDipPerMsSquared = 0.002;
    internal const double SettleDurationMs = 160.0;
    internal const double EdgeResistanceFactor = 0.32;

    internal static SwipeDirection ClassifyDirection(double dx, double dy)
    {
        var absX = Math.Abs(dx);
        var absY = Math.Abs(dy);
        if (Math.Max(absX, absY) < DirectionDeadZoneDip)
            return SwipeDirection.Undecided;

        if (absX > absY * HorizontalDominanceRatio)
            return SwipeDirection.Horizontal;

        if (absY > absX * HorizontalDominanceRatio)
            return SwipeDirection.Vertical;

        return SwipeDirection.Undecided;
    }

    internal static bool HasScrollableItems(double scrollableWidth) =>
        scrollableWidth > 0.5;

    internal static bool CanScrollItems(
        double horizontalOffset,
        double scrollableWidth,
        double translationX) =>
        translationX < 0
            ? horizontalOffset < scrollableWidth - 0.5
            : translationX > 0 && horizontalOffset > 0.5;

    internal static int GetPageDelta(
        double dx,
        double velocityX,
        double commitDistanceDip,
        bool canPrevious,
        bool canNext)
    {
        var direction = Math.Abs(dx) >= commitDistanceDip
            ? Math.Sign(dx)
            : Math.Abs(velocityX) >= CommitVelocityDipPerSecond
                ? Math.Sign(velocityX)
                : 0;

        if (direction < 0 && canNext)
            return 1;

        if (direction > 0 && canPrevious)
            return -1;

        return 0;
    }

    internal static double ApplyEdgeResistance(
        double dx,
        bool canPrevious,
        bool canNext)
    {
        var blocked = (dx > 0 && !canPrevious) || (dx < 0 && !canNext);
        return blocked ? dx * EdgeResistanceFactor : dx;
    }
}
