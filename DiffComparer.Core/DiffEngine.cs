using System.Collections.Immutable;

namespace DiffComparer.Core;

/// <summary>Layered diff: exact sequence alignment, monotone display pairing and grapheme refinement.</summary>
public sealed class DiffEngine
{
    private readonly CancellationToken _cancellationToken;
    public DiffEngine(CancellationToken cancellationToken = default) => _cancellationToken = cancellationToken;
    // Preserve the legacy array API's whitespace behavior. The document API defaults to exact content.
    public bool IgnoreWhitespace { get; set; } = true;
    public bool IncludeCharacterDetails { get; set; } = true;
    public int SimilarityThreshold { get; set; } = 60;
    public DiffStrategy Strategy { get; set; } = DiffStrategy.Readable;
    public bool UsedCoarseAlignment => false;

    public List<LineMap> Compare(string[] left, string[] right)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
        _cancellationToken.ThrowIfCancellationRequested();
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] Intern(string[] lines) => lines.Select(text =>
        {
            string key = IgnoreWhitespace ? TextDiffer.Normalize(text) : text;
            if (!keys.TryGetValue(key, out int id)) keys[key] = id = keys.Count;
            return id;
        }).ToArray();
        var a = Intern(left); var b = Intern(right);
        var result = new List<LineMap>();
        int x = 0, y = 0;
        foreach (var edit in SequenceDiffer.Compute(a, b, Strategy, _cancellationToken))
        {
            while (x < edit.LeftStart && y < edit.RightStart)
            {
                result.Add(Equal(x, y, left[x], right[y])); x++; y++;
            }
            var pairs = LinePairer.Align(left[edit.LeftStart..edit.LeftEnd], right[edit.RightStart..edit.RightEnd],
                SimilarityThreshold, _cancellationToken, out _);
            foreach (var pair in pairs)
            {
                int? li = pair.Left + edit.LeftStart, ri = pair.Right + edit.RightStart;
                string l = li.HasValue ? left[li.Value] : "", r = ri.HasValue ? right[ri.Value] : "";
                var segments = IncludeCharacterDetails ? WordDiff(l, r, _cancellationToken) : (new List<WordSegment> { new(l, true) }, new List<WordSegment> { new(r, true) });
                result.Add(new(li, ri, l, r, li.HasValue ? ri.HasValue ? LineStatus.Modified : LineStatus.Deleted : LineStatus.Added, 0)
                { LeftSegments = segments.Item1, RightSegments = segments.Item2 });
            }
            x = edit.LeftEnd; y = edit.RightEnd;
        }
        while (x < left.Length && y < right.Length) { result.Add(Equal(x, y, left[x], right[y])); x++; y++; }
        return result;
    }

    public ComparisonResult Analyze(TextSnapshot left, TextSnapshot right, DiffOptions? options = null)
    {
        options ??= new DiffOptions();
        if (options.SimilarityThreshold is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(options));
        _cancellationToken.ThrowIfCancellationRequested();
        var keys = new Dictionary<(string, string), int>();
        int[] Intern(TextSnapshot text) => text.Lines.Select(line =>
        {
            _cancellationToken.ThrowIfCancellationRequested();
            string content = options.IgnoreWhitespace ? TextDiffer.Normalize(line.Content) : line.Content;
            string ending = options.CompareLineEndings ? line.Terminator : "";
            var key = (content, ending);
            if (!keys.TryGetValue(key, out int id)) keys[key] = id = keys.Count;
            return id;
        }).ToArray();
        var a = Intern(left); var b = Intern(right);
        var rows = new List<LineMap>();
        var hunks = new List<DiffHunk>();
        var exact = new List<ExactEdit>();
        bool banded = false;
        int x = 0, y = 0;
        var sequenceEdits = SequenceDiffer.Compute(a, b, options.Strategy, _cancellationToken);
        var regions = sequenceEdits.Select(edit =>
        {
            var lr = left.Range(new(edit.LeftStart, edit.LeftLength));
            var rr = right.Range(new(edit.RightStart, edit.RightLength));
            // Inserting/deleting at EOF may also own the preceding separator. This keeps
            // the unchanged preceding line out of the display hunk without losing the newline.
            if (edit.LeftLength == 0 && edit.LeftStart == left.Lines.Length && edit.LeftStart > 0
                && edit.RightStart > 0 && left.Lines[^1].Terminator.Length == 0)
            {
                int prefix = right.Lines[edit.RightStart - 1].Terminator.Length;
                rr = new(rr.Offset - prefix, rr.Length + prefix);
            }
            if (edit.RightLength == 0 && edit.RightStart == right.Lines.Length && edit.RightStart > 0
                && edit.LeftStart > 0 && right.Lines[^1].Terminator.Length == 0)
            {
                int prefix = left.Lines[edit.LeftStart - 1].Terminator.Length;
                lr = new(lr.Offset - prefix, lr.Length + prefix);
            }
            return (Edit: edit, Left: lr, Right: rr);
        }).ToArray();
        bool OwnedByRegion(ExactEdit change)
        {
            int low = 0, high = regions.Length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (regions[middle].Left.Offset <= change.Left.Offset) low = middle + 1; else high = middle;
            }
            for (int i = Math.Max(0, low - 1); i <= Math.Min(regions.Length - 1, low); i++)
                if (regions[i].Left.Offset <= change.Left.Offset && regions[i].Left.End >= change.Left.End
                    && regions[i].Right.Offset <= change.Right.Offset && regions[i].Right.End >= change.Right.End) return true;
            return false;
        }
        void AddEqual()
        {
            var l = left.Lines[x]; var r = right.Lines[y];
            var contentChanges = l.Content == r.Content ? Array.Empty<ExactEdit>()
                : Refine(left, new(l.Offset, l.Content.Length), right, new(r.Offset, r.Content.Length)).ToArray();
            exact.AddRange(contentChanges);
            ExactEdit? ending = l.Terminator == r.Terminator ? null : new(new(l.ContentEnd, l.Terminator.Length), new(r.ContentEnd, r.Terminator.Length));
            if (ending is not null && OwnedByRegion(ending)) ending = null;
            if (ending is not null) exact.Add(ending);
            bool significantEnding = ending is not null && (options.CompareLineEndings || (l.Terminator.Length == 0) != (r.Terminator.Length == 0));
            if (significantEnding)
            {
                var changes = contentChanges.Append(ending!).ToImmutableArray();
                int id = hunks.Count;
                rows.Add(new(x, y, l.Content, r.Content, LineStatus.Modified, 100)
                {
                    HunkId = id, HasTerminatorChange = true,
                    LeftSegments = TextDiffer.Segments(l.Content, l.Offset, contentChanges.Select(c => c.Left).ToArray()),
                    RightSegments = TextDiffer.Segments(r.Content, r.Offset, contentChanges.Select(c => c.Right).ToArray())
                });
                hunks.Add(new(id, new(x, 1), new(y, 1), new(l.Offset, l.End - l.Offset), new(r.Offset, r.End - r.Offset), rows.Count - 1, 1, changes));
            }
            else rows.Add(Equal(x, y, l.Content, r.Content));
            x++; y++;
        }
        foreach (var region in regions)
        {
            var edit = region.Edit;
            while (x < edit.LeftStart && y < edit.RightStart) AddEqual();
            var leftLines = new LineRange(edit.LeftStart, edit.LeftLength);
            var rightLines = new LineRange(edit.RightStart, edit.RightLength);
            TextRange lr = region.Left, rr = region.Right;
            var changes = Refine(left, lr, right, rr).ToImmutableArray();
            exact.AddRange(changes);
            int firstRow = rows.Count, id = hunks.Count;
            var pairs = LinePairer.Align(left.Lines.Skip(edit.LeftStart).Take(edit.LeftLength).Select(l => l.Content).ToArray(),
                right.Lines.Skip(edit.RightStart).Take(edit.RightLength).Select(l => l.Content).ToArray(),
                options.SimilarityThreshold, _cancellationToken, out bool usedBand);
            banded |= usedBand;
            var leftChanges = changes.Select(c => c.Left).ToArray(); var rightChanges = changes.Select(c => c.Right).ToArray();
            foreach (var pair in pairs)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int? li = pair.Left + edit.LeftStart, ri = pair.Right + edit.RightStart;
                var l = li.HasValue ? left.Lines[li.Value] : null;
                var r = ri.HasValue ? right.Lines[ri.Value] : null;
                bool endingChanged = l is not null && r is not null && l.Terminator != r.Terminator
                    && (options.CompareLineEndings || (l.Terminator.Length == 0) != (r.Terminator.Length == 0));
                rows.Add(new(li, ri, l?.Content ?? "", r?.Content ?? "",
                    li.HasValue ? ri.HasValue ? LineStatus.Modified : LineStatus.Deleted : LineStatus.Added, 0)
                {
                    HunkId = id, HasTerminatorChange = endingChanged,
                    LeftSegments = l is null ? new() : TextDiffer.Segments(l.Content, l.Offset, leftChanges),
                    RightSegments = r is null ? new() : TextDiffer.Segments(r.Content, r.Offset, rightChanges)
                });
            }
            hunks.Add(new(id, leftLines, rightLines, lr, rr, firstRow, rows.Count - firstRow, changes));
            x = edit.LeftEnd; y = edit.RightEnd;
        }
        while (x < left.Lines.Length && y < right.Lines.Length) AddEqual();
        var moved = MovedBlockDetector.Detect(rows, _cancellationToken);
        return new(left, right, options, exact.OrderBy(e => e.Left.Offset).ThenBy(e => e.Right.Offset), hunks, moved, banded);
    }

    private IReadOnlyList<ExactEdit> Refine(TextSnapshot left, TextRange lr, TextSnapshot right, TextRange rr) =>
        TextDiffer.Compute(left.Slice(lr), right.Slice(rr), _cancellationToken).Select(change =>
            new ExactEdit(new(change.Left.Offset + lr.Offset, change.Left.Length), new(change.Right.Offset + rr.Offset, change.Right.Length))).ToArray();

    private static LineMap Equal(int left, int right, string l, string r) => new(left, right, l, r, LineStatus.Equal, 100)
    { LeftSegments = new() { new(l, false) }, RightSegments = new() { new(r, false) } };

    public static int Similarity(string left, string right) => Similarity(left, right, CancellationToken.None);
    internal static int Similarity(string left, string right, CancellationToken ct)
    {
        left = TextDiffer.Normalize(left); right = TextDiffer.Normalize(right);
        if (left == right) return 100;
        int sum = left.Length + right.Length;
        if (left.Length == 0 || right.Length == 0) return 0;
        int changed = TextDiffer.Compute(left, right, ct).Sum(c => c.Left.Length + c.Right.Length);
        return (int)((long)(sum - changed) * 100 / sum);
    }
    public static (List<WordSegment> Left, List<WordSegment> Right) WordDiff(string left, string right, CancellationToken cancellationToken = default)
    {
        var edits = TextDiffer.Compute(left, right, cancellationToken);
        return (TextDiffer.Segments(left, 0, edits.Select(c => c.Left).ToArray()),
            TextDiffer.Segments(right, 0, edits.Select(c => c.Right).ToArray()));
    }
}
