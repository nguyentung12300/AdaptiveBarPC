using System.IO;
using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services;

public sealed class ProfileService : IDisposable
{
    private readonly string _directory;
    private readonly AppLogger _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();

    private readonly object _debounceLock = new();
    private readonly System.Threading.Timer _reloadTimer;
    private readonly ProfileDocumentSerializer _documents = new();
    private bool _disposed;

    private List<ProfileDefinition> _profiles = new();

    public event EventHandler? ProfilesChanged;

    public ProfileService(string directory, AppLogger logger)
    {
        _directory = directory;
        _logger = logger;
        Directory.CreateDirectory(directory);

        Reload();

        _reloadTimer = new System.Threading.Timer(ReloadAfterDebounce, null,
            Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(directory, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime
        };

        _watcher.Changed += OnProfileChanged;
        _watcher.Created += OnProfileChanged;
        _watcher.Deleted += OnProfileChanged;
        _watcher.Renamed += OnProfileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private readonly ProfileResolver _resolver = new();

    public ProfileResolver Resolver => _resolver;
    public string DirectoryPath => _directory;

    public IReadOnlyList<ProfileDefinition> GetProfiles()
    {
        lock (_gate)
            return _profiles.ToList();
    }

    public ProfileDefinition Select(ForegroundAppInfo app)
    {
        List<ProfileDefinition> snapshot;
        lock (_gate)
            snapshot = _profiles.ToList();

        return _resolver.Resolve(app, snapshot);
    }

    private void OnProfileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_debounceLock)
        {
            if (!_disposed)
                _reloadTimer.Change(120, Timeout.Infinite);
        }
    }

    private void ReloadAfterDebounce(object? state)
    {
        lock (_debounceLock)
        {
            if (_disposed)
                return;
            try
            {
                Reload();
                ProfilesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error("Profile reload after file change failed.", ex);
            }
        }
    }

    public void Reload()
    {
        var loaded = new List<ProfileDefinition>();
        var seenIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(_directory, "*.json").OrderBy(f => f))
        {
            try
            {
                var text = File.ReadAllText(file);
                var result = _documents.Load(text);
                foreach (var diagnostic in result.Diagnostics)
                {
                    var message = $"Profile '{file}': [{diagnostic.Code}] {diagnostic.Message}";
                    if (diagnostic.Severity == ProfileDiagnosticSeverity.Error)
                        _logger.Error(message);
                    else
                        _logger.Warn(message);
                }

                if (!result.IsValid || result.Document is null)
                    continue;

                var profile = result.Document;

                // Duplicate ids make profile selection depend on file ordering, so
                // surface the conflict instead of silently keeping both entries.
                if (seenIds.TryGetValue(profile.Id, out var previousFile))
                {
                    _logger.Error(
                        $"[{ProfileDiagnosticCodes.DuplicateProfileId}] Duplicate profile id '{profile.Id}' in '{file}'. " +
                        $"Already defined in '{previousFile}'. Skipping this file.");
                    continue;
                }

                seenIds[profile.Id] = file;
                loaded.Add(profile);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load profile: {file}", ex);
            }
        }

        lock (_gate)
            _profiles = loaded;

        _logger.Info($"Loaded {loaded.Count} profiles.");
    }

    public void Dispose()
    {
        lock (_debounceLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _reloadTimer.Dispose();
        }
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
