using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using DiffComparer.Core;

namespace DiffComparer.UI.Services;

public enum DocumentSide { Left, Right }
public sealed record DocumentCapture(Guid Id, long Version, ITextSource Source)
{
    public TextSnapshot Materialize() => new(Source.Text, Id, Version);
}

/// <summary>Owns editable documents independently of filtered/read-only projections.
/// Capture and edits run on the document owner thread; immutable captures can be read by workers.</summary>
public sealed class ComparisonSession
{
    public TextDocument Left { get; } = new();
    public TextDocument Right { get; } = new();
    public Guid LeftId { get; } = Guid.NewGuid();
    public Guid RightId { get; } = Guid.NewGuid();
    public long LeftVersion { get; private set; }
    public long RightVersion { get; private set; }
    private readonly Stack<DocumentSide> _undo = new();
    private readonly Stack<DocumentSide> _redo = new();
    private bool _applying;
    public event Action<DocumentSide>? DocumentChanged;
    public event Action? HistoryChanged;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public ComparisonSession()
    {
        Left.TextChanged += (_, _) => Changed(DocumentSide.Left);
        Right.TextChanged += (_, _) => Changed(DocumentSide.Right);
        Left.UndoStack.PropertyChanged += (_, _) => HistoryChanged?.Invoke();
        Right.UndoStack.PropertyChanged += (_, _) => HistoryChanged?.Invoke();
    }
    private void Changed(DocumentSide side)
    {
        if (side == DocumentSide.Left) LeftVersion++; else RightVersion++;
        if (!_applying) { _undo.Clear(); _redo.Clear(); }
        DocumentChanged?.Invoke(side);
        HistoryChanged?.Invoke();
    }
    public (DocumentCapture Left, DocumentCapture Right) Capture() =>
        (new(LeftId, LeftVersion, Left.CreateSnapshot()), new(RightId, RightVersion, Right.CreateSnapshot()));
    public bool Matches(ComparisonResult result) => result.Left.DocumentId == LeftId && result.Right.DocumentId == RightId
        && result.Left.Version == LeftVersion && result.Right.Version == RightVersion;

    public bool Apply(ComparisonResult result, CopyRequest request, bool preserveDestinationEndings)
    {
        var edit = CopyPlanner.Plan(result, request, LeftId, LeftVersion, RightId, RightVersion, preserveDestinationEndings);
        var side = request.Direction == CopyDirection.LeftToRight ? DocumentSide.Right : DocumentSide.Left;
        var document = side == DocumentSide.Left ? Left : Right;
        if (document.GetText(edit.Range.Offset, edit.Range.Length) != edit.ExpectedText)
            throw new InvalidOperationException("El contenido destino cambió. Recalcula antes de copiar.");
        if (edit.ExpectedText == edit.ReplacementText) return false;
        _applying = true;
        try { using (document.RunUpdate()) document.Replace(edit.Range.Offset, edit.Range.Length, edit.ReplacementText); }
        finally { _applying = false; }
        _undo.Push(side); _redo.Clear();
        HistoryChanged?.Invoke();
        return true;
    }
    public void Undo()
    {
        if (!_undo.TryPop(out var side)) return;
        _applying = true;
        try { (side == DocumentSide.Left ? Left : Right).UndoStack.Undo(); }
        finally { _applying = false; }
        _redo.Push(side);
        HistoryChanged?.Invoke();
    }
    public void Redo()
    {
        if (!_redo.TryPop(out var side)) return;
        _applying = true;
        try { (side == DocumentSide.Left ? Left : Right).UndoStack.Redo(); }
        finally { _applying = false; }
        _undo.Push(side);
        HistoryChanged?.Invoke();
    }
    public void MarkSaved(DocumentSide side) => (side == DocumentSide.Left ? Left : Right).UndoStack.MarkAsOriginalFile();
    public void Load(DocumentSide side, string text)
    {
        var document = side == DocumentSide.Left ? Left : Right;
        document.Text = text;
        document.UndoStack.ClearAll();
        document.UndoStack.MarkAsOriginalFile();
        _undo.Clear(); _redo.Clear();
        HistoryChanged?.Invoke();
    }
}
