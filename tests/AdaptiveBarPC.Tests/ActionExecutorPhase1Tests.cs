using System.IO;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC.Tests;

public sealed class ActionExecutorPhase1Tests
{
    [Fact]
    public void VolumeAndMute_RouteToConfiguredAudioProvider()
    {
        using var temp = new TempFolder();
        var audio = new FakeAudioProvider { MasterVolume = 50 };
        var executor = new ActionExecutor(new AppConfig(), new AppLogger(temp.Path), audio);

        executor.Execute(new ActionDefinition { Type = "volume", Value = "up" });
        Assert.Equal(55, audio.MasterVolume);

        executor.Execute(new ActionDefinition { Type = "media", Value = "volume_down" });
        Assert.Equal(50, audio.MasterVolume);

        executor.Execute(new ActionDefinition { Type = "mute" });
        Assert.Equal(1, audio.ToggleMuteCount);
    }

    [Fact]
    public void InvalidActions_FailSafely()
    {
        using var temp = new TempFolder();
        var executor = new ActionExecutor(
            new AppConfig { AllowLaunchActions = false },
            new AppLogger(temp.Path));

        executor.Execute(null);
        executor.Execute(new ActionDefinition());
        executor.Execute(new ActionDefinition { Type = "unknown", Value = "anything" });
        executor.Execute(new ActionDefinition { Type = "url", Value = "not a url" });
        executor.Execute(new ActionDefinition { Type = "volume", Value = "sideways" });
    }

    [Fact]
    public void PageAction_NavigatesToSpecificPageThroughRuntimeExecutor()
    {
        RunInSta(() =>
        {
            using var temp = new TempFolder();
            File.WriteAllText(Path.Combine(temp.Path, "00-test.json"), """
            {
              "id": "test",
              "displayName": "Test",
              "isDefault": true,
              "pages": [
                {
                  "id": "main",
                  "displayName": "Main",
                  "items": [
                    { "type": "button", "label": "Go", "action": { "type": "page", "value": "settings" } }
                  ]
                },
                {
                  "id": "settings",
                  "displayName": "Settings",
                  "items": []
                }
              ]
            }
            """);

            var logger = new AppLogger(temp.Path);
            var config = new AppConfig();
            using var foreground = new ForegroundAppService(logger);
            using var profiles = new ProfileService(temp.Path, logger);
            using var audio = new FakeAudioProvider();
            using var media = new MediaSessionService(logger);
            var executor = new ActionExecutor(config, logger, audio, media);
            using var vm = new MainViewModel(config, foreground, profiles, audio, executor, media, logger);

            vm.ApplyForegroundApp(new ForegroundAppInfo(
                IntPtr.Zero, "", "", null, "Desktop", null, "Progman"));

            var button = Assert.IsType<ButtonItemViewModel>(Assert.Single(vm.Items));
            button.Command.Execute(null);

            Assert.Equal("Settings", vm.CurrentPageName);
        });
    }

    private sealed class FakeAudioProvider : IAudioProvider
    {
        public double MasterVolume { get; set; }
        public int ToggleMuteCount { get; private set; }

        public double GetMasterVolumePercent() => MasterVolume;
        public void SetMasterVolumePercent(double percent) => MasterVolume = percent;
        public void ToggleMute() => ToggleMuteCount++;
        public bool GetMute() => false;
        public void Dispose() { }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AdaptiveBarPC_ActionEngine_" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }

}

