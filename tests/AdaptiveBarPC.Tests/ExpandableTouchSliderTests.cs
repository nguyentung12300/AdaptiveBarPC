using AdaptiveBarPC.Controls;
using AdaptiveBarPC.ViewModels;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class ExpandableTouchSliderTests
{
    [Fact]
    public void TapFromCollapsed_ExpandsWithoutEnteringDrag()
    {
        var state = ExpandableSliderInteraction.PointerDown(ExpandableSliderState.Collapsed);

        Assert.Equal(ExpandableSliderState.ExpandedIdle, state);
    }

    [Fact]
    public void DragThreshold_RequiresSixDip()
    {
        var state = ExpandableSliderState.ExpandedIdle;

        Assert.Equal(
            ExpandableSliderState.ExpandedIdle,
            ExpandableSliderInteraction.PointerMove(state, 5.9, ExpandableTouchSlider.DragThresholdDip));
        Assert.Equal(
            ExpandableSliderState.Dragging,
            ExpandableSliderInteraction.PointerMove(state, 6.0, ExpandableTouchSlider.DragThresholdDip));
    }

    [Fact]
    public void ExpansionTransition_DoesNotChangeLogicalValue()
    {
        const double value = 63.0;

        _ = ExpandableSliderInteraction.PointerDown(ExpandableSliderState.Collapsed);

        Assert.Equal(63.0, value);
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(50, 50)]
    [InlineData(120, 100)]
    public void MapPosition_ClampsAndMapsAcrossTrack(double x, double expected)
    {
        Assert.Equal(expected, ExpandableSliderInteraction.MapPosition(x, 100, 0, 100));
    }

    [Fact]
    public void SliderSetter_FiresOnlyForMeaningfulChange()
    {
        var calls = 0;
        var vm = new SliderItemViewModel("Volume", 160, 0, 100, 50, _ => calls++);

        vm.Value = 50.005;
        vm.Value = 50.02;

        Assert.Equal(1, calls);
    }

    [Fact]
    public void SliderSetter_CoalescesWhileInteracting_AndFlushesFinalValue()
    {
        var calls = 0;
        var written = 0.0;
        var vm = new SliderItemViewModel("Volume", 160, 0, 100, 50, value =>
        {
            calls++;
            written = value;
        });

        vm.IsInteracting = true;
        vm.Value = 60;
        vm.Value = 70;
        vm.Value = 80;

        Assert.Equal(0, calls);

        vm.IsInteracting = false;

        Assert.Equal(1, calls);
        Assert.Equal(80, written);
    }

    [Fact]
    public void IdleElapsed_CollapsesOnlyIdleState()
    {
        Assert.Equal(
            ExpandableSliderState.Collapsed,
            ExpandableSliderInteraction.IdleElapsed(ExpandableSliderState.ExpandedIdle));
        Assert.Equal(
            ExpandableSliderState.Dragging,
            ExpandableSliderInteraction.IdleElapsed(ExpandableSliderState.Dragging));
    }
}
