using AdaptiveBarPC;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class PageSwipeGestureTests
{
    [Fact]
    public void DirectionLock_UsesDeadZoneAndHorizontalDominance()
    {
        Assert.Equal(SwipeDirection.Undecided, PageSwipeGesture.ClassifyDirection(5, 1));
        Assert.Equal(SwipeDirection.Horizontal, PageSwipeGesture.ClassifyDirection(12, 4));
        Assert.Equal(SwipeDirection.Vertical, PageSwipeGesture.ClassifyDirection(4, 12));
    }

    [Fact]
    public void ScrollableStrip_DetectsOverflow()
    {
        Assert.True(PageSwipeGesture.HasScrollableItems(160));
        Assert.False(PageSwipeGesture.HasScrollableItems(0));
    }

    [Fact]
    public void ScrollableStrip_HandsOutwardEdgeDragToPageSwipe()
    {
        Assert.False(PageSwipeGesture.CanScrollItems(0, 160, 20));
        Assert.True(PageSwipeGesture.CanScrollItems(0, 160, -20));
        Assert.True(PageSwipeGesture.CanScrollItems(80, 160, 20));
        Assert.True(PageSwipeGesture.CanScrollItems(80, 160, -20));
        Assert.False(PageSwipeGesture.CanScrollItems(160, 160, -20));
        Assert.True(PageSwipeGesture.CanScrollItems(160, 160, 20));
    }

    [Fact]
    public void DistanceCommit_ChoosesOnePageInSwipeDirection()
    {
        Assert.Equal(1, PageSwipeGesture.GetPageDelta(-40, 0, 40, true, true));
        Assert.Equal(-1, PageSwipeGesture.GetPageDelta(40, 0, 40, true, true));
    }

    [Fact]
    public void VelocityCommit_AllowsShortFastFlick()
    {
        Assert.Equal(1, PageSwipeGesture.GetPageDelta(-20, -600, 40, true, true));
        Assert.Equal(-1, PageSwipeGesture.GetPageDelta(20, 600, 40, true, true));
    }

    [Fact]
    public void ShortSlowGesture_DoesNotCommit()
    {
        Assert.Equal(0, PageSwipeGesture.GetPageDelta(-20, -300, 40, true, true));
    }

    [Fact]
    public void BlockedEdge_DoesNotWrap()
    {
        Assert.Equal(0, PageSwipeGesture.GetPageDelta(50, 700, 40, false, true));
        Assert.Equal(0, PageSwipeGesture.GetPageDelta(-50, -700, 40, true, false));
    }

    [Fact]
    public void EdgeResistance_ReducesOnlyBlockedMovement()
    {
        Assert.Equal(64 * PageSwipeGesture.EdgeResistanceFactor,
            PageSwipeGesture.ApplyEdgeResistance(64, false, true));
        Assert.Equal(-64 * PageSwipeGesture.EdgeResistanceFactor,
            PageSwipeGesture.ApplyEdgeResistance(-64, true, false));
        Assert.Equal(64, PageSwipeGesture.ApplyEdgeResistance(64, true, true));
    }

    [Fact]
    public void CommitResult_IsAlwaysAtMostOnePage()
    {
        Assert.Equal(1, Math.Abs(PageSwipeGesture.GetPageDelta(-1000, -5000, 40, true, true)));
        Assert.Equal(1, Math.Abs(PageSwipeGesture.GetPageDelta(1000, 5000, 40, true, true)));
    }
}
