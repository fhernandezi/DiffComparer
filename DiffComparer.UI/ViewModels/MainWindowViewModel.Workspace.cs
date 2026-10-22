using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Models;

namespace DiffComparer.UI.ViewModels;

// Presentation actions reuse the existing editable documents and comparison scheduling.
public partial class MainWindowViewModel
{
    [ObservableProperty] private string _selectedAppearance = "Oscuro";
    [ObservableProperty] private string _workspaceNotice = "";
    public string[] AppearanceModes { get; } = { "Sistema", "Claro", "Oscuro" };
    public IClipboardService? ClipboardService { get; set; }
    private WorkspaceState? _workspaceUndo;
    public bool IsLeftEmpty => LeftDocument.TextLength == 0;
    public bool IsRightEmpty => RightDocument.TextLength == 0;
    public bool CanRestoreWorkspace => _workspaceUndo is not null;
    public bool HasWorkspaceNotice => WorkspaceNotice.Length > 0;
    public bool IsWorking => IsBusy || IsUpdatingView || _liveCancellation is not null;
    public string WorkspaceStatus => IsLoading ? "Abriendo el archivo…" : IsSaving ? "Guardando tu texto…"
        : IsExporting ? "Preparando el archivo para compartir…" : IsComparing ? "Buscando diferencias… Puedes seguir escribiendo."
        : IsUpdatingView ? "Actualizando lo que ves…" : _liveCancellation is not null ? "Actualizando la comparación…" : HasStatus ? StatsText
        : CanonicalResult is null ? "Listo para comparar. Escribe, pega o abre dos textos."
        : DiscrepancyCount == 0 ? "Los textos son iguales con las opciones elegidas."
        : $"{(DiscrepancyCount == 1 ? "1 línea distinta" : $"{DiscrepancyCount} líneas distintas")} · {(ChangeBlockCount == 1 ? "1 sección" : $"{ChangeBlockCount} secciones")}";

    partial void OnWorkspaceNoticeChanged(string value) => OnPropertyChanged(nameof(HasWorkspaceNotice));

    private void NotifyWorkspaceState()
    {
        foreach (string name in new[] { nameof(IsLeftEmpty), nameof(IsRightEmpty), nameof(CanRestoreWorkspace), nameof(IsWorking), nameof(WorkspaceStatus) })
            OnPropertyChanged(name);
        ClearLeftCommand.NotifyCanExecuteChanged(); ClearRightCommand.NotifyCanExecuteChanged();
        SwapTextsCommand.NotifyCanExecuteChanged(); ResetWorkspaceCommand.NotifyCanExecuteChanged();
        RestoreWorkspaceCommand.NotifyCanExecuteChanged();
        CopyLeftTextCommand.NotifyCanExecuteChanged(); CopyRightTextCommand.NotifyCanExecuteChanged();
    }

    private sealed record WorkspaceState(string Left, string Right, TextFileContent? LeftFile, TextFileContent? RightFile,
        bool IgnoreWhitespace, bool Endings, bool PreserveEndings, string Strategy, ViewOptions View,
        bool Whitespace, string Language, string Encoding);
    private WorkspaceState CaptureWorkspace() => new(LeftText, RightText, _leftFile, _rightFile,
        IgnoreWhitespace, CompareLineEndings, PreserveDestinationEndings, SelectedStrategy, Options(),
        ShowWhitespace, SelectedLanguage, SelectedEncoding);
    private void RememberWorkspace() { _workspaceUndo = CaptureWorkspace(); NotifyWorkspaceState(); }
    private bool CanClearLeft() => !IsBusy && !IsLeftEmpty;
    private bool CanClearRight() => !IsBusy && !IsRightEmpty;
    private bool CanSwapTexts() => !IsBusy && (!IsLeftEmpty || !IsRightEmpty);
    private bool CanResetWorkspace() => !IsBusy && (!IsLeftEmpty || !IsRightEmpty || _leftFile is not null || _rightFile is not null
        || IgnoreWhitespace || CompareLineEndings || !PreserveDestinationEndings || SelectedStrategy != "Legible"
        || HasViewFilter || ContextLines != 3 || SearchText.Length > 0 || ShowWhitespace
        || SelectedLanguage != "Automático" || SelectedEncoding != "Automático");
    partial void OnSelectedLanguageChanged(string value) => NotifyWorkspaceState();
    partial void OnSelectedEncodingChanged(string value) => NotifyWorkspaceState();
    partial void OnShowWhitespaceChanged(bool value) => NotifyWorkspaceState();
    partial void OnPreserveDestinationEndingsChanged(bool value) => NotifyWorkspaceState();
    private bool CanRestore() => !IsBusy && CanRestoreWorkspace;

