using System.IO;
using System.Text.Json;
using System.Windows;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using AdaptiveBarPC.Services.Audio;
using AdaptiveBarPC.Services.HardwareMonitor;
using AdaptiveBarPC.ViewModels;

namespace AdaptiveBarPC;

public partial class App : System.Windows.Application
{
    private MediaSessionService? _mediaSessionService;
    private IAudioProvider? _audioProvider;
    private AppLogger? _logger;
    private MainViewModel? _viewModel;
    private ProfileService? _profileService;
    private WidgetProviderRegistry? _providerRegistry;
    private HardwareSensorProvider? _hardwareProvider;
    private WindowsAudioSessionService? _audioSessionService;
    private AudioSessionProvider? _audioSessionProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var baseDir = AppContext.BaseDirectory;
        var configPath = Path.Combine(baseDir, "appsettings.json");
        var profilesDir = Path.Combine(baseDir, "Profiles");

        AppConfig config;
        try
        {
            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<AppConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new AppConfig();
        }
        catch
        {
            config = new AppConfig();
        }

        var logger = new AppLogger(Path.Combine(baseDir, "logs"));
        _logger = logger;

        var foreground = new ForegroundAppService(logger);
        var profiles = new ProfileService(profilesDir, logger);
        _profileService = profiles;

        IAudioProvider audio = config.AudioBackend.Equals("voicemeeter", StringComparison.OrdinalIgnoreCase)
            ? new VoiceMeeterAudioProvider(config, logger)
            : new WindowsAudioProvider();
        _audioProvider = audio;

        _mediaSessionService = new MediaSessionService(logger);
        _ = InitializeMediaSessionAsync(_mediaSessionService, logger);
        var actions = new ActionExecutor(config, logger, audio, _mediaSessionService);

        var providerRegistry = new WidgetProviderRegistry();
        _providerRegistry = providerRegistry;

        var hardwareProvider = new HardwareSensorProvider(logger: logger);
        _hardwareProvider = hardwareProvider;
        providerRegistry.RegisterShared("hardware", hardwareProvider);

        var audioSessionService = new WindowsAudioSessionService(logger: logger);
        _audioSessionService = audioSessionService;

        var audioSessionProvider = new AudioSessionProvider(audioSessionService, foreground, logger);
        _audioSessionProvider = audioSessionProvider;
        providerRegistry.RegisterShared("audioSessions", audioSessionProvider);

        var vm = new MainViewModel(config, foreground, profiles, audio, actions, _mediaSessionService, logger, providerRegistry);
        _viewModel = vm;

        var window = new MainWindow(config, vm, logger);
        MainWindow = window;
        MonitorDockService.PrepareWindow(window, config);
        window.Show();
    }

    private static async Task InitializeMediaSessionAsync(MediaSessionService mediaSession, AppLogger logger)
    {
        try
        {
            await mediaSession.InitializeAsync();
        }
        catch (Exception ex)
        {
            logger.Error("Failed to initialize MediaSessionService on startup.", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose the view model first: it stops its DispatcherTimer and releases
        // per-page widgets (media item VMs) while the UI thread is still alive.
        try
        {
            if (MainWindow is MainWindow mw)
            {
                mw.AppBarService?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to dispose AppBarService on exit.", ex);
        }

        try
        {
            _viewModel?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to dispose MainViewModel on exit.", ex);
        }

        // MainViewModel.Dispose() already disposes the ProfileService it owns,
        // so only dispose it here if the view model never got created.
        if (_viewModel is null)
        {
            try
            {
                _profileService?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to dispose ProfileService on exit.", ex);
            }
        }

        try
        {
            _audioProvider?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to dispose AudioProvider on exit.", ex);
        }

        try
        {
            _mediaSessionService?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to dispose MediaSessionService on exit.", ex);
        }

        try
        {
            _audioSessionProvider?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to dispose AudioSessionProvider on exit.", ex);
        }

        try
        {
            _audioSessionService?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to dispose WindowsAudioSessionService on exit.", ex);
        }

        try
        {
            _providerRegistry?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to dispose WidgetProviderRegistry on exit.", ex);
        }

        base.OnExit(e);
    }
}
