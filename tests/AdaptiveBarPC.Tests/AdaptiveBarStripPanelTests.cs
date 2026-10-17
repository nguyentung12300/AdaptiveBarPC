using System;
using System.IO;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class AdaptiveBarStripPanelTests
{
    [Fact]
    public void FlexibleOverflow_UsesMeasuredViewportForExtentAndArrange()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        var source = File.ReadAllText(Path.Combine(
            dir, "src", "AdaptiveBarPC.App", "Controls", "AdaptiveBarStripPanel.cs"));

        Assert.Contains("_viewportWidth = viewportWidth;", source);
        Assert.Contains("totalWidth += targetWidth;", source);
        Assert.Contains("double targetWidth = _viewportWidth;", source);
    }
}
