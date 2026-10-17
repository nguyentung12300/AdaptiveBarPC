using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace AdaptiveBarPC.Designer;

public partial class MainWindow
{
    private bool _fitPreview = true;
    private bool _updatingZoom;
    private bool _fitQueued;
    private bool _spacePan;
    private Point? _panStart;
    private Vector _panOffset;
    internal double CanvasZoom => PreviewScale.ScaleX;
    internal bool IsFitPreview => _fitPreview;
    internal bool IsCanvasPanning => _panStart is not null;
    private bool PanMode => _spacePan || PanModeButton?.IsChecked == true;

    private void Studio_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ExplorerColumn is null) return;
        ExplorerColumn.Width = new GridLength(ActualWidth < 1200 ? 240 : 332);
        InspectorColumn.Width = new GridLength(ActualWidth < 1200 ? 285 : 334);
        ContextBreadcrumb.Visibility = ActualWidth < 1300 ? Visibility.Collapsed : Visibility.Visible;
        var tabLabels = ActualWidth < 1200 ? Visibility.Collapsed : Visibility.Visible;
        ExplorerTabLabel.Visibility = LibraryTabLabel.Visibility = InputTabLabel.Visibility = tabLabels;
    }

    private void CanvasViewport_OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueFitPreview();
    private void HardwareFrame_OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueFitPreview();

    private void QueueFitPreview()
    {
        if (_fitQueued || CanvasViewport is null) return;
        _fitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _fitQueued = false;
            CanvasExtent.MinWidth = Math.Max(0, CanvasViewport.ViewportWidth);
            CanvasExtent.MinHeight = Math.Max(0, CanvasViewport.ViewportHeight);
            if (_fitPreview) FitPreview();
        }));
    }

    internal void FitPreview()
    {
        _fitPreview = true;
        var scale = DesignerCoordinates.FitScale(CanvasViewport.ViewportWidth, CanvasViewport.ViewportHeight,
            HardwareFrame.ActualWidth, HardwareFrame.ActualHeight);
        if (scale is not { } value) return;
        SetCanvasZoom(value, fit: true);
    }

    // The anchor is in viewport DIPs. TranslatePoint includes the ScrollViewer offsets,
    // frame centering, and the layout scale; there is no second coordinate system to maintain.
    internal void SetCanvasZoom(double scale, Point? anchor = null, bool fit = false)
    {
        if (!double.IsFinite(scale)) return;
        var position = anchor ?? new Point(CanvasViewport.ViewportWidth / 2, CanvasViewport.ViewportHeight / 2);
        var logical = CanvasViewport.TranslatePoint(position, HardwareFrame);
        _fitPreview = fit;
        PreviewScale.ScaleX = PreviewScale.ScaleY = Math.Clamp(scale, 0.1, 2);
        CanvasViewport.UpdateLayout();
        var translated = HardwareFrame.TranslatePoint(logical, CanvasViewport);
        CanvasViewport.ScrollToHorizontalOffset(CanvasViewport.HorizontalOffset + translated.X - position.X);
        CanvasViewport.ScrollToVerticalOffset(CanvasViewport.VerticalOffset + translated.Y - position.Y);
        _updatingZoom = true;
        CurrentZoomItem.Content = $"{(fit ? "Fit · " : "")}{CanvasZoom * 100:0}%";
        ZoomPicker.SelectedItem = CurrentZoomItem;
        _updatingZoom = false;
        ZoomOutButton.IsEnabled = CanvasZoom > 0.1;
        ZoomInButton.IsEnabled = CanvasZoom < 2;
    }

    private void ZoomPicker_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingZoom && ZoomPicker.SelectedItem is ComboBoxItem { Tag: "fit" })
        {
            FitPreview();
            return;
        }
        if (!_updatingZoom && ZoomPicker.SelectedItem is ComboBoxItem { Tag: string value } &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            SetCanvasZoom(scale);
    }

    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) => SetCanvasZoom(CanvasZoom * 1.25);
    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) => SetCanvasZoom(CanvasZoom / 1.25);
    private void Fit_OnClick(object sender, RoutedEventArgs e) => FitPreview();

    private void CanvasViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetCanvasZoom(CanvasZoom * (e.Delta > 0 ? 1.25 : 0.8), e.GetPosition(CanvasViewport));
        e.Handled = true;
    }

    private void CanvasMode_OnChecked(object sender, RoutedEventArgs e) => UpdateCanvasCursor();

    private void UpdateCanvasCursor()
    {
        if (CanvasViewport is null) return;
        CanvasViewport.Cursor = PanMode ? Cursors.Hand : Cursors.Arrow;
        CanvasViewport.ForceCursor = PanMode;
        CanvasViewport.PanningMode = PanMode ? PanningMode.Both : PanningMode.None;
    }

    internal bool TryHandleCanvasKey(Key key, bool down, DependencyObject? source)
    {
        if (key == Key.Escape)
        {
            if (!PanMode && _panStart is null) return false;
            EndCanvasPan();
            _spacePan = false;
            SelectModeButton.IsChecked = true;
            UpdateCanvasCursor();
            return true;
        }
        if (key != Key.Space || down && (FindAncestor<TextBoxBase>(source) is not null ||
            FindAncestor<PasswordBox>(source) is not null || FindAncestor<ComboBox>(source) is not null ||
            FindAncestor<ButtonBase>(source) is not null || FindAncestor<MenuItem>(source) is not null)) return false;
        if (!down && !_spacePan) return false;
        _spacePan = down;
        if (!down) EndCanvasPan();
        UpdateCanvasCursor();
        return true;
    }

    private void Designer_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (TryHandleCanvasKey(e.Key, false, e.OriginalSource as DependencyObject)) e.Handled = true;
        if (e.Key == Key.Space && FindAncestor<RepeatButton>(e.OriginalSource as DependencyObject) is not null)
            _viewModel.EndHistoryGesture();
    }

    private void CanvasViewport_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!PanMode || FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null) return;
        BeginCanvasPan(e.GetPosition(CanvasViewport));
        e.Handled = true;
    }

    internal void BeginCanvasPan(Point position)
    {
        ResetPendingDrag();
        _panStart = position;
        _panOffset = new Vector(CanvasViewport.HorizontalOffset, CanvasViewport.VerticalOffset);
        CanvasViewport.CaptureMouse();
    }

    internal void MoveCanvasPan(Point position)
    {
        if (_panStart is not { } start) return;
        var delta = position - start;
        CanvasViewport.ScrollToHorizontalOffset(_panOffset.X - delta.X);
        CanvasViewport.ScrollToVerticalOffset(_panOffset.Y - delta.Y);
    }

    private void CanvasViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is null) return;
        MoveCanvasPan(e.GetPosition(CanvasViewport));
        e.Handled = true;
    }

    private void CanvasViewport_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panStart is null) return;
        EndCanvasPan();
        e.Handled = true;
    }

    private void CanvasViewport_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, CanvasViewport)) EndCanvasPan();
    }

    internal void EndCanvasPan()
    {
        _panStart = null;
        if (CanvasViewport.IsMouseCaptured) CanvasViewport.ReleaseMouseCapture();
        _viewModel?.EndHistoryGesture();
    }
}
