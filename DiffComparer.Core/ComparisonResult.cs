using System.Collections.Immutable;

namespace DiffComparer.Core;

public readonly record struct TextRange(int Offset, int Length)
{
    public int End => checked(Offset + Length);
}
public readonly record struct LineRange(int Start, int Count)
{
    public int End => checked(Start + Count);
}
public sealed record DiffOptions(bool IgnoreWhitespace = false, bool CompareLineEndings = false,
    DiffStrategy Strategy = DiffStrategy.Readable, int SimilarityThreshold = 60, bool IncludeCharacterDetails = true);
public sealed record SourceLine(int Offset, string Content, string Terminator)
{
    public int ContentEnd => Offset + Content.Length;
    public int End => ContentEnd + Terminator.Length;
}

/// <summary>Original text, including line terminators, with explicit document identity and revision.</summary>
public sealed class TextSnapshot
{
    public Guid DocumentId { get; }
    public long Version { get; }
    public string Text { get; }
    public ImmutableArray<SourceLine> Lines { get; }
    public TextSnapshot(string text, Guid documentId = default, long version = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        DocumentId = documentId == Guid.Empty ? Guid.NewGuid() : documentId;
        Version = version;
        var lines = ImmutableArray.CreateBuilder<SourceLine>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            int count = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            lines.Add(new(start, text[start..i], text.Substring(i, count)));
            i += count - 1;
            start = i + 1;
        }
        if (start < text.Length) lines.Add(new(start, text[start..], ""));
        Lines = lines.ToImmutable();
    }
    public int LineOffset(int index) => index == Lines.Length ? Text.Length : Lines[index].Offset;
    public TextRange Range(LineRange lines) => new(LineOffset(lines.Start), LineOffset(lines.End) - LineOffset(lines.Start));
    public string Slice(TextRange range) => Text.Substring(range.Offset, range.Length);
}

public sealed record ExactEdit(TextRange Left, TextRange Right);
public sealed record DiffHunk(int Id, LineRange LeftLines, LineRange RightLines,
    TextRange LeftText, TextRange RightText, int RowStart, int RowCount, ImmutableArray<ExactEdit> InnerChanges);

public sealed class ComparisonResult
{
    public Guid Id { get; } = Guid.NewGuid();
    public TextSnapshot Left { get; }
    public TextSnapshot Right { get; }
    public DiffOptions Options { get; }
    public ImmutableArray<ExactEdit> RawEdits { get; }
    public ImmutableArray<DiffHunk> Hunks { get; }
    public ImmutableArray<LineMap> Lines { get; }
    public bool UsedBandedPairing { get; }
    public ComparisonResult(TextSnapshot left, TextSnapshot right, DiffOptions options,
        IEnumerable<ExactEdit> edits, IEnumerable<DiffHunk> hunks, IEnumerable<LineMap> lines, bool usedBandedPairing)
    {
        Left = left; Right = right; Options = options;
        RawEdits = edits.ToImmutableArray(); Hunks = hunks.ToImmutableArray(); Lines = lines.ToImmutableArray();
        UsedBandedPairing = usedBandedPairing;
    }
    public string ReconstructRight()
    {
        var result = new System.Text.StringBuilder(Right.Text.Length);
        int position = 0;
        foreach (var change in RawEdits)
        {
            result.Append(Left.Text.AsSpan(position, change.Left.Offset - position));
            result.Append(Right.Text.AsSpan(change.Right.Offset, change.Right.Length));
            position = change.Left.End;
        }
        return result.Append(Left.Text.AsSpan(position)).ToString();
    }
}

public enum CopyDirection { LeftToRight, RightToLeft }
public sealed record CopyRequest(Guid SnapshotId, int HunkId, CopyDirection Direction);
public sealed record TextReplacement(Guid DocumentId, long ExpectedVersion, TextRange Range,
    string ExpectedText, string ReplacementText);

public static class CopyPlanner
{
    public static TextReplacement Plan(ComparisonResult result, CopyRequest request,
        Guid leftId, long leftVersion, Guid rightId, long rightVersion, bool preserveDestinationEndings = true)
    {
        if (result.Id != request.SnapshotId || result.Left.DocumentId != leftId || result.Right.DocumentId != rightId
            || result.Left.Version != leftVersion || result.Right.Version != rightVersion)
            throw new InvalidOperationException("El cambio pertenece a una comparación anterior. Recalcula antes de copiar.");
        if ((uint)request.HunkId >= (uint)result.Hunks.Length) throw new ArgumentOutOfRangeException(nameof(request));
        var hunk = result.Hunks[request.HunkId];
        bool toRight = request.Direction == CopyDirection.LeftToRight;
        var source = toRight ? result.Left : result.Right;
        var target = toRight ? result.Right : result.Left;
        var sourceRange = toRight ? hunk.LeftText : hunk.RightText;
        var targetRange = toRight ? hunk.RightText : hunk.LeftText;
        string replacement = source.Slice(sourceRange);
        if (preserveDestinationEndings && !result.Options.CompareLineEndings)
        {
            string? ending = target.Lines.FirstOrDefault(l => l.Terminator.Length > 0)?.Terminator;
            if (ending is not null) replacement = replacement.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", ending);
        }
        return new(target.DocumentId, target.Version, targetRange, target.Slice(targetRange), replacement);
    }
}
