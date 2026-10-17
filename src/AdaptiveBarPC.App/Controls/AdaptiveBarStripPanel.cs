using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdaptiveBarPC.ViewModels;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace AdaptiveBarPC.Controls;

/// <summary>
/// Horizontal strip panel for AdaptiveBarPC item strip.
/// Measures non-flexible items at their desired width, and expands any flexible item
/// (e.g. MediaItemViewModel with Width=NaN when media is available) to consume the remaining width.
/// When no media is available, media collapses to its compact natural width without stretching.
/// </summary>
public sealed class AdaptiveBarStripPanel : Panel
{
    private ScrollViewer? _scrollViewer;
    private double _viewportWidth = double.PositiveInfinity;

    public AdaptiveBarStripPanel()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindParent<ScrollViewer>(this);
        if (_scrollViewer != null)
        {
            _scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
            _scrollViewer = null;
        }
    }

    private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        InvalidateMeasure();
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            child = VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child);
            if (child is T parent)
                return parent;
        }
        return null;
    }

    private static bool IsFlexible(UIElement element)
    {
        if (element is ContentPresenter cp)
        {
            if (cp.Content is MediaItemViewModel mediaVm)
            {
                return double.IsNaN(mediaVm.Width) && mediaVm.IsAvailable;
            }
            if (cp.Content is BarItemViewModel barItem && double.IsNaN(barItem.Width))
            {
                return true;
            }
        }
        return false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double nonFlexWidth = 0;
        double maxHeight = 0;
        var flexElements = new List<UIElement>();

        foreach (UIElement child in InternalChildren)
        {
            if (child == null) continue;

            if (IsFlexible(child))
            {
                flexElements.Add(child);
            }
            else
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                nonFlexWidth += child.DesiredSize.Width;
                maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
            }
        }

        double totalWidth = nonFlexWidth;

        if (flexElements.Count > 0)
        {
            double viewportWidth = availableSize.Width;
            if (double.IsPositiveInfinity(viewportWidth))
            {
                if (_scrollViewer == null)
                    _scrollViewer = FindParent<ScrollViewer>(this);

                if (_scrollViewer != null && _scrollViewer.ActualWidth > 0)
                    viewportWidth = _scrollViewer.ActualWidth;
            }

            double remainingWidth = 0;
            if (!double.IsPositiveInfinity(viewportWidth))
            {
                remainingWidth = Math.Max(0, viewportWidth - nonFlexWidth);
            }

            _viewportWidth = viewportWidth;

            double widthPerFlex = remainingWidth / flexElements.Count;

            foreach (var flex in flexElements)
            {
                flex.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                double naturalWidth = flex.DesiredSize.Width;
                double targetWidth = Math.Max(naturalWidth, widthPerFlex);
                flex.Measure(new Size(targetWidth, availableSize.Height));

                totalWidth += targetWidth;
                maxHeight = Math.Max(maxHeight, flex.DesiredSize.Height);
            }
        }

        double finalWidth = totalWidth;
        double finalHeight = double.IsPositiveInfinity(availableSize.Height)
            ? maxHeight
            : Math.Max(maxHeight, availableSize.Height);

        return new Size(finalWidth, finalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double nonFlexWidth = 0;
        var flexElements = new List<UIElement>();

        foreach (UIElement child in InternalChildren)
        {
            if (child == null) continue;
            if (IsFlexible(child))
            {
                flexElements.Add(child);
            }
            else
            {
                nonFlexWidth += child.DesiredSize.Width;
            }
        }

        double targetWidth = _viewportWidth;
        if (double.IsPositiveInfinity(targetWidth))
            targetWidth = finalSize.Width;

        double remainingWidth = Math.Max(0, targetWidth - nonFlexWidth);
        double widthPerFlex = flexElements.Count > 0 ? remainingWidth / flexElements.Count : 0;

        double x = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child == null) continue;

            double width;
            if (IsFlexible(child))
            {
                width = Math.Max(child.DesiredSize.Width, widthPerFlex);
            }
            else
            {
                width = child.DesiredSize.Width;
            }

            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
