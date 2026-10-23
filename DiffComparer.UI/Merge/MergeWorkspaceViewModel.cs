using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffComparer.Core;
using DiffComparer.Merge;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Models;
using DiffComparer.UI.ViewModels;

namespace DiffComparer.UI.Merge;

public partial class MergeWorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly IFilePickerService _files;
    private readonly ITextFileSaveService _save;
    private CancellationTokenSource? _comparisonCancellation;
    private long _revision;
    private bool _disposed;
    private int _index;
    public TextDocument LeftDocument { get; } = new();
    public TextDocument RightDocument { get; } = new();
    public TextDocument ResultDocument { get; } = new();
    public SectionMergeSession? Session { get; private set; }
    public ComparisonResult? Comparison => IsResultCurrent ? Session?.Comparison : null;
    public Task PendingComparison { get; private set; } = Task.CompletedTask;
    public event Action? PresentationChanged;
    public event Action? SectionNavigationRequested;
    [ObservableProperty] private string _leftName = "Original";
    [ObservableProperty] private string _rightName = "Entrante";
    [ObservableProperty] private string _status = "Pega o abre el original y el entrante. Marca los checks del margen para elegir cada sección.";
    [ObservableProperty] private bool _isResultCurrent;
    [ObservableProperty] private bool _isFinalEditing;
    [ObservableProperty] private bool _isBusy;
    public bool CanEditSources => !IsFinalEditing;
    public bool CanChoose => IsResultCurrent && !IsFinalEditing;
    public bool CanSave => (IsResultCurrent || IsFinalEditing) && !IsBusy;
    public bool CanUndo => CanChoose && Session?.CanUndo == true;
    public bool CanRedo => CanChoose && Session?.CanRedo == true;
    public bool CanNavigate => CanChoose && Session?.Comparison.Hunks.Length > 0;
    public DiffHunk? CurrentSection => CanNavigate ? Session!.Comparison.Hunks[Math.Clamp(_index, 0, Session.Comparison.Hunks.Length - 1)] : null;
    public string SectionTitle => !IsResultCurrent ? "Actualizando diferencias…" : Session!.Comparison.Hunks.Length == 0
        ? "Los dos textos son iguales" : $"Sección {_index + 1} de {Session.Comparison.Hunks.Length}";

    public MergeWorkspaceViewModel(IFilePickerService files, ITextFileSaveService save)
    {
        _files = files; _save = save;
        LeftDocument.TextChanged += SourceChanged;
        RightDocument.TextChanged += SourceChanged;
    }
    partial void OnIsResultCurrentChanged(bool value) => NotifyState();
    partial void OnIsFinalEditingChanged(bool value) => NotifyState();
    partial void OnIsBusyChanged(bool value) => NotifyState();
    private void NotifyState()
    {
        foreach (string name in new[] { nameof(Comparison), nameof(CanEditSources), nameof(CanChoose), nameof(CanSave),
            nameof(CanUndo), nameof(CanRedo), nameof(CanNavigate), nameof(CurrentSection), nameof(SectionTitle) }) OnPropertyChanged(name);
    }
    private void SourceChanged(object? sender, EventArgs e)
    {
        if (_disposed || IsFinalEditing) return;
        _revision++;
        IsResultCurrent = false;
        Status = "Actualizando diferencias…";
        PresentationChanged?.Invoke(); // Remove obsolete controls immediately, before the debounce.
        _comparisonCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _comparisonCancellation = cancellation;
        PendingComparison = RecalculateAsync(_revision, cancellation);
    }
    private async Task RecalculateAsync(long revision, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(250, cancellation.Token);
            string left = LeftDocument.Text, right = RightDocument.Text;
            var comparison = await Task.Run(() => new DiffEngine(cancellation.Token).Analyze(
                new TextSnapshot(left), new TextSnapshot(right), new DiffOptions(CompareLineEndings: true)), cancellation.Token);
            if (_disposed || revision != _revision || cancellation.IsCancellationRequested || IsFinalEditing) return;
            var next = new SectionMergeSession(comparison);
            next.CarryDecisionsFrom(Session);
            Session = next;
            _index = Math.Clamp(_index, 0, Math.Max(0, comparison.Hunks.Length - 1));
            IsResultCurrent = true;
            Refresh();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && revision == _revision) Status = $"No se pudo comparar: {ex.Message}"; }
        finally { cancellation.Dispose(); if (ReferenceEquals(_comparisonCancellation, cancellation)) _comparisonCancellation = null; }
    }
    [RelayCommand]
    private async Task OpenAsync(string side)
    {
        if (!CanEditSources || IsBusy) return;
        IsBusy = true;
        try
        {
            var file = await _files.OpenFileAsync();
            if (file is null || _disposed || !CanEditSources) return;
            if (side == "left") { LeftDocument.Text = file.Text; LeftName = file.Name; }
            if (side == "right") { RightDocument.Text = file.Text; RightName = file.Name; }
        }
        catch (Exception ex) { Status = $"No se pudo abrir: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    [RelayCommand]
    private void Choose(SectionRequest request)
    {
        if (!CanChoose || Session is null || request.SessionId != Session.Id ||
            !Session.Comparison.Hunks.Any(h => h.Id == request.SectionId)) return;
        _index = request.SectionId;
        Session.SetIncluded(request.SectionId, request.Incoming, request.Include);
        Refresh();
    }
    [RelayCommand] private void IncludeBoth()
    {
        if (CurrentSection is { } h) { Session!.Choose(h.Id, MergeChoice.BothLeftFirst); Refresh(); }
    }
    [RelayCommand] private void Undo() { if (CanUndo) { Session!.Undo(); Refresh(); } }
    [RelayCommand] private void Redo() { if (CanRedo) { Session!.Redo(); Refresh(); } }
    [RelayCommand] private void Next() => Navigate(1);
    [RelayCommand] private void Previous() => Navigate(-1);
    private void Navigate(int delta)
    {
        if (!CanNavigate) return;
        int count = Session!.Comparison.Hunks.Length;
        _index = (_index + delta + count) % count;
        NotifyState(); SectionNavigationRequested?.Invoke();
    }
    private void Refresh()
    {
        ResultDocument.Text = Session!.ResultText;
        Status = $"{Session.Comparison.Hunks.Length} secciones diferentes. El resultado conserva el original salvo las secciones que elijas.";
        NotifyState(); PresentationChanged?.Invoke();
    }
    [RelayCommand] private void FinalizeResult()
    {
        if (!CanChoose) return;
        IsFinalEditing = true;
        ResultDocument.UndoStack.ClearAll();
        PresentationChanged?.Invoke();
        Status = "Edición manual del resultado. Ctrl+Z / Ctrl+Y deshacen y rehacen tus ediciones.";
    }
    [RelayCommand] private void EditInputs()
    {
        if (!IsFinalEditing || IsBusy) return;
        // Preserve the manual result in the original document's undo history before resuming live merge.
        IsFinalEditing = false;
        LeftDocument.Text = ResultDocument.Text;
        SourceChanged(this, EventArgs.Empty);
    }
    [RelayCommand] private async Task SaveAsync()
    {
        if (!CanSave) return;
        IsBusy = true;
        string text = ResultDocument.Text;
        try
        {
            var saved = await _save.SaveFileAsAsync(new TextFileContent(text, "merge-result.txt", "", "utf-8", TextFileContent.DetectLineEndings(text)));
            if (saved is not null) Status = ResultDocument.Text == text ? $"Resultado guardado: {saved.Path}" : "Se guardó la versión anterior; hay cambios nuevos sin guardar.";
        }
        catch (Exception ex) { Status = $"No se pudo guardar: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _comparisonCancellation?.Cancel();
        LeftDocument.TextChanged -= SourceChanged; RightDocument.TextChanged -= SourceChanged;
    }
}
