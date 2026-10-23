using System.Text;
using DiffComparer.Core;

namespace DiffComparer.Merge;

public enum MergeChoice { Unresolved, Left, Right, BothLeftFirst, BothRightFirst, Base, Manual }

public sealed record MergeBlock(int Id, int BaseStart, string BaseText, string LeftText,
    string RightText, bool IsConflict, string AutomaticText);

/// <summary>A merge plan owns immutable source text. Resolution belongs to a separate session.</summary>
public sealed record MergePlan(IReadOnlyList<MergeBlock> Blocks);

public static class ThreeWayMerge
{
    private sealed record Edit(SequenceEdit Range, bool Left);

    public static MergePlan Compute(string baseText, string leftText, string rightText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseText);
        ArgumentNullException.ThrowIfNull(leftText);
        ArgumentNullException.ThrowIfNull(rightText);
        cancellationToken.ThrowIfCancellationRequested();
        var basis = Lines(baseText);
        var left = Lines(leftText);
        var right = Lines(rightText);
        // Shared interning avoids hash collisions and keeps comparison exact, including EOLs.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] Intern(string[] lines) => lines.Select(line =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ids.TryGetValue(line, out int id)) ids[line] = id = ids.Count;
            return id;
        }).ToArray();
        var b = Intern(basis);
        var l = Intern(left);
        var r = Intern(right);
        var edits = SequenceDiffer.Compute(b, l, cancellationToken: cancellationToken).Select(e => new Edit(e, true))
            .Concat(SequenceDiffer.Compute(b, r, cancellationToken: cancellationToken).Select(e => new Edit(e, false)))
            .OrderBy(e => e.Range.LeftStart).ThenBy(e => e.Range.LeftEnd).ToArray();
        var blocks = new List<MergeBlock>();
        int cursor = 0;
        for (int i = 0; i < edits.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = new List<Edit> { edits[i++] };
            // Only overlapping changes conflict. Insertions at the same point overlap;
            // insertions exactly at a replacement boundary remain independent.
            while (i < edits.Length && group.Any(e => Overlaps(e.Range, edits[i].Range))) group.Add(edits[i++]);
            int start = group.Min(e => e.Range.LeftStart), end = group.Max(e => e.Range.LeftEnd);
            if (start > cursor) Add(cursor, Join(basis, cursor, start));
            string original = Join(basis, start, end);
            string a = Apply(basis, left, start, end, group.Where(e => e.Left));
            string c = Apply(basis, right, start, end, group.Where(e => !e.Left));
            bool conflict = a != c && a != original && c != original;
            blocks.Add(new(blocks.Count, start, original, a, c, conflict,
                conflict ? original : a == original ? c : a));
            cursor = end;
        }
        if (cursor < basis.Length) Add(cursor, Join(basis, cursor, basis.Length));
        return new(blocks.AsReadOnly());

        void Add(int start, string text) =>
            blocks.Add(new(blocks.Count, start, text, text, text, false, text));
    }

    private static bool Overlaps(SequenceEdit a, SequenceEdit b)
    {
        if (a.LeftLength == 0 && b.LeftLength == 0) return a.LeftStart == b.LeftStart;
        if (a.LeftLength == 0) return a.LeftStart > b.LeftStart && a.LeftStart < b.LeftEnd;
        if (b.LeftLength == 0) return b.LeftStart > a.LeftStart && b.LeftStart < a.LeftEnd;
        return a.LeftStart < b.LeftEnd && b.LeftStart < a.LeftEnd;
    }

    private static string Apply(string[] basis, string[] side, int start, int end, IEnumerable<Edit> edits)
    {
        var result = new StringBuilder();
        int cursor = start;
        foreach (var edit in edits.OrderBy(e => e.Range.LeftStart))
        {
            result.Append(Join(basis, cursor, edit.Range.LeftStart));
            result.Append(Join(side, edit.Range.RightStart, edit.Range.RightEnd));
            cursor = edit.Range.LeftEnd;
        }
        return result.Append(Join(basis, cursor, end)).ToString();
    }

    private static string Join(string[] lines, int start, int end) => string.Concat(lines.AsSpan(start, end - start));

    private static string[] Lines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] is '\r' or '\n')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        if (start < text.Length) lines.Add(text[start..]);
        return lines.ToArray();
    }
}
