using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Designer;

public sealed record ThemeLibraryItem(ThemeLibraryEntry Entry, ImageSource? Preview)
{
    public string Name => Entry.Name;
}

public sealed class ThemeLibraryViewModel : INotifyPropertyChanged
{
    private readonly DesignerViewModel _designer;
    private readonly ThemeLibraryStore _store;
    private string _themeName = "My theme";
    private string _message = "Save a named copy of your current appearance.";
    private bool _isBusy;

    public ThemeLibraryViewModel(DesignerViewModel designer, string directory)
    {
        _designer = designer;
        _store = new ThemeLibraryStore(directory);
    }

    public ObservableCollection<ThemeLibraryItem> Themes { get; } = new();
    public string ThemeName
    {
        get => _themeName;
        set { _themeName = value; OnPropertyChanged(); }
    }
    public string Message
    {
        get => _message;
        private set { _message = value; OnPropertyChanged(); }
    }
    public bool IsAvailable => !_isBusy;

    public Task<bool> RefreshAsync() => RunAsync(async () =>
    {
        await RefreshCoreAsync();
        Message = Themes.Count == 0 ? "Enter a name, then choose Save Copy." : "Click a preview to apply a theme.";
    });

    public Task<bool> SaveAsync(byte[] preview) => RunAsync(async () =>
    {
        // Snapshot on the UI thread; the user can keep editing while files are copied.
        using var data = new MemoryStream(GlobalAppearanceStore.SerializeTheme(_designer.GlobalAppearance));
        var settings = GlobalAppearanceStore.ReadTheme(data);
        var media = _designer.ResolveMediaAsset(settings.BackgroundMedia.Source);
        var name = ThemeName;
        var entry = await Task.Run(() => _store.Save(name, settings, media, preview));
        await RefreshCoreAsync();
        ThemeName = entry.Name;
        Message = $"Saved “{entry.Name}”. Click its preview to apply.";
    });

    public Task<bool> ApplyAsync(ThemeLibraryItem item) => RunAsync(async () =>
    {
        var settings = await Task.Run(() => _store.Load(item.Entry));
        _designer.ApplyTheme(settings, item.Name);
        ThemeName = item.Name;
        Message = $"Applied “{item.Name}”.";
    });

    public Task<bool> ImportAsync(string path) => RunAsync(async () =>
    {
        var entry = await Task.Run(() => _store.Import(path));
        await RefreshCoreAsync();
        Message = $"Imported “{entry.Name}”. Click its preview to apply.";
    });

    public Task<bool> ExportAsync(ThemeLibraryItem item, string path) => RunAsync(async () =>
    {
        await Task.Run(() => _store.Export(item.Entry, path));
        Message = $"Exported “{item.Name}” with its preview and background media.";
    });

    private async Task RefreshCoreAsync()
    {
        var items = await Task.Run(() => _store.List().Select(entry => new ThemeLibraryItem(
            entry, AssetImageLoader.LoadBitmapFile(entry.PreviewPath, 640))).ToArray());
        Themes.Clear();
        foreach (var item in items) Themes.Add(item);
    }

    private async Task<bool> RunAsync(Func<Task> operation)
    {
        if (_isBusy) return false;
        _isBusy = true;
        OnPropertyChanged(nameof(IsAvailable));
        Message = "Working…";
        try
        {
            await operation();
            return true;
        }
        catch (Exception ex)
        {
            Message = $"Theme library: {ex.Message}";
            return false;
        }
        finally
        {
            _isBusy = false;
            OnPropertyChanged(nameof(IsAvailable));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
