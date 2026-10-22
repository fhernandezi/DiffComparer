using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DiffComparer.Core;
using DiffComparer.UI.Models;

namespace DiffComparer.UI.Renderers;

public sealed class DiffWordColorizer : DocumentColorizingTransformer
{
    private readonly bool _isLeftSide;
    private IReadOnlySet<int>? _focusLines;
    public bool IsDarkTheme { get; set; } = true;
    public void SetFocusLines(IReadOnlySet<int>? lines) => _focusLines = lines;
    private IReadOnlyDictionary<int, LineMap> _diffLines = new Dictionary<int, LineMap>();

    private IReadOnlyDictionary<int, SearchHit[]> _hits = new Dictionary<int, SearchHit[]>();

    public void SetSearchHits(IReadOnlyDictionary<int, SearchHit[]> hits) => _hits = hits;

    public DiffWordColorizer(bool isLeftSide)
    {
        _isLeftSide = isLeftSide;
    }

    public void SetDiffLines(IReadOnlyList<LineMap> diffLines, bool sourceLineNumbers = false)
    {
        _diffLines = diffLines.Select((line, index) => (Line: line, Index: sourceLineNumbers
            ? (_isLeftSide ? line.LeftLine : line.RightLine) : index))
            .Where(x => x.Index.HasValue).ToDictionary(x => x.Index!.Value, x => x.Line);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        int editorLineIndex = line.LineNumber - 1;
        if (_focusLines is not null && !_focusLines.Contains(editorLineIndex))
        {
            if (line.Length > 0) ChangeLinePart(line.Offset, line.EndOffset,
                element => element.TextRunProperties.SetForegroundBrush(DiffPalette.Muted(IsDarkTheme)));
            return;
        }

        if (!_diffLines.TryGetValue(editorLineIndex, out var diffLine)) return;

        var segments = _isLeftSide
            ? diffLine.LeftSegments
            : diffLine.RightSegments;


        int currentOffset = line.Offset;
        int lineEndOffset = line.EndOffset;

        foreach (var segment in segments)
        {
            int segmentLength = segment.Text?.Length ?? 0;

            if (segmentLength <= 0)
                continue;

            int startOffset = currentOffset;
            int endOffset = Math.Min(currentOffset + segmentLength, lineEndOffset);

            if (diffLine.Status != LineStatus.Equal && segment.IsChanged && startOffset < endOffset)
            {
                ChangeLinePart(
                    startOffset,
                    endOffset,
                    element =>
                    {
                        element.TextRunProperties.SetBackgroundBrush(DiffPalette.Fragment(diffLine.Status, IsDarkTheme));
                        element.TextRunProperties.SetForegroundBrush(DiffPalette.Foreground(IsDarkTheme));
                    });
            }

            currentOffset += segmentLength;

            if (currentOffset >= lineEndOffset)
                break;
        }
        if (_hits.TryGetValue(editorLineIndex, out var hits))
            foreach (var hit in hits)
            {
                int start = line.Offset + hit.Column;
                int end = Math.Min(line.EndOffset, start + hit.Length);
                if (start < end) ChangeLinePart(start, end, element =>
                {
                    element.TextRunProperties.SetBackgroundBrush(DiffPalette.Search(IsDarkTheme));
                    element.TextRunProperties.SetForegroundBrush(DiffPalette.Foreground(IsDarkTheme));
                });
            }
    }
}
