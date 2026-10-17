using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using AdaptiveBarPC.Designer.Interop;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;
using Microsoft.Win32;

namespace AdaptiveBarPC.Designer;

public partial class MainWindow : Window
{
    private const string PaletteDragFormat = "AdaptiveBarDesigner/PaletteWidget";
    private const string WidgetDragFormat = "AdaptiveBarDesigner/Widget";

    private readonly DesignerViewModel _viewModel;
    private bool _syncingProfileSelection;
    private object? _pendingDrag;
    private Point _dragStart;
    private ControlDefinition? _resizingWidget;
    private double _resizeStartDisplayX;
    private double _resizeStartLogicalWidth;
    private bool _resizeChanged;

    public MainWindow() : this(new DesignerViewModel(DesignerPaths.FindProfileDirectory())) { }

    internal MainWindow(DesignerViewModel viewModel, ResourceDictionary? resources = null)
    {
        // Offscreen hosts can scope resources to this window without a process-wide Application.
        if (resources is not null) Resources = resources;
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        _viewModel = viewModel;
        _viewModel.AppearanceChanged += OnAppearanceChanged;
        DataContext = _viewModel;
        _viewModel.NotifyAppearanceChanged();
        Loaded += async (_, _) => await _viewModel.ThemeLibrary.RefreshAsync();
        Deactivated += (_, _) =>
        {
            EndCanvasPan();
            _spacePan = false;
            UpdateCanvasCursor();
            _viewModel.EndHistoryGesture();
        };
    }

    private void HistoryPointer_OnDown(object sender, RoutedEventArgs e) => _viewModel.BeginHistoryGesture();
    private void HistoryPointer_OnUp(object sender, RoutedEventArgs e) => _viewModel.EndHistoryGesture();

