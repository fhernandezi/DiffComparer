using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using DiffComparer.Core;
using DiffComparer.Merge;
using DiffComparer.UI.Renderers;

namespace DiffComparer.UI.Merge;

/// <summary>One checkbox and one bracket per visible section, positioned using real editor geometry.</summary>
public sealed class MergeSectionGutter : Canvas
{
    private MergeWorkspaceViewModel? _model;
    private TextEditor? _editor;
    private bool _incoming, _updating;
    private Guid _sessionId;
    private readonly Decoration _decoration;
    public MergeSectionGutter()
    {
        _decoration = new Decoration(this) { IsHitTestVisible = false };
        Children.Add(_decoration);
    }
    private void ClearChecks()
    {
        foreach (var check in _checks.Values) Children.Remove(check);
        _checks.Clear();
    }
    private readonly Dictionary<int, CheckBox> _checks = new();
    private readonly List<(double Top, double Bottom)> _brackets = new();
    public void Connect(MergeWorkspaceViewModel model, TextEditor editor, bool incoming)
    {
        _model = model; _editor = editor; _incoming = incoming;
        ClipToBounds = true;
        editor.TextArea.TextView.VisualLinesChanged += (_, _) => Refresh();
        editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => Refresh();
        LayoutUpdated += (_, _) => Refresh();
    }
    public void Refresh()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            _brackets.Clear();
            _decoration.Width = Bounds.Width; _decoration.Height = Bounds.Height;
            var session = _model?.Session;
            var comparison = _model?.Comparison;
            if (comparison is null || session is null || _model?.CanChoose != true || _editor is null)
            { ClearChecks(); return; }
            if (_sessionId != session.Id) { ClearChecks(); _sessionId = session.Id; }
            var view = _editor.TextArea.TextView;
            if (!view.VisualLinesValid || view.TranslatePoint(default, this) is not Point origin) return;
            var visible = new HashSet<int>();
            foreach (var h in comparison.Hunks)
            {
                var range = _incoming ? h.RightLines : h.LeftLines;
                double top, bottom;
                if (range.Count == 0)
                {
                    int line = Math.Min(range.Start + 1, _editor.Document.LineCount);
                    var visual = view.VisualLines.FirstOrDefault(v => v.FirstDocumentLine.LineNumber <= line && v.LastDocumentLine.LineNumber >= line);
                    if (visual is null) continue;
                    top = bottom = origin.Y + visual.VisualTop - view.VerticalOffset;
                }
                else
                {
                    var lines = view.VisualLines.Where(v => v.LastDocumentLine.LineNumber > range.Start && v.FirstDocumentLine.LineNumber <= range.End).ToArray();
                    if (lines.Length == 0) continue;
                    top = origin.Y + lines[0].VisualTop - view.VerticalOffset;
                    bottom = origin.Y + lines[^1].VisualTop + lines[^1].Height - view.VerticalOffset;
                }
                if (bottom < 0 || top > Bounds.Height) continue;
                top = Math.Clamp(top, 0, Bounds.Height); bottom = Math.Clamp(bottom, 0, Bounds.Height);
                _brackets.Add((top, bottom)); visible.Add(h.Id);
                if (!_checks.TryGetValue(h.Id, out var check))
                {
                    int id = h.Id;
                    Guid snapshotId = session.Id;
                    check = new CheckBox { Width = 28, Height = 24, MinHeight = 24, Padding = new Thickness(0), Tag = id };
                    check.Classes.Add("mergeChoice");
                    ToolTip.SetTip(check, $"Usar {(_incoming ? "entrante" : "original")} · sección {id + 1}\n"
                        + (range.Count == 0 ? "Esta versión elimina la sección." : $"Líneas {range.Start + 1}–{range.End}")
                        + "\nCada check es independiente. Ambos marcados incluyen original y luego entrante."
                        + "\nDesmarcar quita únicamente este lado del resultado.");
                    AutomationProperties.SetName(check, $"Usar {(_incoming ? "entrante" : "original")} sección {id + 1}");
                    check.PropertyChanged += (_, e) =>
                    {
                        if (!_updating && e.Property == ToggleButton.IsCheckedProperty)
                            _model.ChooseCommand.Execute(new SectionRequest(snapshotId, id, _incoming, check.IsChecked == true));
                    };
                    _checks.Add(id, check); Children.Add(check);
                }
                var choice = session.ChoiceFor(h.Id);
                check.IsChecked = _incoming ? choice is MergeChoice.Right or MergeChoice.BothLeftFirst or MergeChoice.BothRightFirst
                    : choice is MergeChoice.Left or MergeChoice.BothLeftFirst or MergeChoice.BothRightFirst;
                SetLeft(check, 0);
                SetTop(check, Math.Clamp((top + bottom) / 2 - 12, 0, Math.Max(0, Bounds.Height - 24)));
            }
            foreach (int id in _checks.Keys.ToArray())
                if (!visible.Contains(id)) { Children.Remove(_checks[id]); _checks.Remove(id); }
        }
        finally { _updating = false; _decoration.InvalidateVisual(); }
    }
    private void DrawBrackets(DrawingContext context)
    {
        var pen = DiffPalette.Bracket(LineStatus.Modified, ActualThemeVariant == ThemeVariant.Dark);
        foreach (var (top, bottom) in _brackets)
        {
            context.DrawLine(pen, new(30, top), new(30, bottom));
            context.DrawLine(pen, new(30, top), new(36, top));
            context.DrawLine(pen, new(30, bottom), new(36, bottom));
        }
    }
    private sealed class Decoration(MergeSectionGutter owner) : Control
    {
        public override void Render(DrawingContext context) => owner.DrawBrackets(context);
    }
}
