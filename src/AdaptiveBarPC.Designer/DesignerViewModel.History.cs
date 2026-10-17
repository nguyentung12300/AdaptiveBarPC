using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services;

namespace AdaptiveBarPC.Designer;

public sealed partial class DesignerViewModel
{
    private sealed record HistorySnapshot(string Document, string Appearance, string? PageId,
        string? WidgetId, string? Placement)
    {
        public bool SameData(HistorySnapshot other) => Document == other.Document && Appearance == other.Appearance;
    }

    // ponytail: bounded JSON snapshots reuse the data contracts; use deltas if large profiles make this costly.
    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private readonly List<HistorySnapshot> _undoHistory = new();
    private readonly List<HistorySnapshot> _redoHistory = new();
    private HistorySnapshot? _historyCurrent;
    private string? _savedDocumentState;
    private string? _historyKey;
    private long _historyEditTime;
    private bool _historyGesture;
    private bool _restoringHistory;

    public ICommand UndoCommand { get; private set; } = null!;
    public ICommand RedoCommand { get; private set; } = null!;
    public bool CanUndo => _undoHistory.Count > 0 && ThemeLibrary.IsAvailable;
    public bool CanRedo => _redoHistory.Count > 0 && ThemeLibrary.IsAvailable;

    private HistorySnapshot CaptureHistory() => new(
        JsonSerializer.Serialize(_document, HistoryJson),
        JsonSerializer.Serialize(_globalAppearance, HistoryJson),
        SelectedPage?.Id, SelectedWidget?.Id,
        SelectedWidget is null ? null : GetWidgetPlacement(SelectedWidget));

    private void ResetHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _historyCurrent = CaptureHistory();
        _savedDocumentState = _historyCurrent.Document;
        EndHistoryGesture();
        NotifyHistoryChanged();
    }

    private void MarkHistorySaved()
    {
        _savedDocumentState = JsonSerializer.Serialize(_document, HistoryJson);
        EndHistoryGesture();
    }

    internal void BeginHistoryGesture()
    {
        if (_historyGesture) return;
        _historyGesture = true;
        _historyKey = null;
    }

    internal void EndHistoryGesture()
    {
        _historyGesture = false;
        _historyKey = null;
    }

    private void UpdateHistorySelection()
    {
        if (_restoringHistory || _historyCurrent is null) return;
        var state = CaptureHistory();
        // Navigation is not an edit. Do not replace the pre-edit selection during an add/delete.
        if (_historyCurrent.SameData(state))
        {
            _historyCurrent = state;
            _historyKey = null;
        }
    }

    private void RecordHistory(string? key = null)
    {
        if (_restoringHistory || _historyCurrent is null) return;
        var next = CaptureHistory();
        if (_historyCurrent.SameData(next)) return;
        if (_historyGesture) key = "pointer-gesture";
        var now = Environment.TickCount64;
        var combine = key is not null && key == _historyKey && _undoHistory.Count > 0 &&
                      _redoHistory.Count == 0 && (_historyGesture || now - _historyEditTime <= 750);
        if (!combine)
        {
            _undoHistory.Add(_historyCurrent);
            if (_undoHistory.Count > 100) _undoHistory.RemoveAt(0);
        }
        _redoHistory.Clear();
        _historyCurrent = next;
        _historyKey = key;
        _historyEditTime = now;
        IsDirty = next.Document != _savedDocumentState;
        NotifyHistoryChanged();
    }

    public bool Undo() => RestoreHistory(_undoHistory, _redoHistory, "Undo");
    public bool Redo() => RestoreHistory(_redoHistory, _undoHistory, "Redo");

    private bool RestoreHistory(List<HistorySnapshot> source, List<HistorySnapshot> destination, string label)
    {
        if (source.Count == 0 || _historyCurrent is null || !ThemeLibrary.IsAvailable) return false;
        EndHistoryGesture();
        var target = source[^1];
        var previous = _historyCurrent;
        var keepMediaEditor = IsDecorativeMediaEditMode && SelectedWidget?.Id == target.WidgetId;
        _restoringHistory = true;
        try
        {
            var document = JsonSerializer.Deserialize<ProfileDefinition>(target.Document, HistoryJson);
            var appearance = JsonSerializer.Deserialize<GlobalAppearanceSettings>(target.Appearance, HistoryJson)!;
            var appearanceChanged = previous.Appearance != target.Appearance;
            // Persist first: a failed global write must leave both the current state and history intact.
            if (appearanceChanged || _appearanceSavePending)
                GlobalAppearanceStore.Save(appearance, _appearanceFilePath);
            _appearanceSaveTimer.Stop();
            _appearanceSavePending = false;
            if (appearanceChanged) _globalAppearance = appearance;
            if (previous.Document != target.Document)
            {
                _document = document;
                BuildPages();
            }
            SelectedPage = Pages.FirstOrDefault(p => p.Id == target.PageId) ?? Pages.FirstOrDefault();
            SelectedWidget = HistoryWidget(target) ?? SelectedPage?.Items.FirstOrDefault();
            if (keepMediaEditor && SelectedWidget?.Type == WidgetTypes.DecorativeMedia)
                IsDecorativeMediaEditMode = true;
            if (SelectedProfile is not null && _document is not null)
                SelectedProfile.DisplayName = _document.DisplayName;
            if (appearanceChanged)
            {
                AppearanceEditor.LoadFromSettings(_globalAppearance);
                NotifyAppearanceChanged();
            }
            foreach (var property in new[] { nameof(Document), nameof(ProfileDisplayName), nameof(ProfileId),
                         nameof(SchemaVersion), nameof(MatchSummary), nameof(ActiveContext), nameof(CurrentWidgets),
                         nameof(PageDisplayName), nameof(CurrentPageIdentity), nameof(InputItems) })
                OnPropertyChanged(property);
            RefreshPreview();
            ValidateCurrent();
            source.RemoveAt(source.Count - 1);
            destination.Add(previous);
            _historyCurrent = CaptureHistory();
            IsDirty = _historyCurrent.Document != _savedDocumentState;
            StatusMessage = $"{label} completed";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"{label} failed: {ex.Message}";
            return false;
        }
        finally
        {
            _restoringHistory = false;
            NotifyHistoryChanged();
        }
    }

    private ControlDefinition? HistoryWidget(HistorySnapshot state)
    {
        var items = state.Placement switch
        {
            "Fixed Left (All Profiles)" => _globalAppearance.FixedLeft,
            "Fixed Right (All Profiles)" => _globalAppearance.FixedRight,
            "Input Tools (All Profiles)" => _globalAppearance.InputItems,
            "Fixed Left" => _document?.FixedLeft,
            "Fixed Right" => _document?.FixedRight,
            _ => SelectedPage?.Items
        };
        return items?.FirstOrDefault(w => w.Id == state.WidgetId);
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        CommandManager.InvalidateRequerySuggested();
    }
}
