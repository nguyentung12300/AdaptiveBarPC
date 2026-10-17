using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public class SafeCleanupTests
{
    [Theory]
    [InlineData(true, "0DBCB8018ED3367269D1E52B24CF777AD37790BD9B9FDA7347CF3C8E6788C3C5")]
    [InlineData(false, "BF5025F2D055F9C7E354BA422CAFC374325616F9972DCA9E206A414394BD75B7")]
    public void FallbackProfilesKeepOriginalJsonAndIndependentMutableItems(bool desktop, string expectedHash)
    {
        ProfileDefinition Create() => desktop
            ? ProfileResolver.CreateDefaultDesktopProfile()
            : ProfileResolver.CreateGenericProfile(new ForegroundAppInfo(IntPtr.Zero, "notepad.exe", "Notes"));

        var profile = Create();
        var originalJson = JsonSerializer.SerializeToUtf8Bytes(profile);
        // Frozen from the pre-cleanup fallback, including defaults and item ordering.
        var hash = Convert.ToHexString(SHA256.HashData(originalJson));
        Assert.True(hash == expectedHash, $"Fallback JSON SHA256 (desktop={desktop}): {hash}");

        var volume = profile.Pages[0].Items.Single(item => item.Action?.Type == "volume");
        volume.Label = "Changed";
        volume.Action!.Value = "Changed";
        profile.Theme.Accent = "#FF000000";
        foreach (var page in profile.Pages.Skip(1))
        {
            var otherVolume = page.Items.Single(item => item.Action?.Type == "volume");
            Assert.Equal("MASTER VOL", otherVolume.Label);
            Assert.Null(otherVolume.Action!.Value);
        }
        Assert.Equal(originalJson, JsonSerializer.SerializeToUtf8Bytes(Create()));
    }

    [Fact]
    public void DynamicCommandsKeepPrecedenceReplacementAndEmptyToggleBehavior()
    {
        RunInSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "AdaptiveBarCommands_" + Guid.NewGuid().ToString("N"));
            try
            {
                var calls = new List<string>();
                var actions = new ActionExecutor(new AppConfig(), new AppLogger(directory));
                actions.SetPageNavigator(value => calls.Add("page:" + value));
                using var provider = new MockDynamicWidgetProvider();
                provider.SetItems([
                    new() { Id = "button", OnClick = () => calls.Add("click"),
                        OnSecondaryClick = () => calls.Add("hold"),
                        Action = new() { Type = "page", Value = "ignored" },
                        SecondaryAction = new() { Type = "page", Value = "ignored" } },
                    new() { Id = "toggle", Type = WidgetTypes.Toggle,
                        OnToggle = value => calls.Add("toggle:" + value),
                        Action = new() { Type = "page", Value = "ignored" } },
                    new() { Id = "empty", Type = WidgetTypes.Toggle }
                ]);
                using var vm = new DynamicListItemViewModel(provider, actions: actions, dispatcher: Dispatcher.CurrentDispatcher);
                var button = Assert.IsType<ButtonItemViewModel>(vm.Items[0]);
                var toggle = Assert.IsType<ToggleItemViewModel>(vm.Items[1]);
                var empty = Assert.IsType<ToggleItemViewModel>(vm.Items[2]);
                Assert.Null(empty.ToggleCommand);
                button.Command.Execute(null);
                button.SecondaryCommand!.Execute(null);
                toggle.ToggleCommand.Execute(null);
                Assert.Equal(["click", "hold", "toggle:True"], calls);

                provider.SetItems([
                    new() { Id = "button", Action = new() { Type = "page", Value = "primary" },
                        SecondaryAction = new() { Type = "page", Value = "secondary" } },
                    new() { Id = "toggle", Type = WidgetTypes.Toggle,
                        Action = new() { Type = "page", Value = "toggle" } },
                    new() { Id = "empty", Type = WidgetTypes.Toggle }
                ]);
                Assert.Same(button, vm.Items[0]);
                Assert.Same(toggle, vm.Items[1]);
                calls.Clear();
                button.Command.Execute(null);
                button.SecondaryCommand!.Execute(null);
                toggle.ToggleCommand.Execute(null);
                Assert.NotNull(empty.ToggleCommand);
                empty.ToggleCommand.Execute(null);
                Assert.True(toggle.IsChecked);
                Assert.True(empty.IsChecked);
                Assert.Equal(["page:primary", "page:secondary", "page:toggle"], calls);

                provider.SetItems([new() { Id = "button" }, new() { Id = "toggle", Type = WidgetTypes.Toggle }]);
                calls.Clear();
                button.Command.Execute(null);
                Assert.Null(button.SecondaryCommand);
                toggle.ToggleCommand.Execute(null);
                Assert.True(toggle.IsChecked);
                Assert.Empty(calls);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
    }
}
