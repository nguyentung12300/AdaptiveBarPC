using System.IO;
using System.Text.Json;
using System.Windows;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public class BrowserProfileTests
{
    [Fact]
    public void BrowserProfile_HasMediaWidgetWithCollapseWhenUnavailable()
    {
        var baseDir = AppContext.BaseDirectory;
        var profilePath = Path.Combine(baseDir, "Profiles", "10-browser.json");
        Assert.True(File.Exists(profilePath), $"Profile not found at {profilePath}");

        var json = File.ReadAllText(profilePath);
        var profile = JsonSerializer.Deserialize<ProfileDefinition>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(profile);
        Assert.Equal("browser", profile.Id);
        Assert.NotEmpty(profile.Pages);

        foreach (var page in profile.Pages)
        {
            var mediaItem = page.Items.FirstOrDefault(i => i.Type.Equals("media", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(mediaItem);
            Assert.True(mediaItem.CollapseWhenUnavailable || mediaItem.AutoCollapse);
            Assert.Equal(0, mediaItem.Width);

            // System controls
            var volume = page.Items.FirstOrDefault(i => i.Type.Equals("slider", StringComparison.OrdinalIgnoreCase) && i.System);
            Assert.NotNull(volume);
            var mute = page.Items.FirstOrDefault(i => i.Type.Equals("button", StringComparison.OrdinalIgnoreCase) && i.System);
            Assert.NotNull(mute);
        }
    }

    [Fact]
    public void DesktopProfile_RetainsDefaultCollapseWhenUnavailableFalse()
    {
        var baseDir = AppContext.BaseDirectory;
        var profilePath = Path.Combine(baseDir, "Profiles", "00-desktop.json");
        Assert.True(File.Exists(profilePath), $"Profile not found at {profilePath}");

        var json = File.ReadAllText(profilePath);
        var profile = JsonSerializer.Deserialize<ProfileDefinition>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(profile);
        var mainPage = profile.Pages.FirstOrDefault(p => p.Id == "main");
        Assert.NotNull(mainPage);

        var mediaItem = mainPage.Items.FirstOrDefault(i => i.Type.Equals("media", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mediaItem);
        Assert.False(mediaItem.CollapseWhenUnavailable);
        Assert.False(mediaItem.AutoCollapse);
    }

    [Fact]
    public void MediaItemViewModel_VisibilityReflectsCollapseAndAvailability()
    {
        var logger = new AppLogger(Path.Combine(Path.GetTempPath(), "AdaptiveBarTest_" + Guid.NewGuid()));
        var mediaService = new MediaSessionService(logger);

        // When CollapseWhenUnavailable is true:
        // By default, media is not available on test runner machine.
        using var collapsingVm = new MediaItemViewModel(mediaService, width: double.NaN, collapseWhenUnavailable: true);
        Assert.False(collapsingVm.IsAvailable);
        Assert.Equal(Visibility.Collapsed, collapsingVm.Visibility);

        // When CollapseWhenUnavailable is false (Desktop):
        using var desktopVm = new MediaItemViewModel(mediaService, width: double.NaN, collapseWhenUnavailable: false);
        Assert.False(desktopVm.IsAvailable);
        Assert.Equal(Visibility.Visible, desktopVm.Visibility);
    }
}
