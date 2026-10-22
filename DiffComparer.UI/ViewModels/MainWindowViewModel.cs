using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffComparer.Core;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Models;
using DiffComparer.UI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaEdit.Document;

namespace DiffComparer.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IFilePickerService _filePicker;
    private readonly IReportExportService? _exporter;
    private ComparisonSnapshot _snapshot = ComparisonSnapshot.Empty;
    private readonly HashSet<int> _reviewed = new();
    private Dictionary<int, int> _blockByStart = new();
    private CancellationTokenSource? _viewCancellation;
    private TextFileContent? _leftFile, _rightFile;
    private bool _applyingFile, _leftEdited, _rightEdited;
    private bool _disposed;
    private CancellationTokenSource? _liveCancellation;
    private readonly SemaphoreSlim _comparisonLock = new(1, 1);
    public Task PendingComparison { get; private set; } = Task.CompletedTask;

    private void ScheduleComparison()
    {
        if (_disposed) return;
        _liveCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _liveCancellation = cancellation;
        NotifyWorkspaceState();
        PendingComparison = UpdateAsync();
        async Task UpdateAsync()
        {
            try
            {
                await Task.Delay(250, cancellation.Token);
                await RunComparisonAsync(cancellation.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_liveCancellation, cancellation)) { _liveCancellation = null; NotifyWorkspaceState(); }
                cancellation.Dispose();
            }
        }
    }
    public ComparisonSession Session { get; } = new();
    private int _comparisonGeneration;
    private int? _preferredOffset;
    private CopyDirection _lastDirection = CopyDirection.LeftToRight;
    public ComparisonResult? CanonicalResult => _snapshot.Canonical;
    public TextDocument LeftDocument => Session.Left;
    public TextDocument RightDocument => Session.Right;

    public MainWindowViewModel(IFilePickerService filePicker, IReportExportService? exporter = null)
    {
        Session.DocumentChanged += DocumentChanged;
        Session.HistoryChanged += HistoryChanged;
        _filePicker = filePicker;
        _exporter = exporter ?? filePicker as IReportExportService;
    }

    public string LeftText { get => Session.Left.Text; set { if (Session.Left.Text != value) Session.Left.Text = value; } }
    public string RightText { get => Session.Right.Text; set { if (Session.Right.Text != value) Session.Right.Text = value; } }
    [ObservableProperty] private bool _compareLineEndings;
    [ObservableProperty] private bool _preserveDestinationEndings = true;
    [ObservableProperty] private string _selectedStrategy = "Legible";
    public string[] Strategies { get; } = { "Legible", "Mínimo" };
    [ObservableProperty] private bool _isEditMode = true;
    [ObservableProperty] private bool _isComparing;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isUpdatingView;
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private int _currentChangeIndex = -1;
    [ObservableProperty] private ComparisonView _view = ComparisonView.Empty;
    [ObservableProperty] private bool _ignoreWhitespace;
    [ObservableProperty] private bool _onlyDifferences;
    [ObservableProperty] private int _contextLines = 3;
    [ObservableProperty] private bool _showAdded = true;
    [ObservableProperty] private bool _showDeleted = true;
    [ObservableProperty] private bool _showModified = true;
    [ObservableProperty] private bool _movedOnly;
    [ObservableProperty] private bool _unreviewedOnly;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _searchOnly;
    [ObservableProperty] private int _currentSearchIndex = -1;
    [ObservableProperty] private bool _showWhitespace;
    [ObservableProperty] private DocumentSide _activeSide;
    [ObservableProperty] private string _selectedEncoding = "Automático";
    [ObservableProperty] private string _selectedLanguage = "Automático";

    public string[] Encodings { get; } = { "Automático", "UTF-8", "UTF-16 LE", "UTF-16 BE", "Windows-1252" };
    public string[] Languages { get; } = { "Automático", "Texto", "C#", "JavaScript", "JSON", "XML", "Python", "SQL" };
    public string LeftName => _leftFile?.Name ?? "Izquierda.txt";
    public string RightName => _rightFile?.Name ?? "Derecha.txt";
    public string LeftPath => _leftFile?.Path ?? "Texto pegado o escrito";
    public string RightPath => _rightFile?.Path ?? "Texto pegado o escrito";
    public string LeftHeader => Header(_leftFile, "Texto izquierdo", _leftEdited);
    public string RightHeader => Header(_rightFile, "Texto derecho", _rightEdited);
    private static string Header(TextFileContent? file, string fallback, bool edited) =>
        (file is null ? fallback : $"{file.Name} · {file.EncodingName} · {file.LineEndings}") + (edited ? " · editado" : "");
    public IReadOnlyList<LineMap> DiffLines => _snapshot.Lines;
    public int AddedCount => _snapshot.Added;
    public int DeletedCount => _snapshot.Deleted;
    public int ModifiedCount => _snapshot.Modified;
    public int DiscrepancyCount => AddedCount + DeletedCount + ModifiedCount;
    public int ChangeBlockCount => _snapshot.Blocks.Length;
    public int MovedBlockCount => _snapshot.MovedBlocks;
    public string SummaryText => $"{DiscrepancyCount} líneas con discrepancias · +{AddedCount} agregadas · -{DeletedCount} eliminadas · ~{ModifiedCount} modificadas · ↔ {MovedBlockCount} bloques movidos";
    public bool HasStatus => StatsText.Length > 0;
    public string ReviewText => $"{_reviewed.Count} de {ChangeBlockCount} bloques revisados";
    public bool CurrentReviewed => _blockByStart.TryGetValue(CurrentChangeIndex, out int block) && _reviewed.Contains(block);
    public string ReviewButtonText => CurrentReviewed ? "Quitar revisión" : "Marcar revisado";
    public string NavigationText
    {
        get
        {
            if (ChangeBlockCount == 0) return "Sin discrepancias";
            if (View.VisibleBlocks.Length == 0) return "Sin bloques con estos filtros";
            int block = _blockByStart.GetValueOrDefault(CurrentChangeIndex, -1);
            int index = Array.IndexOf(View.VisibleBlocks, block);
            return View.VisibleBlocks.Length == ChangeBlockCount ? $"Bloque {index + 1} de {ChangeBlockCount}"
                : $"Bloque {index + 1} de {View.VisibleBlocks.Length} visibles ({ChangeBlockCount} total)";
        }
    }
    public string CurrentChangeDetails
    {
        get
        {
            if (!_blockByStart.TryGetValue(CurrentChangeIndex, out int id)) return "";
            var block = _snapshot.Blocks[id];
            var lines = _snapshot.Lines.AsSpan(block.Start, block.End - block.Start + 1);
            int? left = null, right = null, partner = null;
            bool endingChanged = false;
            foreach (var line in lines) { left ??= line.LeftLine; right ??= line.RightLine; partner ??= line.MovedPartnerLine; endingChanged |= line.HasTerminatorChange; }
            string location = $"Línea original: izquierda {(left.HasValue ? (left + 1).ToString() : "—")} · derecha {(right.HasValue ? (right + 1).ToString() : "—")}";
            if (endingChanged) location += " · ¶ Cambio de terminador o salto final";
            return partner.HasValue ? location + $" · ↔ Movido: línea {partner + 1} del otro archivo" : location;
        }
    }
    public string SearchStatus => View.SearchHits.Length == 0 ? (SearchText.Length == 0 ? "" : "Sin coincidencias visibles")
        : $"{CurrentSearchIndex + 1} de {View.SearchHits.Length} coincidencias" + (View.SearchLimitReached ? " (límite de resultados)" : "");
    public bool IsBusy => IsComparing || IsLoading || IsExporting || IsSaving;
    public bool CanChangeView => !IsEditMode && !IsBusy;
    public bool CanConfigureComparison => !IsLoading && !IsSaving && !IsExporting;
    public bool HasViewFilter => OnlyDifferences || MovedOnly || UnreviewedOnly || SearchOnly
        || !ShowAdded || !ShowDeleted || !ShowModified;
    public Task PendingViewUpdate { get; private set; } = Task.CompletedTask;
    private void ViewFilterChanged()
    {
        OnPropertyChanged(nameof(HasViewFilter));
        NotifyWorkspaceState();
        ScheduleView();
    }
    public event Action? SearchFocusRequested;
    public event Action? ChangeNavigationRequested;

    [RelayCommand]
    private void FocusSearch() => SearchFocusRequested?.Invoke();

    private void DocumentChanged(DocumentSide side)
    {
        if (side == DocumentSide.Left) { if (!_applyingFile) _leftEdited = true; OnPropertyChanged(nameof(LeftText)); }
        else { if (!_applyingFile) _rightEdited = true; OnPropertyChanged(nameof(RightText)); }
        NotifyDocumentState();
        ScheduleComparison();
    }
    private void HistoryChanged()
    {
        _leftEdited = !Session.Left.UndoStack.IsOriginalFile;
        _rightEdited = !Session.Right.UndoStack.IsOriginalFile;
        NotifyDocumentState();
    }
    private void NotifyDocumentState()
    {
        OnPropertyChanged(nameof(LeftHeader)); OnPropertyChanged(nameof(RightHeader));
        OnPropertyChanged(nameof(IsResultCurrent)); OnPropertyChanged(nameof(ResultNeedsRefresh));
        OnPropertyChanged(nameof(CopyHint));
        RefreshCommands();
    }
    public bool IsResultCurrent => CanonicalResult is { } result && Session.Matches(result) && result.Options == EngineOptions();
    public bool ResultNeedsRefresh => !IsEditMode && !IsResultCurrent;
    public string CopyHint => ResultNeedsRefresh ? "Actualizando diferencias…"
        : !IsEditMode && CurrentChangeIndex >= 0 && !CanCopySelected() && !IsBusy && !IsUpdatingView
        ? "Muestra el bloque completo para copiar: hay cambios ocultos por los filtros." : "";
    private DiffOptions EngineOptions() => new(IgnoreWhitespace, CompareLineEndings,
        SelectedStrategy == "Mínimo" ? DiffStrategy.Minimal : DiffStrategy.Readable);
    partial void OnIgnoreWhitespaceChanged(bool value) { NotifyDocumentState(); ScheduleComparison(); }
    partial void OnCompareLineEndingsChanged(bool value) { NotifyDocumentState(); ScheduleComparison(); }
    partial void OnSelectedStrategyChanged(string value) { NotifyDocumentState(); ScheduleComparison(); }

    partial void OnIsComparingChanged(bool value) => RefreshCommands();
    partial void OnIsSavingChanged(bool value) => RefreshCommands();
    partial void OnIsLoadingChanged(bool value) => RefreshCommands();
    partial void OnIsExportingChanged(bool value) => RefreshCommands();
    partial void OnIsUpdatingViewChanged(bool value) => RefreshCommands();
    partial void OnIsEditModeChanged(bool value) => RefreshCommands();
    partial void OnActiveSideChanged(DocumentSide value) => RefreshCommands();
    partial void OnStatsTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatus)); OnPropertyChanged(nameof(WorkspaceStatus));
    }
    partial void OnCurrentChangeIndexChanged(int value) => NotifyNavigation();
    partial void OnCurrentSearchIndexChanged(int value) => OnPropertyChanged(nameof(SearchStatus));
    partial void OnOnlyDifferencesChanged(bool value) => ViewFilterChanged();
    partial void OnContextLinesChanged(int value) { NotifyWorkspaceState(); ScheduleView(); }
    partial void OnShowAddedChanged(bool value) => ViewFilterChanged();
    partial void OnShowDeletedChanged(bool value) => ViewFilterChanged();
    partial void OnShowModifiedChanged(bool value) => ViewFilterChanged();
    partial void OnMovedOnlyChanged(bool value) => ViewFilterChanged();
    partial void OnUnreviewedOnlyChanged(bool value) => ViewFilterChanged();
    partial void OnSearchOnlyChanged(bool value) => ViewFilterChanged();
    partial void OnSearchTextChanged(string value) => ScheduleView(180);

    private ViewOptions Options() => new(OnlyDifferences, ContextLines, ShowAdded, ShowDeleted,
        ShowModified, MovedOnly, UnreviewedOnly, SearchText, SearchOnly);
    private bool CanEdit() => !IsBusy;
    private bool CanCompare() => !IsBusy;
    private bool CanNavigate() => CanChangeView && IsResultCurrent && !IsUpdatingView && View.VisibleBlocks.Length > 0;
    private bool CanFind() => CanChangeView && IsResultCurrent && !IsUpdatingView && View.SearchHits.Length > 0;
    private bool CanExport() => CanChangeView && IsResultCurrent && _exporter is not null;
    private bool CanReview() => CanNavigate() && _blockByStart.ContainsKey(CurrentChangeIndex);

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanChangeView));
        OnPropertyChanged(nameof(CanConfigureComparison));
        CompareCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        OpenLeftFileCommand.NotifyCanExecuteChanged();
        OpenRightFileCommand.NotifyCanExecuteChanged();
        NextChangeCommand.NotifyCanExecuteChanged();
        PreviousChangeCommand.NotifyCanExecuteChanged();
        FindNextCommand.NotifyCanExecuteChanged();
        FindPreviousCommand.NotifyCanExecuteChanged();
        ToggleReviewedCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
        ExportPatchCommand.NotifyCanExecuteChanged();
        SaveLeftAsCommand.NotifyCanExecuteChanged(); SaveRightAsCommand.NotifyCanExecuteChanged();
        CopyToRightCommand.NotifyCanExecuteChanged(); CopyToLeftCommand.NotifyCanExecuteChanged();
        ApplyChangeCommand.NotifyCanExecuteChanged();
        UndoMergeCommand.NotifyCanExecuteChanged(); RedoMergeCommand.NotifyCanExecuteChanged();
        NotifyWorkspaceState();
    }

    private void NotifyNavigation()
    {
        foreach (string name in new[] { nameof(NavigationText), nameof(CurrentChangeDetails), nameof(ReviewText), nameof(CurrentReviewed), nameof(ReviewButtonText), nameof(CopyHint) })
            OnPropertyChanged(name);
        RefreshCommands();
    }
    private void SetSnapshot(ComparisonSnapshot snapshot)
    {
        _snapshot = snapshot;
        _blockByStart = snapshot.Blocks.Select((block, id) => (block.Start, id)).ToDictionary(x => x.Start, x => x.id);
        foreach (var name in new[] { nameof(DiffLines), nameof(AddedCount), nameof(DeletedCount), nameof(ModifiedCount), nameof(DiscrepancyCount), nameof(ChangeBlockCount), nameof(MovedBlockCount), nameof(SummaryText) })
            OnPropertyChanged(name);
        OnPropertyChanged(nameof(CanonicalResult));
        NotifyDocumentState();
        NotifyNavigation();
    }
    private void SetView(ComparisonView view)
    {
        View = view;
        int oldBlock = _blockByStart.GetValueOrDefault(CurrentChangeIndex, -1);
        if (!view.VisibleBlocks.Contains(oldBlock))
            CurrentChangeIndex = view.VisibleBlocks.Length == 0 ? -1 : _snapshot.Blocks[view.VisibleBlocks[0]].Start;
        CurrentSearchIndex = -1;
        OnPropertyChanged(nameof(SearchStatus));
        NotifyNavigation();
    }
    private void ScheduleView(int delay = 0)
    {
        if (_disposed || IsEditMode || IsComparing) return;
        _viewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _viewCancellation = cancellation;
        var snapshot = _snapshot;
        var options = Options();
        var reviewed = new HashSet<int>(_reviewed);
        PendingViewUpdate = UpdateViewAsync();
        async Task UpdateViewAsync()
        {
            IsUpdatingView = true;
            try
            {
                if (delay > 0) await Task.Delay(delay, cancellation.Token);
                var view = await Task.Run(() => ComparisonView.Build(snapshot, options, reviewed, cancellation.Token), cancellation.Token);
                if (!_disposed && ReferenceEquals(_viewCancellation, cancellation) && ReferenceEquals(_snapshot, snapshot)) SetView(view);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatsText = $"No se pudo actualizar la vista: {ex.Message}"; }
            finally
            {
                if (ReferenceEquals(_viewCancellation, cancellation))
                {
                    _viewCancellation = null;
                    IsUpdatingView = false;
                }
                cancellation.Dispose();
            }
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanCompare))]
    private async Task CompareAsync(CancellationToken ct)
    {
        _liveCancellation?.Cancel();
        await RunComparisonAsync(ct);
    }

    private async Task RunComparisonAsync(CancellationToken ct)
    {
        await _comparisonLock.WaitAsync(ct);
        try { await ComputeComparisonAsync(ct); }
        finally { _comparisonLock.Release(); }
    }

    private async Task ComputeComparisonAsync(CancellationToken ct)
    {
        _viewCancellation?.Cancel(); _viewCancellation = null; IsUpdatingView = false;
        IsComparing = true;
        StatsText = "Alineando líneas y refinando cambios…";
        int generation = ++_comparisonGeneration;
        var capture = Session.Capture();
        var engineOptions = EngineOptions();
        var options = Options();
        var retained = ReviewIdentities();
        try
        {
            var computed = await Task.Run(() =>
            {
                var canonical = new DiffEngine(ct).Analyze(capture.Left.Materialize(), capture.Right.Materialize(), engineOptions);
                var snapshot = new ComparisonSnapshot(canonical.Lines.ToArray(), engineOptions.IgnoreWhitespace, false, canonical);
                var reviewed = RetainedReviews(canonical, retained);
                var view = ComparisonView.Build(snapshot, options, reviewed, ct);
                return (canonical, snapshot, reviewed, view);
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (_disposed || generation != _comparisonGeneration || !Session.Matches(computed.canonical) || engineOptions != EngineOptions())
            { StatsText = "Actualizando diferencias…"; return; }
            _reviewed.Clear(); _reviewed.UnionWith(computed.reviewed);
            CurrentChangeIndex = -1;
            SetSnapshot(computed.snapshot);
            SetView(computed.view);
            if (_preferredOffset is int preferred && View.VisibleBlocks.Length > 0)
            {
                var ids = View.VisibleBlocks;
                int chosen = ids.FirstOrDefault(id => (_lastDirection == CopyDirection.LeftToRight
                    ? computed.canonical.Hunks[id].RightText.Offset : computed.canonical.Hunks[id].LeftText.Offset) >= preferred, ids[^1]);
                CurrentChangeIndex = computed.canonical.Hunks[chosen].RowStart;
            }
            _preferredOffset = null;
            IsEditMode = false;
            StatsText = "";
            NotifyDocumentState();
        }
        catch (OperationCanceledException) { StatsText = ""; }
        catch (Exception ex) { StatsText = $"No se pudo comparar: {ex.Message}"; }
        finally { IsComparing = false; }
    }

    private readonly record struct ReviewIdentity(string Left, string Right, string BeforeLeft, string AfterLeft, string BeforeRight, string AfterRight);
    private static ReviewIdentity Identity(ComparisonResult result, DiffHunk hunk)
    {
        string Before(TextSnapshot document, LineRange range) => range.Start == 0 ? "" : document.Lines[range.Start - 1].Content;
        string After(TextSnapshot document, LineRange range) => range.End >= document.Lines.Length ? "" : document.Lines[range.End].Content;
        return new(result.Left.Slice(hunk.LeftText), result.Right.Slice(hunk.RightText), Before(result.Left, hunk.LeftLines),
            After(result.Left, hunk.LeftLines), Before(result.Right, hunk.RightLines), After(result.Right, hunk.RightLines));
    }
    private HashSet<ReviewIdentity> ReviewIdentities()
    {
        if (CanonicalResult is not { } result) return new();
        var all = result.Hunks.Select(h => (Hunk: h, Key: Identity(result, h))).ToArray();
        return all.GroupBy(x => x.Key).Where(g => g.Count() == 1 && _reviewed.Contains(g.First().Hunk.Id)).Select(g => g.Key).ToHashSet();
    }
    private static HashSet<int> RetainedReviews(ComparisonResult result, HashSet<ReviewIdentity> old) =>
        result.Hunks.Select(h => (Hunk: h, Key: Identity(result, h))).GroupBy(x => x.Key)
            .Where(g => g.Count() == 1 && old.Contains(g.Key)).Select(g => g.First().Hunk.Id).ToHashSet();

    private bool CanCopySelected() => _blockByStart.TryGetValue(CurrentChangeIndex, out int id) && CanonicalResult is { } result
        && CanApplyRequest(new(result.Id, id, CopyDirection.LeftToRight));
    private bool CanApplyRequest(CopyRequest? request) => request is not null && CanonicalResult is { } result
        && !IsEditMode && !IsBusy && !IsUpdatingView && IsResultCurrent && request.SnapshotId == result.Id
        && (uint)request.HunkId < (uint)result.Hunks.Length && View.FullyVisibleHunks.Contains(request.HunkId);

    [RelayCommand(CanExecute = nameof(CanCopySelected))]
    private Task CopyToRightAsync() => CopySelectedAsync(CopyDirection.LeftToRight);
    [RelayCommand(CanExecute = nameof(CanCopySelected))]
    private Task CopyToLeftAsync() => CopySelectedAsync(CopyDirection.RightToLeft);
    private Task CopySelectedAsync(CopyDirection direction) => CanCopySelected() && CanonicalResult is { } result
        ? ApplyChangeAsync(new(result.Id, _blockByStart[CurrentChangeIndex], direction)) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanApplyRequest))]
    private async Task ApplyChangeAsync(CopyRequest request)
    {
        if (!CanApplyRequest(request) || CanonicalResult is not { } result) return;
        try
        {
            var hunk = result.Hunks[request.HunkId];
            _lastDirection = request.Direction;
            _preferredOffset = request.Direction == CopyDirection.LeftToRight ? hunk.RightText.Offset : hunk.LeftText.Offset;
            if (Session.Apply(result, request, PreserveDestinationEndings)) await CompareCommand.ExecuteAsync(null);
        }
        catch (Exception ex) { StatsText = $"No se pudo copiar el cambio: {ex.Message}"; }
    }
    private TextDocument ActiveDocument => ActiveSide == DocumentSide.Left ? LeftDocument : RightDocument;
    private bool CanUndoMerge() => !IsBusy && (Session.CanUndo || ActiveDocument.UndoStack.CanUndo);
    private bool CanRedoMerge() => !IsBusy && (Session.CanRedo || ActiveDocument.UndoStack.CanRedo);
    [RelayCommand(CanExecute = nameof(CanUndoMerge))]
    private async Task UndoMergeAsync()
    {
        if (Session.CanUndo) Session.Undo(); else ActiveDocument.UndoStack.Undo();
        await CompareCommand.ExecuteAsync(null);
    }
    [RelayCommand(CanExecute = nameof(CanRedoMerge))]
    private async Task RedoMergeAsync()
    {
        if (Session.CanRedo) Session.Redo(); else ActiveDocument.UndoStack.Redo();
        await CompareCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        _viewCancellation?.Cancel();
        _viewCancellation = null;
        IsUpdatingView = false;
        IsEditMode = true;
        StatsText = "";
        _reviewed.Clear();
        CurrentChangeIndex = -1;
        SetSnapshot(ComparisonSnapshot.Empty);
        SetView(ComparisonView.Empty);
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task OpenLeftFileAsync() => OpenFileAsync(true);
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task OpenRightFileAsync() => OpenFileAsync(false);
    private async Task OpenFileAsync(bool left)
    {
        IsLoading = true;
        try
        {
            var file = await _filePicker.OpenFileAsync(SelectedEncoding);
            if (file is null) return;
            Edit();
            _applyingFile = true;
            try
            {
                if (left) { _leftFile = file; Session.Load(DocumentSide.Left, file.Text); _leftEdited = false; }
                else { _rightFile = file; Session.Load(DocumentSide.Right, file.Text); _rightEdited = false; }
            }
            finally { _applyingFile = false; }
            foreach (var name in new[] { nameof(LeftName), nameof(RightName), nameof(LeftPath), nameof(RightPath), nameof(LeftHeader), nameof(RightHeader) }) OnPropertyChanged(name);
        }
        catch (Exception ex) { StatsText = $"No se pudo abrir el archivo: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void NextChange() => Navigate(1);
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void PreviousChange() => Navigate(-1);
    private void Navigate(int direction)
    {
        var blocks = View.VisibleBlocks;
        if (blocks.Length == 0) return;
        int index = Array.IndexOf(blocks, _blockByStart.GetValueOrDefault(CurrentChangeIndex, -1));
        index = index < 0 ? (direction > 0 ? 0 : blocks.Length - 1) : (index + direction + blocks.Length) % blocks.Length;
        CurrentChangeIndex = _snapshot.Blocks[blocks[index]].Start;
        ChangeNavigationRequested?.Invoke();
    }
    internal List<int> GetChangeBlocks() => _snapshot.Blocks.Select(x => x.Start).ToList();

    [RelayCommand(CanExecute = nameof(CanFind))]
    private void FindNext() => Find(1);
    [RelayCommand(CanExecute = nameof(CanFind))]
    private void FindPrevious() => Find(-1);
    private void Find(int direction)
    {
        int count = View.SearchHits.Length;
        if (count == 0) return;
        CurrentSearchIndex = CurrentSearchIndex < 0 ? (direction > 0 ? 0 : count - 1) : (CurrentSearchIndex + direction + count) % count;
    }

    [RelayCommand(CanExecute = nameof(CanReview))]
    private void ToggleReviewed()
    {
        if (!_blockByStart.TryGetValue(CurrentChangeIndex, out int block)) return;
        if (!_reviewed.Add(block)) _reviewed.Remove(block);
        NotifyNavigation();
        ScheduleView();
    }

    private bool CanSave() => !IsBusy && _filePicker is ITextFileSaveService;
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveLeftAsAsync() => SaveAsAsync(DocumentSide.Left);
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveRightAsAsync() => SaveAsAsync(DocumentSide.Right);
    private async Task SaveAsAsync(DocumentSide side)
    {
        if (_filePicker is not ITextFileSaveService saver) return;
        IsSaving = true;
        try
        {
            string text = side == DocumentSide.Left ? LeftText : RightText;
            long version = side == DocumentSide.Left ? Session.LeftVersion : Session.RightVersion;
            var previous = side == DocumentSide.Left ? _leftFile : _rightFile;
            var file = new TextFileContent(text, previous?.Name ?? (side == DocumentSide.Left ? LeftName : RightName),
                previous?.Path ?? "", previous is null || previous.EncodingName == "Desconocida" ? "utf-8" : previous.EncodingName,
                TextFileContent.DetectLineEndings(text), previous?.HasBom ?? false);
            var saved = await saver.SaveFileAsAsync(file);
            if (saved is null) return;
            if (side == DocumentSide.Left) _leftFile = saved; else _rightFile = saved;
            if (version == (side == DocumentSide.Left ? Session.LeftVersion : Session.RightVersion)) Session.MarkSaved(side);
            foreach (var name in new[] { nameof(LeftName), nameof(RightName), nameof(LeftPath), nameof(RightPath) }) OnPropertyChanged(name);
            HistoryChanged();
            StatsText = "Archivo guardado.";
        }
        catch (Exception ex) { StatsText = $"No se pudo guardar: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportHtmlAsync() => ExportAsync(true);
    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportPatchAsync() => ExportAsync(false);
    private async Task ExportAsync(bool html)
    {
        if (_exporter is null) return;
        IsExporting = true;
        var snapshot = _snapshot;
        var reviewed = new HashSet<int>(_reviewed);
        string left = LeftText, right = RightText, leftName = LeftName, rightName = RightName;
        try
        {
            var content = await Task.Run(() => html ? ComparisonExporter.Html(snapshot, leftName, rightName, reviewed)
                : ComparisonExporter.Patch(left, right, leftName, rightName));
            if (!html && content.Length == 0) { StatsText = "No hay diferencias de contenido para exportar como parche."; return; }
            if (await _exporter.SaveTextAsync(html ? "comparacion.html" : "comparacion.patch", content, html))
                StatsText = html ? "Informe completo exportado (incluye filas ocultas)." : "Parche exacto exportado (independiente de filtros y reglas de espacios).";
        }
        catch (Exception ex) { StatsText = $"No se pudo exportar: {ex.Message}"; }
        finally { IsExporting = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewCancellation?.Cancel();
        _viewCancellation = null;
        _liveCancellation?.Cancel();
        CompareCommand.Cancel();
        Session.DocumentChanged -= DocumentChanged;
        Session.HistoryChanged -= HistoryChanged;
    }
}