    // A full-document Replace is one native undo operation; it preserves the document instance.
    private static void ReplaceText(AvaloniaEdit.Document.TextDocument document, string text)
    {
        if (document.Text == text) return;
        using (document.RunUpdate()) document.Replace(0, document.TextLength, text);
    }
    [RelayCommand(CanExecute = nameof(CanClearLeft))]
    private void ClearLeft()
    {
        RememberWorkspace(); ReplaceText(LeftDocument, ""); WorkspaceNotice = "Texto izquierdo limpiado. Puedes recuperarlo.";
    }
    [RelayCommand(CanExecute = nameof(CanClearRight))]
    private void ClearRight()
    {
        RememberWorkspace(); ReplaceText(RightDocument, ""); WorkspaceNotice = "Texto derecho limpiado. Puedes recuperarlo.";
    }
    [RelayCommand(CanExecute = nameof(CanSwapTexts))]
    private void SwapTexts()
    {
        RememberWorkspace(); string left = LeftText, right = RightText;
        ReplaceText(LeftDocument, right); ReplaceText(RightDocument, left);
        WorkspaceNotice = "Textos intercambiados. La izquierda es ahora el original.";
    }
    [RelayCommand(CanExecute = nameof(CanResetWorkspace))]
    private void ResetWorkspace()
    {
        if (!CanResetWorkspace()) return;
        RememberWorkspace();
        ReplaceText(LeftDocument, ""); ReplaceText(RightDocument, "");
        _leftFile = _rightFile = null;
        IgnoreWhitespace = CompareLineEndings = false; PreserveDestinationEndings = true; SelectedStrategy = "Legible";
        OnlyDifferences = MovedOnly = UnreviewedOnly = SearchOnly = ShowWhitespace = false;
        ShowAdded = ShowDeleted = ShowModified = true; ContextLines = 3; SearchText = "";
        SelectedLanguage = SelectedEncoding = "Automático";
        _reviewed.Clear();
        NotifyFileLabels();
        ScheduleComparison();
        WorkspaceNotice = "Todo listo para empezar de nuevo. Puedes recuperar tu trabajo anterior.";
    }
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void RestoreWorkspace()
    {
        if (_workspaceUndo is not { } state) return;
        _workspaceUndo = null;
        ReplaceText(LeftDocument, state.Left); ReplaceText(RightDocument, state.Right);
        _leftFile = state.LeftFile; _rightFile = state.RightFile;
        IgnoreWhitespace = state.IgnoreWhitespace; CompareLineEndings = state.Endings;
        PreserveDestinationEndings = state.PreserveEndings; SelectedStrategy = state.Strategy;
        OnlyDifferences = state.View.OnlyDifferences; ContextLines = state.View.Context;
        ShowAdded = state.View.Added; ShowDeleted = state.View.Deleted; ShowModified = state.View.Modified;
        MovedOnly = state.View.MovedOnly; UnreviewedOnly = state.View.UnreviewedOnly;
        SearchText = state.View.Search; SearchOnly = state.View.SearchOnly;
        ShowWhitespace = state.Whitespace; SelectedLanguage = state.Language; SelectedEncoding = state.Encoding;
        NotifyFileLabels(); ScheduleComparison(); NotifyWorkspaceState();
        WorkspaceNotice = "Recuperaste los textos y las opciones anteriores.";
    }
    private void NotifyFileLabels()
    {
        foreach (string name in new[] { nameof(LeftName), nameof(RightName), nameof(LeftPath), nameof(RightPath), nameof(LeftHeader), nameof(RightHeader) })
            OnPropertyChanged(name);
    }
    private bool CanCopyLeftText() => !IsLeftEmpty;
    private bool CanCopyRightText() => !IsRightEmpty;
    [RelayCommand(CanExecute = nameof(CanCopyLeftText))]
    private Task CopyLeftTextAsync() => CopyTextAsync(LeftText, "izquierdo");
    [RelayCommand(CanExecute = nameof(CanCopyRightText))]
    private Task CopyRightTextAsync() => CopyTextAsync(RightText, "derecho");
    private async Task CopyTextAsync(string text, string side)
    {
        try
        {
            if (ClipboardService is null) { WorkspaceNotice = "El portapapeles no está disponible. Puedes seleccionar el texto y usar Ctrl + C."; return; }
            await ClipboardService.SetTextAsync(text);
            WorkspaceNotice = $"Texto {side} copiado. Ya puedes pegarlo donde quieras.";
        }
        catch (Exception) { WorkspaceNotice = "No se pudo copiar. Intenta otra vez o selecciona el texto y usa Ctrl + C."; }
    }
}
