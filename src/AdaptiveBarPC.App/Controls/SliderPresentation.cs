using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AdaptiveBarPC.Controls;

/// <summary>Reuses slider visuals without exposing a second interactive automation control.</summary>
public sealed class SliderPresentation : Slider
{
    public SliderPresentation()
    {
        Focusable = false;
        IsTabStop = false;
        IsHitTestVisible = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PresentationPeer(this);

    private sealed class PresentationPeer(SliderPresentation owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
