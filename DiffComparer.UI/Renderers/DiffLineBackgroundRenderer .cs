using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using DiffComparer.Core;
using System.Collections.Generic;
using System.Linq;

namespace DiffComparer.UI.Renderers;

public class DiffLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly bool _isLeftSide;
    private List<LineMap> _lines = new();

    public KnownLayer Layer => KnownLayer.Background;

    public DiffLineBackgroundRenderer(bool isLeftSide)
    {
        _isLeftSide = isLeftSide;
    }

    public void SetDiffLines(IEnumerable<LineMap> lines)
    {
        _lines = lines.ToList();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lines.Count == 0)
            return;

        if (!textView.VisualLinesValid)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            int documentLineNumber = visualLine.FirstDocumentLine.LineNumber;

            var diffLine = _lines.FirstOrDefault(x =>
       _isLeftSide
           ? x.LeftLine == documentLineNumber - 1
           : x.RightLine == documentLineNumber - 1);

            if (diffLine is null)
                continue;

            var brush = GetBrush(diffLine.Status);

            if (brush is null)
                continue;

            var rect = new Rect(
                0,
                visualLine.VisualTop - textView.VerticalOffset,
                textView.Bounds.Width,
                visualLine.Height);

            drawingContext.FillRectangle(brush, rect);
        }
    }

    private static IBrush? GetBrush(LineStatus status)
    {
        return status switch
        {
            LineStatus.Added => new SolidColorBrush(Color.FromArgb(70, 40, 120, 60)),
            LineStatus.Deleted => new SolidColorBrush(Color.FromArgb(70, 140, 50, 50)),
            LineStatus.Modified => new SolidColorBrush(Color.FromArgb(70, 130, 120, 40)),
            _ => null
        };
    }
}