    private void Designer_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.SelectedWidgetEditor is { IsIconPickerOpen: true } editor)
        {
            editor.IsIconPickerOpen = false;
            e.Handled = true;
            return;
        }
        if (FindAncestor<RepeatButton>(e.OriginalSource as DependencyObject) is not null && e.Key == Key.Space)
        {
            _viewModel.BeginHistoryGesture();
            return;
        }
        if (TryHandleCanvasKey(e.Key, true, e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }
        if (TryHandleHistoryShortcut(e.Key, Keyboard.Modifiers, e.OriginalSource as DependencyObject))
            e.Handled = true;
    }

    internal bool TryHandleHistoryShortcut(Key key, ModifierKeys modifiers, DependencyObject? source)
    {
        var undo = key == Key.Z && modifiers == ModifierKeys.Control;
        var redo = key == Key.Y && modifiers == ModifierKeys.Control ||
                   key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        if ((!undo && !redo) || _resizingWidget is not null) return false;
        if (FindAncestor<TextBox>(source) is { } textBox)
        {
            var binding = textBox.GetBindingExpression(TextBox.TextProperty);
            // Search/name fields and invalid drafts retain WPF's local text undo.
            var tracked = binding?.ResolvedSource switch
            {
                WidgetEditorViewModel editor => binding.ResolvedSourcePropertyName != nameof(WidgetEditorViewModel.IconSearchText) &&
                    (binding.ResolvedSourcePropertyName != nameof(WidgetEditorViewModel.WidthText) ||
                     editor.GetEditorDiagnostic(_viewModel.ProfileId, _viewModel.CurrentPageIdentity) is null),
                DesignerViewModel => binding.ResolvedSourcePropertyName is nameof(DesignerViewModel.ProfileDisplayName) or nameof(DesignerViewModel.PageDisplayName),
                AppearanceEditorViewModel => true,
                ColorFieldViewModel => binding.ResolvedSourcePropertyName != nameof(ColorFieldViewModel.Text) ||
                                       AppearanceEditorViewModel.TryParseHexColor(textBox.Text, out _),
                _ => false
            };
            if (!tracked) return false;
        }
        var command = undo ? _viewModel.UndoCommand : _viewModel.RedoCommand;
        if (command.CanExecute(null)) command.Execute(null);
        return true;
    }

    private void OnAppearanceChanged(ResolvedAppearance resolved)
    {
        if (PreviewSurface is not null)
        {
            AppearanceResolver.ApplyToResources(PreviewSurface.Resources, resolved);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmWindowTheme.ApplyDarkCaption(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.FlushPendingAppearanceSave();
        base.OnClosed(e);
    }

    private void ProfilesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingProfileSelection || ProfilesList.SelectedItem is not ProfileEntry target)
            return;
        if (ReferenceEquals(target, _viewModel.SelectedProfile))
            return;

        var decision = _viewModel.IsDirty ? PromptDirty() : DirtyDecision.Discard;
        if (!_viewModel.TrySwitchProfile(target, decision))
        {
            _syncingProfileSelection = true;
            ProfilesList.SelectedItem = _viewModel.SelectedProfile;
            _syncingProfileSelection = false;
        }
    }

    private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        var decision = _viewModel.IsDirty ? PromptDirty() : DirtyDecision.Discard;
        if (!_viewModel.TryReload(decision) && decision == DirtyDecision.Save)
            MessageBox.Show(this, _viewModel.StatusMessage, "AdaptiveBarDesigner", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void PaletteList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.AddSelectedPaletteWidget();
    }

    private void PaletteList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not WidgetDefinition definition)
            return;

        PaletteList.SelectedItem = definition;
        _viewModel.SelectedPaletteDefinition = definition;
        ArmDrag(definition, e.GetPosition(this));
    }

    private void PaletteList_OnPreviewMouseMove(object sender, MouseEventArgs e) =>
        TryStartDrag(sender as DependencyObject ?? PaletteList, e);

    private void PaletteList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        ResetPendingDrag();

    private void PreviewItem_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PanMode) return;
        if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null)
            return;
        if ((sender as FrameworkElement)?.DataContext is not PreviewItemViewModel item)
            return;

        _viewModel.SelectWidgetById(item.WidgetId);
        ArmDrag(item, e.GetPosition(this));
        e.Handled = true;
    }

    private void PreviewItem_OnPreviewMouseMove(object sender, MouseEventArgs e) =>
        TryStartDrag(sender as DependencyObject ?? PreviewSurface, e);

    private void PreviewItem_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        ResetPendingDrag();

    private void PreviewSurface_OnDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDragPayload(e.Data, out var palette, out var widget) ||
            !TryGetInsertionTarget(widget, e.GetPosition(PreviewContentGrid).X, out _, out var indicatorX))
        {
            e.Effects = DragDropEffects.None;
            HideDropIndicator();
            e.Handled = true;
            return;
        }

        ShowDropIndicator(indicatorX);
        e.Effects = palette is null ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void PreviewSurface_OnDragLeave(object sender, DragEventArgs e) =>
        HideDropIndicator();

    private void PreviewSurface_OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!TryGetDragPayload(e.Data, out var palette, out var widget) ||
                !TryGetInsertionTarget(widget, e.GetPosition(PreviewContentGrid).X, out var insertionIndex, out _))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            if (palette is not null)
            {
                if (_viewModel.AddWidget(palette, insertionIndex))
                    e.Effects = DragDropEffects.Copy;
            }
            else if (widget is not null && _viewModel.MoveWidget(widget, insertionIndex))
            {
                e.Effects = DragDropEffects.Move;
            }
        }
        finally
        {
            HideDropIndicator();
            ResetPendingDrag();
            e.Handled = true;
        }
    }

    private void ResizeThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PreviewItemViewModel item ||
            _viewModel.FindWidgetById(item.WidgetId) is not { } widget)
        {
            return;
        }

        _viewModel.SelectWidgetById(item.WidgetId);
        _resizingWidget = widget;
        _viewModel.BeginHistoryGesture();
        _resizeStartDisplayX = Mouse.GetPosition(this).X;
        _resizeStartLogicalWidth = widget.Width > 0 ? widget.Width : GetRenderedLogicalWidth(item);
        _resizeChanged = false;
        ResetPendingDrag();
        e.Handled = true;
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizingWidget is null)
            return;

        var displayDelta = Mouse.GetPosition(this).X - _resizeStartDisplayX;
        var logicalDelta = DesignerCoordinates.DisplayDeltaToLogical(displayDelta, GetPreviewDisplayScale());
        _resizeChanged |= _viewModel.ResizeWidget(_resizingWidget, _resizeStartLogicalWidth + logicalDelta, validate: false);
        e.Handled = true;
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_resizeChanged)
            _viewModel.ValidateCurrent();
        _resizingWidget = null;
        _resizeChanged = false;
        _viewModel.EndHistoryGesture();
        e.Handled = true;
    }

    private void DeletePageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var page = _viewModel.SelectedPage;
        if (page is null)
            return;

        var result = MessageBox.Show(
            this,
            $"Delete page '{page.DisplayName}'?",
            "Delete page",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            _viewModel.DeleteSelectedPage();
    }

    private void DiagnosticsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DiagnosticsList.SelectedItem is ProfileDiagnostic diagnostic)
            _viewModel.SelectDiagnostic(diagnostic);
    }

    private async void BrowseMediaButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedWidgetEditor is not { HasDecorativeMediaSettings: true } editor)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select decorative media",
            Filter = "Supported media|*.png;*.jpg;*.jpeg;*.gif;*.mp4|Images|*.png;*.jpg;*.jpeg;*.gif|Video|*.mp4"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var imported = await _viewModel.ImportMediaAssetAsync(dialog.FileName);
        if (IsLoaded && ReferenceEquals(editor, _viewModel.SelectedWidgetEditor) && imported is not null)
            editor.MediaSource = imported;
    }

    private async void BrowseStickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedWidgetEditor is not { HasStickerSettings: true } editor)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select sticker image",
            Filter = "Sticker images|*.png;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var imported = await _viewModel.ImportMediaAssetAsync(dialog.FileName, sticker: true);
        if (IsLoaded && ReferenceEquals(editor, _viewModel.SelectedWidgetEditor) && imported is not null)
            editor.MediaSource = imported;
    }

    private async void BrowseBackgroundMediaButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select bar background media",
            Filter = "Supported media|*.png;*.jpg;*.jpeg;*.gif;*.mp4|Images|*.png;*.jpg;*.jpeg;*.gif|Video|*.mp4"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var previousSource = _viewModel.AppearanceEditor.BackgroundMediaSource;
        var imported = await _viewModel.ImportMediaAssetAsync(dialog.FileName);
        if (IsLoaded && _viewModel.AppearanceEditor.BackgroundMediaSource == previousSource && imported is not null)
            _viewModel.AppearanceEditor.BackgroundMediaSource = imported;
    }

    private async void SaveLibraryThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            PreviewSurface.UpdateLayout();
            var width = (int)Math.Ceiling(PreviewSurface.ActualWidth);
            var height = (int)Math.Ceiling(PreviewSurface.ActualHeight);
            if (width <= 0 || height <= 0) return;
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(PreviewSurface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var data = new MemoryStream();
            encoder.Save(data);
            if (!await _viewModel.ThemeLibrary.SaveAsync(data.ToArray()))
                ShowThemeLibraryError();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save Theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ApplyLibraryThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ThemeLibraryItem item &&
            !await _viewModel.ThemeLibrary.ApplyAsync(item))
            ShowThemeLibraryError();
    }

    private async void ExportLibraryThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ThemeLibraryItem item) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export theme with background media",
            Filter = "AdaptiveBarPC theme package|*.adaptivebar-theme.zip",
            DefaultExt = ".adaptivebar-theme.zip",
            FileName = string.Concat(item.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))
                + ".adaptivebar-theme.zip"
        };
        if (dialog.ShowDialog(this) == true && !await _viewModel.ThemeLibrary.ExportAsync(item, dialog.FileName))
            ShowThemeLibraryError();
    }

    private async void ImportLibraryThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import theme package",
            Filter = "AdaptiveBarPC theme package|*.adaptivebar-theme.zip;*.flexbar-theme.zip|ZIP files|*.zip"
        };
        if (dialog.ShowDialog(this) == true && !await _viewModel.ThemeLibrary.ImportAsync(dialog.FileName))
            ShowThemeLibraryError();
    }

    private void ShowThemeLibraryError() => MessageBox.Show(this, _viewModel.ThemeLibrary.Message,
        "Theme Library", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void SaveThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save custom theme",
            Filter = "AdaptiveBarPC theme|*.adaptivebar-theme.json",
            DefaultExt = ".adaptivebar-theme.json",
            FileName = $"{_viewModel.AppearanceEditor.SelectedPresetId}-custom.adaptivebar-theme.json"
        };
        if (dialog.ShowDialog(this) == true && !_viewModel.SaveTheme(dialog.FileName))
            MessageBox.Show(this, _viewModel.StatusMessage, "Save Theme", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void LoadThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load custom theme",
            Filter = "AdaptiveBarPC theme|*.adaptivebar-theme.json;*.flexbar-theme.json|JSON files|*.json"
        };
        if (dialog.ShowDialog(this) == true && !_viewModel.LoadTheme(dialog.FileName))
            MessageBox.Show(this, _viewModel.StatusMessage, "Load Theme", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void IconTile_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is IconPickerTile tile)
            _viewModel.SelectedWidgetEditor?.SelectCatalogIconCommand.Execute(tile);
    }

    private void ChooseAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose application for this profile",
            Filter = "Applications|*.exe"
        };
        if (dialog.ShowDialog(this) == true)
            _viewModel.AssignApplication(dialog.FileName);
    }

    private void DeleteProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var profile = _viewModel.SelectedProfile;
        if (profile is null)
            return;

        var unsaved = _viewModel.IsDirty ? "\nUnsaved changes in this profile will be discarded." : "";
        var result = MessageBox.Show(
            this,
            $"Delete profile '{profile.DisplayName}'?\nThis deletes {profile.FileName}.{unsaved}",
            "Delete profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _viewModel.DeleteSelectedProfile();
    }

    private void ArmDrag(object source, Point start)
    {
        _pendingDrag = source;
        _dragStart = start;
    }

    private void ResetPendingDrag() => _pendingDrag = null;

    private void TryStartDrag(DependencyObject source, MouseEventArgs e)
    {
        if (_pendingDrag is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetPendingDrag();
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        var effects = DragDropEffects.None;
        if (_pendingDrag is WidgetDefinition definition)
        {
            data.SetData(PaletteDragFormat, definition.TypeId);
            effects = DragDropEffects.Copy;
        }
        else if (_pendingDrag is PreviewItemViewModel item)
        {
            data.SetData(WidgetDragFormat, item.WidgetId);
            effects = DragDropEffects.Move;
        }

        ResetPendingDrag();
        if (effects == DragDropEffects.None)
            return;

        try
        {
            DragDrop.DoDragDrop(source, data, effects);
        }
        finally
        {
            HideDropIndicator();
            _viewModel.EndHistoryGesture();
        }
    }

    private bool TryGetDragPayload(IDataObject data, out WidgetDefinition? palette, out ControlDefinition? widget)
    {
        palette = null;
        widget = null;

        if (data.GetDataPresent(PaletteDragFormat) && data.GetData(PaletteDragFormat) is string typeId)
        {
            if (!WidgetDefinitionRegistry.TryGet(typeId, out var definition) || !definition.IsPlaceable)
                return false;
            palette = definition;
            return true;
        }

        if (!data.GetDataPresent(WidgetDragFormat) || data.GetData(WidgetDragFormat) is not string widgetId)
            return false;

        widget = _viewModel.FindWidgetById(widgetId);
        return widget is not null;
    }

    internal bool TryGetInsertionTarget(ControlDefinition? draggedWidget, double pointerX, out int insertionIndex, out double indicatorX)
    {
        insertionIndex = 0;
        indicatorX = 0;
        var page = _viewModel.SelectedPage;
        if (page is null)
            return false;

        var placement = draggedWidget is null ? "Current Page" : _viewModel.GetWidgetPlacement(draggedWidget);
        var system = draggedWidget?.System == true;
        var control = placement switch
        {
            "Fixed Left" or "Fixed Left (All Profiles)" => FixedLeftPreviewItems,
            "Fixed Right" or "Fixed Right (All Profiles)" => FixedRightPreviewItems,
            _ when system => SystemPreviewItems,
            _ => MainPreviewItems
        };
        var previews = placement switch
        {
            "Fixed Left" or "Fixed Left (All Profiles)" => _viewModel.FixedLeftPreviewItems,
            "Fixed Right" or "Fixed Right (All Profiles)" => _viewModel.FixedRightPreviewItems,
            _ when system => _viewModel.SystemPreviewItems,
            _ => _viewModel.PreviewItems
        };
        var items = draggedWidget is null
            ? page.Items
            : _viewModel.GetWidgetCollection(draggedWidget);
        if (items is null)
            return false;
        var rendered = new List<(ControlDefinition Widget, double Left, double Width)>();

        foreach (var preview in previews)
        {
            if (control.ItemContainerGenerator.ContainerFromItem(preview) is not FrameworkElement container)
                continue;
            var widget = items.FirstOrDefault(x =>
                string.Equals(x.Id, preview.WidgetId, StringComparison.OrdinalIgnoreCase));
            if (widget is null)
                continue;

            var left = container.TransformToAncestor(PreviewContentGrid).Transform(new Point(0, 0)).X;
            rendered.Add((widget, left, container.ActualWidth));
        }

        rendered.Sort((a, b) => a.Left.CompareTo(b.Left));
        if (rendered.Count == 0)
        {
            insertionIndex = system
                ? items.Count
                : items.FindIndex(x => x.System) is var firstSystem && firstSystem >= 0
                    ? firstSystem
                    : items.Count;
            indicatorX = system
                ? Math.Max(0, PreviewContentGrid.ActualWidth - 2)
                : MainPreviewHost.TransformToAncestor(PreviewContentGrid).Transform(new Point(2, 0)).X;
            return true;
        }

        var before = rendered.FirstOrDefault(x => pointerX < x.Left + (x.Width / 2));
        if (before.Widget is not null)
        {
            insertionIndex = items.IndexOf(before.Widget);
            indicatorX = before.Left - 1;
        }
        else
        {
            var last = rendered[^1];
            insertionIndex = items.IndexOf(last.Widget) + 1;
            indicatorX = last.Left + last.Width + 1;
        }

        indicatorX = Math.Clamp(indicatorX, 0, Math.Max(0, PreviewContentGrid.ActualWidth - 2));
        return insertionIndex >= 0;
    }

    private void ShowDropIndicator(double x)
    {
        Canvas.SetLeft(DropIndicator, x);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;

    private double GetRenderedLogicalWidth(PreviewItemViewModel item)
    {
        var control = _viewModel.FixedLeftPreviewItems.Contains(item)
            ? FixedLeftPreviewItems
            : _viewModel.FixedRightPreviewItems.Contains(item)
                ? FixedRightPreviewItems
                : _viewModel.SystemPreviewItems.Contains(item)
                    ? SystemPreviewItems
                    : MainPreviewItems;
        return control.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container && container.ActualWidth > 0
            ? container.ActualWidth
            : 120;
    }

    private double GetPreviewDisplayScale()
    {
        if (PreviewSurface.ActualWidth <= 0)
            return 1;
        var transform = PreviewSurface.TransformToAncestor(this);
        var left = transform.Transform(new Point(0, 0)).X;
        var right = transform.Transform(new Point(PreviewSurface.ActualWidth, 0)).X;
        var scale = Math.Abs(right - left) / PreviewSurface.ActualWidth;
        return double.IsFinite(scale) && scale > 0 ? scale : 1;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsDirty)
        {
            var decision = PromptDirty();
            if (!_viewModel.ResolveDirty(decision))
            {
                e.Cancel = true;
                if (decision == DirtyDecision.Save)
                    MessageBox.Show(this, _viewModel.StatusMessage, "AdaptiveBarDesigner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        base.OnClosing(e);
    }

    private DirtyDecision PromptDirty()
    {
        var result = MessageBox.Show(
            this,
            $"Save changes to {_viewModel.SelectedProfile?.DisplayName ?? "the current profile"}?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => DirtyDecision.Save,
            MessageBoxResult.No => DirtyDecision.Discard,
            _ => DirtyDecision.Cancel
        };
    }
}

public static class DesignerCoordinates
{
    public static double? FitScale(double viewportWidth, double viewportHeight, double frameWidth, double frameHeight)
    {
        if (!double.IsFinite(viewportWidth + viewportHeight + frameWidth + frameHeight) ||
            viewportWidth <= 48 || viewportHeight <= 48 || frameWidth <= 0 || frameHeight <= 0) return null;
        return Math.Clamp(Math.Min((viewportWidth - 48) / frameWidth, (viewportHeight - 48) / frameHeight), 0.1, 2);
    }

    public static double DisplayDeltaToLogical(double displayDelta, double displayScale) =>
        double.IsFinite(displayScale) && displayScale > 0 ? displayDelta / displayScale : displayDelta;
}
