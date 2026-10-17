using System.IO;
using System.Threading;
using AdaptiveBarPC.Interop;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using Xunit;

namespace AdaptiveBarPC.Tests;

public class VoiceMeeterTests
{
    [Fact]
    public void SetVolume_To20Percent_UpdatesBus0Gain_AndStaysConsistent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AdaptiveBarPC_VM_Test");
        Directory.CreateDirectory(tempDir);
        var logger = new AppLogger(tempDir);
        var config = new AppConfig
        {
            AudioBackend = "voicemeeter",
            VoiceMeeterBusIndex = 0
        };

        using var provider = new VoiceMeeterAudioProvider(config, logger);

        // Simulate dragging slider to 20%
        provider.SetMasterVolumePercent(20.0);

        // Immediate read must return 20% and not be overwritten with stale data
        Assert.Equal(20.0, provider.GetMasterVolumePercent());

        // Wait 300ms (window where GetParameterFloat could be stale)
        Thread.Sleep(300);
        Assert.Equal(20.0, provider.GetMasterVolumePercent());

        // Wait another 500ms for VoiceMeeter engine to settle
        Thread.Sleep(500);
        Assert.Equal(20.0, provider.GetMasterVolumePercent(), precision: 1);

        // Verify with low-level interop that Bus[0].Gain in VoiceMeeter matches ~ -13.98 dB (approx -14.0 dB)
        using var interop = new VoiceMeeterInterop();
        interop.Login();
        interop.IsParametersDirty();
        var rc = interop.GetParameterFloat("Bus[0].Gain", out var actualGainDb);
        Assert.Equal(0, rc);

        // 20% is 20*log10(0.20) = -13.9794 dB. Allow tolerance of 0.5 dB
        Assert.InRange(actualGainDb, -14.5f, -13.5f);
    }
}
