using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DiffComparer.Core;

namespace DiffComparer.UI.Renderers;

public sealed class DiffWordColorizer : DocumentColorizingTransformer
{
    private readonly bool _isLeftSide;
    private IReadOnlyList<LineMap> _diffLines = Array.Empty<LineMap>();

    private static readonly IBrush ChangedBackground =
        new SolidColorBrush(Color.FromArgb(130, 255, 180, 80));

    private static readonly IBrush ChangedForeground =
        Brushes.White;

    public DiffWordColorizer(bool isLeftSide)
    {
        _isLeftSide = isLeftSide;
    }

    public void SetDiffLines(IReadOnlyList<LineMap> diffLines)
    {
        _diffLines = diffLines;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_diffLines.Count == 0)
            return;

        int editorLineIndex = line.LineNumber - 1;

        var diffLine = _isLeftSide
            ? _diffLines.FirstOrDefault(x => x.LeftLine == editorLineIndex)
            : _diffLines.FirstOrDefault(x => x.RightLine == editorLineIndex);

        if (diffLine is null)
            return;

        if (diffLine.Status != LineStatus.Modified)
            return;

        var segments = _isLeftSide
            ? diffLine.LeftSegments
            : diffLine.RightSegments;

        if (segments.Count == 0)
            return;

        int currentOffset = line.Offset;
        int lineEndOffset = line.EndOffset;

        foreach (var segment in segments)
        {
            int segmentLength = segment.Text?.Length ?? 0;

            if (segmentLength <= 0)
                continue;

            int startOffset = currentOffset;
            int endOffset = Math.Min(currentOffset + segmentLength, lineEndOffset);

            if (segment.IsChanged && startOffset < endOffset)
            {
                ChangeLinePart(
                    startOffset,
                    endOffset,
                    element =>
                    {
                        element.TextRunProperties.SetBackgroundBrush(ChangedBackground);
                        element.TextRunProperties.SetForegroundBrush(ChangedForeground);
                    });
            }

            currentOffset += segmentLength;

            if (currentOffset >= lineEndOffset)
                break;
        }
    }
}