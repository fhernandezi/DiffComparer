using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using DiffComparer.Core;
using DiffComparer.UI.Models;

namespace DiffComparer.UI.Renderers;

public sealed class OriginalLineNumberMargin : Control
{
    private readonly TextView _view;
    private readonly bool _left;
    public bool IsDarkTheme { get; set; } = true;
    private ProjectedRow[] _rows = Array.Empty<ProjectedRow>();
    private bool _comparing;
    private bool _sourceMode;
    private IReadOnlyDictionary<int, ProjectedRow> _sourceRows = new Dictionary<int, ProjectedRow>();
    public void SetSourceRows(IEnumerable<ProjectedRow> rows)
    {
        _sourceMode = true;
        _sourceRows = rows.Where(row => !row.Gap && (_left ? row.Line.LeftLine : row.Line.RightLine).HasValue)
            .ToDictionary(row => (_left ? row.Line.LeftLine : row.Line.RightLine)!.Value);
        _comparing = false;
        InvalidateMeasure();
        InvalidateVisual();
    }
    private int _maxNumber;
    private int _digits = 4;
    public OriginalLineNumberMargin(TextView view, bool left)
    {
        _view = view;
        _left = left;
        _view.VisualLinesChanged += (_, _) =>
        {
            int digits = Math.Max(4, (_comparing ? _maxNumber : _view.Document?.LineCount ?? 1).ToString().Length);
            if (digits != _digits) { _digits = digits; InvalidateMeasure(); }
            InvalidateVisual();
        };
        _view.ScrollOffsetChanged += (_, _) => InvalidateVisual();
    }
    public void SetRows(ProjectedRow[] rows, bool comparing)
    {
        _sourceMode = false;
        _rows = rows;
        _comparing = comparing;
        _maxNumber = rows.Length == 0 ? 0 : rows.Max(row => (_left ? row.Line.LeftLine : row.Line.RightLine) ?? 0) + 1;
        InvalidateMeasure();
        InvalidateVisual();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        int max = _comparing ? _maxNumber : _view.Document?.LineCount ?? 1;
        _digits = Math.Max(4, max.ToString().Length);
        return new Size(_digits * 9 + (_sourceMode ? 42 : 24), 0);
    }
    public override void Render(DrawingContext context)
    {
        if (!_view.VisualLinesValid) return;
        foreach (var visual in _view.VisualLines)
        {
            int index = visual.FirstDocumentLine.LineNumber - 1;
            string label = (index + 1).ToString();
            IBrush brush = DiffPalette.Muted(IsDarkTheme);
            if (_comparing)
            {
                if (index >= _rows.Length) continue;
                var row = _rows[index];
                int? number = _left ? row.Line.LeftLine : row.Line.RightLine;
                label = row.Gap ? "⋯" : number.HasValue ? (row.Reviewed ? "✓ " : "") + (number + 1).ToString() : "";
                if (row.Line.HasTerminatorChange) label += " ¶";
                if (row.Line.MoveId.HasValue) brush = DiffPalette.Selection(IsDarkTheme).Brush!;
                else if (row.Reviewed) brush = DiffPalette.Bracket(LineStatus.Added, IsDarkTheme).Brush!;
            }
            else if (_sourceRows.TryGetValue(index, out var source))
            {
                if (source.Line.Status != LineStatus.Equal)
                {
                    label = (source.Line.Status == LineStatus.Added ? "+ " : source.Line.Status == LineStatus.Deleted ? "− " : "~ ") + label;
                    brush = DiffPalette.Bracket(source.Line.Status, IsDarkTheme).Brush!;
                }
                if (source.Reviewed) { label = "✓ " + label; brush = DiffPalette.Bracket(LineStatus.Added, IsDarkTheme).Brush!; }
                if (source.Line.HasTerminatorChange) { label += " ¶"; brush = DiffPalette.Bracket(LineStatus.Modified, IsDarkTheme).Brush!; }
                if (source.Line.MoveId.HasValue) { label += " ↔"; brush = DiffPalette.Selection(IsDarkTheme).Brush!; }
            }
            var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Consolas"), 13, brush);
            context.DrawText(text, new Point(Math.Max(0, Bounds.Width - text.Width - 8), visual.VisualTop - _view.VerticalOffset));
        }
    }
}

public class DiffLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly bool _isLeftSide;
    private IReadOnlyDictionary<int, LineMap> _lines = new Dictionary<int, LineMap>();
    private int _selectionStart = -1, _selectionEnd = -1;
    public bool IsDarkTheme { get; set; } = true;
    public KnownLayer Layer => KnownLayer.Background;
    public DiffLineBackgroundRenderer(bool isLeftSide) => _isLeftSide = isLeftSide;
    public void SetDiffLines(IReadOnlyList<LineMap> lines, bool sourceLineNumbers = false) =>
        _lines = lines.Select((line, index) => (Line: line, Index: sourceLineNumbers
            ? (_isLeftSide ? line.LeftLine : line.RightLine) : index))
            .Where(x => x.Index.HasValue).ToDictionary(x => x.Index!.Value, x => x.Line);
    public void SetSelection(int start, int end) { _selectionStart = start; _selectionEnd = end; }
    public void Draw(TextView textView, DrawingContext context)
    {
        if (!textView.VisualLinesValid) return;
        Rect? section = null;
        int? hunk = null;
        LineStatus status = LineStatus.Equal;
        bool selected = false, clippedTop = false, clippedBottom = false;
        void Flush()
        {
            if (section is not Rect rect) return;
            context.FillRectangle(DiffPalette.Line(status, IsDarkTheme), rect);
            if (selected)
            {
                // One outline for the whole section; never draw horizontal seams between lines.
                context.DrawLine(DiffPalette.Selection(IsDarkTheme), rect.TopLeft, rect.BottomLeft);
                context.DrawLine(DiffPalette.Selection(IsDarkTheme), rect.TopRight, rect.BottomRight);
                if (!clippedTop) context.DrawLine(DiffPalette.Selection(IsDarkTheme), rect.TopLeft, rect.TopRight);
                if (!clippedBottom) context.DrawLine(DiffPalette.Selection(IsDarkTheme), rect.BottomLeft, rect.BottomRight);
            }
            section = null;
        }
        foreach (var visual in textView.VisualLines)
        {
            if (visual.FirstDocumentLine.IsDeleted) continue;
            int index = visual.FirstDocumentLine.LineNumber - 1;
            if (!_lines.TryGetValue(index, out var line) || line.Status == LineStatus.Equal
                || !(_isLeftSide ? line.LeftLine.HasValue : line.RightLine.HasValue))
            { Flush(); continue; }
            var rect = new Rect(0, visual.VisualTop - textView.VerticalOffset, textView.Bounds.Width, visual.Height);
            bool isSelected = _selectionStart >= 0 && index >= _selectionStart && index <= _selectionEnd;
            if (section is Rect previous && hunk == line.HunkId && status == line.Status && selected == isSelected
                && Math.Abs(previous.Bottom - rect.Top) < 0.01)
                section = new Rect(previous.TopLeft, rect.BottomRight);
            else
            {
                Flush(); section = rect; hunk = line.HunkId; status = line.Status; selected = isSelected;
                clippedTop = index > _selectionStart;
            }
            clippedBottom = visual.LastDocumentLine.LineNumber - 1 < _selectionEnd;
        }
        Flush();
    }
}
