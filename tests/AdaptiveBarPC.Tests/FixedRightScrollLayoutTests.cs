using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AdaptiveBarPC.Controls;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class FixedRightScrollLayoutTests
{
    [Fact]
    public void FixedRight_ShrinksViewport_CurrentPageStillHasHorizontalExtent()
    {
        RunInSta(() =>
        {
            var strip = new AdaptiveBarStripPanel();
            for (var i = 0; i < 5; i++)
                strip.Children.Add(new Border { Width = 100, Height = 40 });

            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Content = strip
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            Grid.SetColumn(scroller, 1);
            grid.Children.Add(scroller);

            var window = new Window { Width = 600, Height = 80, Content = grid };
            window.Show();
            Pump();

            Assert.True(scroller.ViewportWidth > 200);
            Assert.True(scroller.ExtentWidth >= 500);
            Assert.True(scroller.ScrollableWidth > 150);

            scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth);
            Pump();
            Assert.True(scroller.HorizontalOffset > 150);

            window.Close();
        });
    }


    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
