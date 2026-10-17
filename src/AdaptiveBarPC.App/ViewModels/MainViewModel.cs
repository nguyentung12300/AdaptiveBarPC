using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using BrushConverter = System.Windows.Media.BrushConverter;
using Color = System.Windows.Media.Color;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppConfig _config;
    private readonly ForegroundAppService _foreground;
    private readonly ProfileService _profiles;
    private readonly IAudioProvider _audio;
    private readonly ActionExecutor _actions;
    private readonly MediaSessionService _mediaSession;
    private readonly AppLogger _logger;
    private readonly AssetPathResolver _assetPaths;
    private readonly WidgetProviderRegistry _providerRegistry;
    private readonly bool _ownsProviderRegistry;
    private readonly ClipboardHistoryService _clipboardHistory;
    private readonly bool _ownsClipboardHistory;
    private readonly string _appearancePath;
    private readonly Dispatcher _dispatcher;
    private readonly System.IO.FileSystemWatcher? _appearanceWatcher;
    private GlobalAppearanceSettings _globalAppearance = new();
    internal WidgetProviderRegistry ProviderRegistry => _providerRegistry;
    internal ClipboardHistoryService ClipboardHistory => _clipboardHistory;

    private string _activeProfileId = "";
    private string _activeProfileName = "AdaptiveBarPC";
    private string _activeProcessName = "";
    private ProfileDefinition? _activeProfile;
    private string _fixedProfileId = "";
    private List<PageDefinition> _activePages = new();
    private int _currentPageIndex;
    private InputDrawer _inputDrawer;
    private bool _textInputContextActive;

    private static readonly string[] EmojiChoices =
    [
        "😀", "😃", "😄", "😁", "😂", "🥰", "😍", "😘",
        "😊", "😎", "🤔", "😅", "😭", "😡", "👍", "👎",
        "👏", "🙏", "💪", "👋", "❤️", "🔥", "✨", "🎉",
        "✅", "❌", "⭐", "💯", "🚀", "💡", "📌", "👀"
    ];

    private Brush _barBackgroundBrush = NewBrush("#FF0A0A0D", Brushes.Black);
    private Brush _tileBackgroundBrush = NewBrush("#FF1C1C24", Brushes.DimGray);
    private Brush _accentBrush = NewBrush("#FF7C5CFC", Brushes.MediumPurple);
    private Brush _primaryTextBrush = NewBrush("#FFF5F5F7", Brushes.White);
    private Brush _secondaryTextBrush = NewBrush("#FFA1A1AA", Brushes.Gray);
    private Brush _controlWellBrush = NewBrush("#FF121217", Brushes.DimGray);
    private Brush _controlSurfaceBrush = NewBrush("#FF1C1C24", Brushes.DimGray);
    private Brush _controlHoverBrush = NewBrush("#FF282834", Brushes.DimGray);
    private Brush _controlPressedBrush = NewBrush("#FF343444", Brushes.DimGray);
    private Brush _accentSubtleBrush = NewBrush("#247C5CFC", Brushes.MediumPurple);
    private Brush _textMutedBrush = NewBrush("#FF71717A", Brushes.Gray);
    private Brush _dividerBrush = NewBrush("#1AFFFFFF", Brushes.DimGray);

    public ObservableCollection<BarItemViewModel> Items { get; } = new();

    public ObservableCollection<BarItemViewModel> FixedLeftItems { get; } = new();
    public ObservableCollection<BarItemViewModel> FixedRightItems { get; } = new();

    /// <summary>Items flagged <c>system:true</c> in the profile — rendered in the pinned right panel.</summary>
    public ObservableCollection<BarItemViewModel> SystemItems { get; } = new();

    public bool HasFixedLeftItems => FixedLeftItems.Count > 0;
    public bool HasFixedRightItems => FixedRightItems.Count > 0;
    public bool HasSystemItems => SystemItems.Count > 0;
    public bool IsEmojiPickerOpen => _inputDrawer == InputDrawer.Emoji;
    public bool EnableAnimations => _config.EnableAnimations;
    public string? BackgroundMediaSource => _assetPaths.ResolveExistingFile(_globalAppearance.BackgroundMedia.Source);
    public string BackgroundMediaFit => _globalAppearance.BackgroundMedia.Fit;
    public bool BackgroundMediaLoop => _globalAppearance.BackgroundMedia.Loop;
    public bool BackgroundMediaEnabled => _globalAppearance.BackgroundMedia.Enabled;
    public double BackgroundMediaOpacity => _globalAppearance.BackgroundMedia.Opacity;
    public double BackgroundMediaOffsetX => _globalAppearance.BackgroundMedia.OffsetX;
    public double BackgroundMediaOffsetY => _globalAppearance.BackgroundMedia.OffsetY;
    public double BackgroundMediaZoom => _globalAppearance.BackgroundMedia.Zoom;
    public bool BackgroundMediaMotionEnabled => EnableAnimations && (_globalAppearance.Appearance?.MotionEnabled ?? true);

    public event EventHandler? LayoutChanged;

    public string ActiveProfileName
    {
        get => _activeProfileName;
        private set => SetField(ref _activeProfileName, value);
    }

    public string ActiveProcessName
    {
        get => _activeProcessName;
        private set => SetField(ref _activeProcessName, value);
    }

    public string CurrentPageName =>
        _inputDrawer != InputDrawer.None
            ? _inputDrawer.ToString()
            : _textInputContextActive
            ? "Input"
            : _activePages.Count == 0
            ? ""
            : string.IsNullOrWhiteSpace(_activePages[_currentPageIndex].DisplayName)
                ? _activePages[_currentPageIndex].Id
                : _activePages[_currentPageIndex].DisplayName;

    public string PageIndicator =>
        _inputDrawer != InputDrawer.None || _textInputContextActive || _activePages.Count <= 1
            ? ""
            : string.Join(" ", Enumerable.Range(0, _activePages.Count)
                .Select(i => i == _currentPageIndex ? "●" : "·"));

    public bool HasMultiplePages =>
        _inputDrawer == InputDrawer.None && !_textInputContextActive && _activePages.Count > 1;
    public bool HasRightControls => HasFixedRightItems || HasSystemItems || HasMultiplePages;

    public IReadOnlyList<PageDot> PageDots =>
        _inputDrawer != InputDrawer.None || _textInputContextActive || _activePages.Count <= 1
            ? Array.Empty<PageDot>()
            : Enumerable.Range(0, _activePages.Count)
                .Select(i => new PageDot(i, i == _currentPageIndex))
                .ToArray();

    internal bool CanSwipePreviousPage =>
        _inputDrawer == InputDrawer.None && !_textInputContextActive && _activePages.Count > 1 && _currentPageIndex > 0;
    internal bool CanSwipeNextPage =>
        _inputDrawer == InputDrawer.None && !_textInputContextActive && _activePages.Count > 1 && _currentPageIndex < _activePages.Count - 1;

    public Brush BarBackgroundBrush
    {
        get => _barBackgroundBrush;
        private set => SetField(ref _barBackgroundBrush, value);
    }

    public Brush TileBackgroundBrush
    {
        get => _tileBackgroundBrush;
        private set => SetField(ref _tileBackgroundBrush, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        private set => SetField(ref _accentBrush, value);
    }

    public Brush PrimaryTextBrush
    {
        get => _primaryTextBrush;
        private set => SetField(ref _primaryTextBrush, value);
    }

    public Brush SecondaryTextBrush
    {
        get => _secondaryTextBrush;
        private set => SetField(ref _secondaryTextBrush, value);
    }

    public Brush ControlWellBrush
    {
        get => _controlWellBrush;
        private set => SetField(ref _controlWellBrush, value);
    }

    public Brush ControlSurfaceBrush
    {
        get => _controlSurfaceBrush;
        private set => SetField(ref _controlSurfaceBrush, value);
    }

    public Brush ControlHoverBrush
    {
        get => _controlHoverBrush;
        private set => SetField(ref _controlHoverBrush, value);
    }

    public Brush ControlPressedBrush
    {
        get => _controlPressedBrush;
        private set => SetField(ref _controlPressedBrush, value);
    }

    public Brush AccentSubtleBrush
    {
        get => _accentSubtleBrush;
        private set => SetField(ref _accentSubtleBrush, value);
    }

    public Brush TextMutedBrush
    {
        get => _textMutedBrush;
        private set => SetField(ref _textMutedBrush, value);
    }

    public Brush DividerBrush
    {
        get => _dividerBrush;
        private set => SetField(ref _dividerBrush, value);
    }

    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }

    public MainViewModel(
        AppConfig config,
        ForegroundAppService foreground,
        ProfileService profiles,
        IAudioProvider audio,
        ActionExecutor actions,
        MediaSessionService mediaSession,
        AppLogger logger,
        WidgetProviderRegistry? providerRegistry = null,
        string? appearanceFilePath = null,
        ClipboardHistoryService? clipboardHistory = null)
    {
        _config = config;
        _foreground = foreground;
        _profiles = profiles;
        _audio = audio;
        _actions = actions;
        _mediaSession = mediaSession;
        _logger = logger;
        _assetPaths = AssetPathResolver.ForProfilesDirectory(profiles.DirectoryPath);
        _ownsProviderRegistry = providerRegistry is null;
        _providerRegistry = providerRegistry ?? new WidgetProviderRegistry();
        _ownsClipboardHistory = clipboardHistory is null;
        _clipboardHistory = clipboardHistory ?? new ClipboardHistoryService(logger);
        _clipboardHistory.HistoryChanged += OnClipboardHistoryChanged;
        _appearancePath = System.IO.Path.GetFullPath(appearanceFilePath ?? GlobalAppearanceStore.DefaultPath);
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        NextPageCommand = new DelegateCommand(NextPage);
        PreviousPageCommand = new DelegateCommand(PreviousPage);
        _actions.SetPageNavigator(NavigatePage);

        _profiles.ProfilesChanged += (_, _) =>
        {
            // ProfileService raises this from a background thread, and in unit
            // tests there is no Application.Current. Fall back to the dispatcher
            // captured at construction time instead of dereferencing null.
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

            dispatcher.BeginInvoke(() =>
            {
                _activeProfileId = "";
                _fixedProfileId = "";
                ApplyForegroundApp(_foreground.GetCurrent());
            });
        };

        _foreground.ContextChanged += OnForegroundContextChanged;
        _foreground.TextInputContextChanged += OnTextInputContextChanged;

        var initialApp = _foreground.GetCurrent();
        _globalAppearance = GlobalAppearanceStore.Load(_appearancePath);
        if (!System.IO.File.Exists(_appearancePath))
        {
            try
            {
                _globalAppearance = GlobalAppearanceStore.FromProfile(_profiles.Select(initialApp));
                GlobalAppearanceStore.Save(_globalAppearance, _appearancePath);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Could not save global appearance settings: {ex.Message}");
            }
        }

        ApplyGlobalAppearance();
        _appearanceWatcher = CreateAppearanceWatcher();
        ApplyForegroundApp(initialApp);
        _foreground.CheckCurrent();
        _foreground.Start(config.PollIntervalMs);
    }

    private System.IO.FileSystemWatcher? CreateAppearanceWatcher()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_appearancePath);
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            System.IO.Directory.CreateDirectory(directory);
            var watcher = new System.IO.FileSystemWatcher(directory)
            {
                NotifyFilter = System.IO.NotifyFilters.FileName |
                               System.IO.NotifyFilters.LastWrite |
                               System.IO.NotifyFilters.CreationTime
            };
            watcher.Changed += OnAppearanceFileChanged;
            watcher.Created += OnAppearanceFileChanged;
            watcher.Renamed += OnAppearanceFileChanged;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not watch global appearance settings: {ex.Message}");
            return null;
        }
    }

    private void OnAppearanceFileChanged(object sender, System.IO.FileSystemEventArgs e)
    {
        if (!string.Equals(System.IO.Path.GetFullPath(e.FullPath), _appearancePath, StringComparison.OrdinalIgnoreCase) ||
            _dispatcher.HasShutdownStarted)
            return;

        _dispatcher.BeginInvoke(() =>
        {
            if (!System.IO.File.Exists(_appearancePath))
                return;

            _globalAppearance = GlobalAppearanceStore.Load(_appearancePath);
            ApplyGlobalAppearance();
            _fixedProfileId = "";
            BuildCurrentPage();
        });
    }

    public void NextPage()
    {
        if (_textInputContextActive || _inputDrawer != InputDrawer.None || _activePages.Count <= 1)
            return;

        _currentPageIndex = (_currentPageIndex + 1) % _activePages.Count;
        BuildCurrentPage();
    }

    public void PreviousPage()
    {
        if (_textInputContextActive || _inputDrawer != InputDrawer.None || _activePages.Count <= 1)
            return;

        _currentPageIndex--;
        if (_currentPageIndex < 0)
            _currentPageIndex = _activePages.Count - 1;

        BuildCurrentPage();
    }

    private void NavigatePage(string target)
    {
        if (target.Equals("next", StringComparison.OrdinalIgnoreCase))
        {
            NextPage();
            return;
        }

        if (target.Equals("previous", StringComparison.OrdinalIgnoreCase))
        {
            PreviousPage();
            return;
        }

        var pageIndex = _activePages.FindIndex(page =>
            page.Id.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (pageIndex < 0 || pageIndex == _currentPageIndex)
            return;

        _currentPageIndex = pageIndex;
        BuildCurrentPage();
    }

    private void OnForegroundContextChanged(object? sender, ForegroundAppInfo app)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyForegroundApp(app));
        }
        else
        {
            ApplyForegroundApp(app);
        }
    }

    private void OnTextInputContextChanged(bool active)
    {
        if (_dispatcher.CheckAccess())
            ApplyTextInputContext(active);
        else
            _dispatcher.BeginInvoke(() => ApplyTextInputContext(active));
    }

    internal void ApplyTextInputContext(bool active)
    {
        if (_textInputContextActive == active)
            return;

        _textInputContextActive = active;
        if (active && _inputDrawer != InputDrawer.None)
        {
            NotifyPageProperties();
            return;
        }

        BuildCurrentPage();
    }

    public void ApplyForegroundApp(ForegroundAppInfo app)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(app.ProcessName))
                ActiveProcessName = app.ProcessName;

            var profile = _profiles.Select(app);

            if (_activeProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                var expectedName = string.IsNullOrWhiteSpace(profile.DisplayName)
                    ? profile.Id
                    : profile.DisplayName;

                if (_activeProfileName.Equals(expectedName, StringComparison.Ordinal))
                    return;

                ActiveProfileName = expectedName;
                return;
            }

            _activeProfileId = profile.Id;
            _activeProfile = profile;
            _currentPageIndex = 0;
            _activePages = BuildEffectivePages(profile);

            ActiveProfileName = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.Id
                : profile.DisplayName;

            NotifyPageProperties();
            BuildCurrentPage();
        }
        catch (Exception ex)
        {
            _logger.Error("Foreground/profile resolution failed.", ex);
        }
    }

    private static List<PageDefinition> BuildEffectivePages(ProfileDefinition profile)
    {
        if (profile.Pages.Count > 0)
            return profile.Pages;

        return new List<PageDefinition>
        {
            new()
            {
                Id = "main",
                DisplayName = "Main",
                Items = profile.Items
            }
        };
    }

    private void BuildCurrentPage()
    {
        _inputDrawer = InputDrawer.None;
        foreach (var item in Items)
        {
            if (item is IDisposable disposable)
                disposable.Dispose();
        }
        Items.Clear();

        foreach (var item in SystemItems)
        {
            if (item is IDisposable disposable)
                disposable.Dispose();
        }
        SystemItems.Clear();

        var rebuildFixed = !_fixedProfileId.Equals(_activeProfileId, StringComparison.OrdinalIgnoreCase);
        if (rebuildFixed)
        {
            DisposeItems(FixedLeftItems);
            DisposeItems(FixedRightItems);
            _fixedProfileId = _activeProfileId;
        }

        if (_activePages.Count == 0)
        {
            NotifyPageProperties();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _currentPageIndex = Math.Clamp(_currentPageIndex, 0, _activePages.Count - 1);
        var page = _activePages[_currentPageIndex];

        var buildQueue = new List<(ControlDefinition Item, ObservableCollection<BarItemViewModel> MainTarget, ObservableCollection<BarItemViewModel> SystemTarget)>();
        if (rebuildFixed && _activeProfile is not null)
        {
            buildQueue.AddRange(_globalAppearance.FixedLeft.Select(item => (item, FixedLeftItems, FixedLeftItems)));
            buildQueue.AddRange(_activeProfile.FixedLeft.Select(item => (item, FixedLeftItems, FixedLeftItems)));
            buildQueue.AddRange(_activeProfile.FixedRight.Select(item => (item, FixedRightItems, FixedRightItems)));
            buildQueue.AddRange(_globalAppearance.FixedRight.Select(item => (item, FixedRightItems, FixedRightItems)));
        }
        if (_textInputContextActive)
        {
            Items.Add(CreateInputButton("Emoji", 84, ShowEmojiPicker));
            Items.Add(CreateInputButton("Clipboard", 96, ShowClipboardPicker));
            Items.Add(CreateInputButton("Text", 72, ShowTextPicker));
            Items.Add(CreateInputButton("Sticker", 76, ShowStickerPicker));
        }
        else
        {
            buildQueue.AddRange(page.Items.Select(item => (item, Items, SystemItems)));
        }

        foreach (var (item, mainTarget, systemTarget) in buildQueue)
        {
            switch (item.Type.Trim().ToLowerInvariant())
            {
                case WidgetTypes.Button:
                case WidgetTypes.TextSlot:
                {
                    var primaryAction = item.Action;
                    var secondaryAction = item.SecondaryAction;

                    var vm = new ButtonItemViewModel
                    {
                        Id = item.Id,
                        Label = item.Label,
                        Icon = item.Icon?.Value,
                        IconKind = item.Icon?.Kind,
                        AssetIcon = LoadAssetIcon(item.Icon),
                        Width = item.Width,
                        LongPressMilliseconds = item.LongPressMs ?? _config.LongPressMilliseconds,
                        Command = new DelegateCommand(() => _actions.Execute(primaryAction)),
                        SecondaryCommand = secondaryAction is null
                            ? null
                            : new DelegateCommand(() => _actions.Execute(secondaryAction))
                    };
                    (item.System ? systemTarget : mainTarget).Add(vm);
                    break;
                }

                case WidgetTypes.Emoji:
                    (item.System ? systemTarget : mainTarget).Add(new ButtonItemViewModel
                    {
                        Id = item.Id,
                        Label = string.IsNullOrWhiteSpace(item.Label) ? "Emoji" : item.Label,
                        Icon = item.Icon?.Value,
                        IconKind = item.Icon?.Kind,
                        AssetIcon = LoadAssetIcon(item.Icon),
                        Width = item.Width,
                        Command = new DelegateCommand(ShowEmojiPicker)
                    });
                    break;

                case WidgetTypes.Clipboard:
                    (item.System ? systemTarget : mainTarget).Add(new ButtonItemViewModel
                    {
                        Id = item.Id,
                        Label = string.IsNullOrWhiteSpace(item.Label) ? "Clipboard" : item.Label,
                        Icon = item.Icon?.Value,
                        IconKind = item.Icon?.Kind,
                        AssetIcon = LoadAssetIcon(item.Icon),
                        Width = item.Width,
                        Command = new DelegateCommand(ShowClipboardPicker)
                    });
                    break;

                case WidgetTypes.Sticker:
                    (item.System ? systemTarget : mainTarget).Add(
                        CreateStickerButton(item, closeOnTap: false) ?? new ButtonItemViewModel
                        {
                            Id = item.Id,
                            Label = string.IsNullOrWhiteSpace(item.Label) ? "Sticker" : item.Label,
                            Width = item.Width,
                            Command = new DelegateCommand(() => { })
                        });
                    break;

                case "slider":
                    if (item.Action?.Type.Equals(
                            "volume",
                            StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var initial = 50.0;
                        try { initial = _audio.GetMasterVolumePercent(); }
                        catch (Exception ex)
                        {
                            _logger.Error("Failed to read initial volume.", ex);
                        }

                        var sliderVm = new MasterVolumeItemViewModel(
                            item.Label,
                            item.Width,
                            item.Min,
                            item.Max,
                            Math.Clamp(initial, item.Min, item.Max),
                            value =>
                            {
                                try { _audio.SetMasterVolumePercent(value); }
                                catch (Exception ex)
                                {
                                    _logger.Error("Volume slider failed.", ex);
                                }
                            })
                        {
                            Id = item.Id,
                            Icon = item.Icon?.Value,
                            IconKind = item.Icon?.Kind,
                            AssetIcon = LoadAssetIcon(item.Icon)
                        };
                        (item.System ? systemTarget : mainTarget).Add(sliderVm);
                    }
                    break;

                case WidgetTypes.ExpandableSlider:
                {
                    var maximum = item.Max > item.Min ? item.Max : item.Min + 100;
                    var initial = Math.Clamp(item.Value ?? 50.0, item.Min, maximum);
                    var icon = item.Icon?.Kind.Equals("glyph", StringComparison.OrdinalIgnoreCase) == true
                        ? item.Icon.Value
                        : null;
                    var sliderVm = new ExpandableSliderItemViewModel(
                        item.Label,
                        item.Width,
                        item.Min,
                        maximum,
                        initial,
                        setter: null,
                        icon,
                        item.Enabled ?? true)
                    {
                        Id = item.Id
                    };
                    (item.System ? systemTarget : mainTarget).Add(sliderVm);
                    break;
                }

                case "media":
                    // Width <= 0 in the profile means "stretch to fill available space".
                    // Pass double.NaN so the DataTemplate can use HorizontalAlignment=Stretch.
                    var mediaWidth = item.Width > 0 ? item.Width : double.NaN;
                    var collapse = item.CollapseWhenUnavailable || item.AutoCollapse;
                    mainTarget.Add(new MediaItemViewModel(
                        _mediaSession,
                        mediaWidth,
                        collapse));
                    break;

                case WidgetTypes.DecorativeMedia:
                    (item.System ? systemTarget : mainTarget).Add(
                        DecorativeMediaItemViewModel.Create(item, _assetPaths));
                    break;

                case "spacer":
                    mainTarget.Add(new SpacerItemViewModel { Width = item.Width });
                    break;

                case "dynamiclist":
                {
                    var providerId = !string.IsNullOrWhiteSpace(item.Provider)
                        ? item.Provider
                        : item.Id;

                    var (provider, ownsProvider) = _providerRegistry.Resolve(providerId);
                    if (provider != null)
                    {
                        var dynamicWidth = item.Width > 0 ? item.Width : double.NaN;
                        var dynamicVm = new DynamicListItemViewModel(
                            provider,
                            ownsProvider,
                            dynamicWidth,
                            item.EmptyBehavior ?? "collapse",
                            item.MaxItems,
                            _actions,
                            _logger)
                        {
                            Id = item.Id
                        };

                        (item.System ? systemTarget : mainTarget).Add(dynamicVm);
                    }
                    break;
                }

                case "toggle":
                {
                    var toggleVm = new ToggleItemViewModel
                    {
                        Id = item.Id,
                        Label = item.Label,
                        OnLabel = item.OnLabel,
                        OffLabel = item.OffLabel,
                        Icon = item.Icon?.Value,
                        IconKind = item.Icon?.Kind,
                        AssetIcon = LoadAssetIcon(item.Icon),
                        Width = item.Width,
                        IsChecked = item.IsChecked,
                        LongPressMilliseconds = item.LongPressMs ?? _config.LongPressMilliseconds
                    };

                    var onAction = item.OnAction ?? item.Action;
                    var offAction = item.OffAction;
                    var secAction = item.SecondaryAction;

                    toggleVm.ToggleCommand = new DelegateCommand(() =>
                    {
                        toggleVm.IsChecked = !toggleVm.IsChecked;
                        var actionToRun = toggleVm.IsChecked ? onAction : (offAction ?? onAction);
                        if (actionToRun != null)
                            _actions.Execute(actionToRun);
                    });

                    if (secAction != null)
                    {
                        toggleVm.SecondaryCommand = new DelegateCommand(() => _actions.Execute(secAction));
                    }

                    (item.System ? systemTarget : mainTarget).Add(toggleVm);
                    break;
                }

                case "multistate":
                {
                    var states = item.States;
                    var initialIdx = 0;
                    if (!string.IsNullOrEmpty(item.State))
                    {
                        var found = states.FindIndex(s => s.Id.Equals(item.State, StringComparison.OrdinalIgnoreCase));
                        if (found >= 0) initialIdx = found;
                    }

                    var multiVm = new MultiStateItemViewModel
                    {
                        Id = item.Id,
                        Width = item.Width,
                        States = states,
                        CurrentIndex = initialIdx
                    };

                    multiVm.CycleCommand = new DelegateCommand(() =>
                    {
                        if (multiVm.States.Count == 0) return;
                        multiVm.CurrentIndex = (multiVm.CurrentIndex + 1) % multiVm.States.Count;
                        var act = multiVm.CurrentState?.Action;
                        if (act != null)
                            _actions.Execute(act);
                    });

                    (item.System ? systemTarget : mainTarget).Add(multiVm);
                    break;
                }

                case "sensor":
                {
                    var sensorVm = new SensorItemViewModel
                    {
                        Id = item.Id,
                        Label = item.Label,
                        Icon = item.Icon?.Value,
                        IconKind = item.Icon?.Kind,
                        AssetIcon = LoadAssetIcon(item.Icon),
                        Width = item.Width,
                        Metric = item.Metric ?? "",
                        Unit = item.Unit ?? "",
                        Format = item.Format ?? "{0}"
                    };
                    (item.System ? systemTarget : mainTarget).Add(sensorVm);
                    break;
                }
            }
        }

        OnPropertyChanged(nameof(HasSystemItems));
        if (rebuildFixed)
        {
            OnPropertyChanged(nameof(HasFixedLeftItems));
            OnPropertyChanged(nameof(HasFixedRightItems));
        }
        NotifyPageProperties();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowEmojiPicker()
    {
        var items = EmojiChoices.Select(emoji => new ButtonItemViewModel
        {
            Label = emoji,
            IsEmoji = true,
            Width = 56,
            AssetIcon = AssetImageLoader.LoadBitmapFile(System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "emoji",
                string.Join("-", emoji.EnumerateRunes().Select(rune => rune.Value.ToString("x"))) + ".png"), 64),
            Command = new DelegateCommand(() =>
                _actions.Execute(new ActionDefinition { Type = "text", Value = emoji }))
        });
        ShowInputDrawer(InputDrawer.Emoji, items);
    }

    private void ShowClipboardPicker()
    {
        IEnumerable<BarItemViewModel> items = _clipboardHistory.Items.Count == 0
            ? [new ButtonItemViewModel
            {
                Label = "No text history",
                Width = 150,
                Command = new DelegateCommand(() => { })
            }]
            : _clipboardHistory.Items.Select(text => new ButtonItemViewModel
            {
                Label = ClipboardLabel(text),
                Width = 180,
                Command = new DelegateCommand(() =>
                {
                    _actions.Execute(new ActionDefinition { Type = "clipboard_text", Value = text });
                    CloseInputDrawer();
                })
            });
        ShowInputDrawer(InputDrawer.Clipboard, items);
    }

    private void ShowTextPicker()
    {
        var slots = GetInputItems(WidgetTypes.TextSlot)
            .ToArray();

        IEnumerable<BarItemViewModel> items = slots.Length == 0
            ? [new ButtonItemViewModel
            {
                Label = "No text slots",
                Width = 140,
                Command = new DelegateCommand(() => { })
            }]
            : slots.Select(slot => new ButtonItemViewModel
            {
                Id = slot.Id,
                Label = string.IsNullOrWhiteSpace(slot.Label) ? "Text" : slot.Label,
                Icon = slot.Icon?.Value,
                IconKind = slot.Icon?.Kind,
                AssetIcon = LoadAssetIcon(slot.Icon),
                Width = Math.Clamp(slot.Width, 48, 220),
                Command = new DelegateCommand(() =>
                {
                    _actions.Execute(slot.Action);
                    CloseInputDrawer();
                })
            });

        ShowInputDrawer(InputDrawer.Text, items);
    }

    private void ShowStickerPicker()
    {
        var stickers = GetInputItems(WidgetTypes.Sticker)
            .Select(item => CreateStickerButton(item, closeOnTap: true))
            .OfType<BarItemViewModel>()
            .ToArray();

        ShowInputDrawer(InputDrawer.Sticker, stickers.Length == 0
            ? [new ButtonItemViewModel
            {
                Label = "No stickers",
                Width = 120,
                Command = new DelegateCommand(() => { })
            }]
            : stickers);
    }

    private IEnumerable<ControlDefinition> GetInputItems(string type) =>
        _globalAppearance.InputItems
            .Concat(_activePages.SelectMany(page => page.Items))
            .Where(item => item.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase);

    private ButtonItemViewModel? CreateStickerButton(ControlDefinition sticker, bool closeOnTap)
    {
        var path = _assetPaths.ResolveExistingFile(sticker.Source);
        var thumbnail = StickerImageLoader.Load(path, 32);
        if (path is null || thumbnail is null)
            return null;

        return new ButtonItemViewModel
        {
            Id = sticker.Id,
            Label = string.IsNullOrWhiteSpace(sticker.Label) ? "Sticker" : sticker.Label,
            AssetIcon = thumbnail,
            Width = Math.Clamp(sticker.Width, 48, 220),
            Command = new DelegateCommand(() =>
            {
                _actions.Execute(new ActionDefinition { Type = "clipboard_image", Value = path });
                if (closeOnTap)
                    CloseInputDrawer();
            })
        };
    }

    private static ButtonItemViewModel CreateInputButton(string label, double width, Action action) => new()
    {
        Label = label,
        Width = width,
        Command = new DelegateCommand(action)
    };

    private void ShowInputDrawer(InputDrawer drawer, IEnumerable<BarItemViewModel> drawerItems)
    {
        _inputDrawer = drawer;
        DisposeItems(Items);
        Items.Add(new ButtonItemViewModel
        {
            Label = "Back",
            Icon = "E72B",
            IconKind = "glyph",
            Width = 48,
            Command = new DelegateCommand(CloseInputDrawer)
        });
        foreach (var item in drawerItems)
            Items.Add(item);

        NotifyPageProperties();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseInputDrawer()
    {
        if (_inputDrawer != InputDrawer.None)
            BuildCurrentPage();
    }

    private void OnClipboardHistoryChanged(object? sender, EventArgs e)
    {
        if (_inputDrawer != InputDrawer.Clipboard)
            return;

        if (_dispatcher.CheckAccess())
            ShowClipboardPicker();
        else
            _dispatcher.BeginInvoke(ShowClipboardPicker);
    }

    private static string ClipboardLabel(string text)
        => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static void DisposeItems(ObservableCollection<BarItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item is IDisposable disposable)
                disposable.Dispose();
        }
        items.Clear();
    }

    internal void ApplyTheme(ThemeDefinition? theme, AppearanceDefinition? appearance = null)
    {
        var resolved = AppearanceResolver.Resolve(theme, appearance, EnableAnimations);

        if (System.Windows.Application.Current?.Resources is not null)
        {
            AppearanceResolver.ApplyToResources(System.Windows.Application.Current.Resources, resolved);
        }

        BarBackgroundBrush   = NewBrush(resolved.SurfaceBase,    _barBackgroundBrush);
        TileBackgroundBrush  = NewBrush(resolved.SurfaceControl, _tileBackgroundBrush);
        AccentBrush          = NewBrush(resolved.AccentPrimary,  _accentBrush);
        PrimaryTextBrush     = NewBrush(resolved.TextPrimary,    _primaryTextBrush);
        SecondaryTextBrush   = NewBrush(resolved.TextSecondary,  _secondaryTextBrush);

        // ControlSurface is driven directly by the profile's TileBackgroundBrush
        ControlSurfaceBrush  = TileBackgroundBrush;

        // ControlWell is deterministically derived as a darkened tint (-10) of ControlSurface
        ControlWellBrush     = DeriveTint(ControlSurfaceBrush, -10, _controlWellBrush);

        // ControlHover is deterministically derived as a lightened tint (+12) of ControlSurface
        ControlHoverBrush    = DeriveTint(ControlSurfaceBrush, 12, _controlHoverBrush);

        // ControlPressed is deterministically derived as a lightened tint (+24) of ControlSurface
        ControlPressedBrush  = DeriveTint(ControlSurfaceBrush, 24, _controlPressedBrush);

        // AccentSubtle is deterministically derived from Accent with 14% alpha (0x24)
        AccentSubtleBrush    = DeriveAlpha(AccentBrush, 0x24, _accentSubtleBrush);

        TextMutedBrush       = _textMutedBrush;
        DividerBrush         = _dividerBrush;
    }

    private void ApplyGlobalAppearance()
    {
        ApplyTheme(_globalAppearance.Theme, _globalAppearance.Appearance);
        OnPropertyChanged(nameof(BackgroundMediaSource));
        OnPropertyChanged(nameof(BackgroundMediaFit));
        OnPropertyChanged(nameof(BackgroundMediaLoop));
        OnPropertyChanged(nameof(BackgroundMediaEnabled));
        OnPropertyChanged(nameof(BackgroundMediaOpacity));
        OnPropertyChanged(nameof(BackgroundMediaOffsetX));
        OnPropertyChanged(nameof(BackgroundMediaOffsetY));
        OnPropertyChanged(nameof(BackgroundMediaZoom));
        OnPropertyChanged(nameof(BackgroundMediaMotionEnabled));
    }

    private static Brush DeriveAlpha(Brush baseBrush, byte alpha, Brush fallback)
    {
        if (baseBrush is SolidColorBrush scb)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, scb.Color.R, scb.Color.G, scb.Color.B));
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }

        return fallback;
    }

    private static Brush DeriveTint(Brush baseBrush, int delta, Brush fallback)
    {
        if (baseBrush is SolidColorBrush scb)
        {
            var r = (byte)Math.Clamp(scb.Color.R + delta, 0, 255);
            var g = (byte)Math.Clamp(scb.Color.G + delta, 0, 255);
            var b = (byte)Math.Clamp(scb.Color.B + delta, 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(scb.Color.A, r, g, b));
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }

        return fallback;
    }

    private static Brush NewBrush(string? value, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        try
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(value)!;
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }
        catch
        {
            return fallback;
        }
    }

    private ImageSource? LoadAssetIcon(IconDefinition? icon) =>
        string.Equals(icon?.Kind, "asset", StringComparison.OrdinalIgnoreCase)
            ? AssetImageLoader.LoadIcon(_assetPaths, icon?.Value)
            : null;

    private void NotifyPageProperties()
    {
        OnPropertyChanged(nameof(IsEmojiPickerOpen));
        OnPropertyChanged(nameof(CurrentPageName));
        OnPropertyChanged(nameof(PageIndicator));
        OnPropertyChanged(nameof(PageDots));
        OnPropertyChanged(nameof(HasMultiplePages));
        OnPropertyChanged(nameof(HasRightControls));
    }

    public void Dispose()
    {
        _clipboardHistory.HistoryChanged -= OnClipboardHistoryChanged;
        _appearanceWatcher?.Dispose();
        _foreground.ContextChanged -= OnForegroundContextChanged;
        _foreground.TextInputContextChanged -= OnTextInputContextChanged;
        _foreground.Dispose();
        foreach (var item in Items)
        {
            if (item is IDisposable disposable)
                disposable.Dispose();
        }
        Items.Clear();
        foreach (var item in SystemItems)
        {
            if (item is IDisposable disposable)
                disposable.Dispose();
        }
        SystemItems.Clear();
        DisposeItems(FixedLeftItems);
        DisposeItems(FixedRightItems);
        _profiles.Dispose();

        if (_ownsProviderRegistry)
        {
            _providerRegistry.Dispose();
        }

        if (_ownsClipboardHistory)
            _clipboardHistory.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private enum InputDrawer
    {
        None,
        Emoji,
        Clipboard,
        Text,
        Sticker
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record PageDot(int Index, bool IsActive);
