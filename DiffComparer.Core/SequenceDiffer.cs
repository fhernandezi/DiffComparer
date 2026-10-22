using System.Buffers;

namespace DiffComparer.Core;

public enum DiffStrategy { Readable, Minimal }
public readonly record struct SequenceEdit(int LeftStart, int LeftLength, int RightStart, int RightLength)
{
    public int LeftEnd => LeftStart + LeftLength;
    public int RightEnd => RightStart + RightLength;
}

/// <summary>Ordered exact edit script. Patience/low-frequency anchors for readability;
/// bidirectional Myers for the remaining regions. No truncation of the final result.</summary>
public static class SequenceDiffer
{
    public static IReadOnlyList<SequenceEdit> Compute(int[] left, int[] right,
        DiffStrategy strategy = DiffStrategy.Readable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var edits = new List<SequenceEdit>();
        var pending = new Stack<(int A, int AE, int B, int BE)>();
        pending.Push((0, left.Length, 0, right.Length));
        while (pending.TryPop(out var range))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int a = range.A, ae = range.AE, b = range.B, be = range.BE;
            while (a < ae && b < be && left[a] == right[b]) { a++; b++; if ((a & 1023) == 0) cancellationToken.ThrowIfCancellationRequested(); }
            while (a < ae && b < be && left[ae - 1] == right[be - 1]) { ae--; be--; if ((ae & 1023) == 0) cancellationToken.ThrowIfCancellationRequested(); }
            int n = ae - a, m = be - b;
            if (n == 0 || m == 0) { if (n + m > 0) edits.Add(new(a, n, b, m)); continue; }
            if ((long)n * m <= 4096) { SmallDiff(left, a, ae, right, b, be, edits, cancellationToken); continue; }
            var positions = new Dictionary<int, List<int>>();
            for (int j = b; j < be; j++)
            {
                if ((j & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!positions.TryGetValue(right[j], out var list)) positions[right[j]] = list = new();
                // Counts above 64 are retained by the sentinel; candidate positions are bounded.
                if (list.Count <= 64) list.Add(j);
            }
            bool common = false;
            for (int i = a; i < ae && !common; i++) common = positions.ContainsKey(left[i]);
            if (!common) { edits.Add(new(a, n, b, m)); continue; }
            if (strategy == DiffStrategy.Readable)
            {
                var anchors = Anchors(left, a, ae, positions, cancellationToken);
                if (anchors.Count > 0)
                {
                    int lastA = a, lastB = b;
                    foreach (var anchor in anchors)
                    {
                        if (anchor.A > lastA || anchor.B > lastB) pending.Push((lastA, anchor.A, lastB, anchor.B));
                        lastA = anchor.A + 1; lastB = anchor.B + 1;
                    }
                    if (lastA < ae || lastB < be) pending.Push((lastA, ae, lastB, be));
                    continue;
                }
            }
            var (x, y) = Bisect(left, a, ae, right, b, be, cancellationToken);
            if ((x == a && y == b) || (x == ae && y == be))
                throw new InvalidOperationException("Myers failed to partition a nontrivial region.");
            pending.Push((x, ae, y, be));
            pending.Push((a, x, b, y));
        }
        edits.Sort((x, y) => x.LeftStart != y.LeftStart ? x.LeftStart.CompareTo(y.LeftStart) : x.RightStart.CompareTo(y.RightStart));
        var merged = new List<SequenceEdit>(edits.Count);
        foreach (var edit in edits)
        {
            if (merged.Count > 0 && merged[^1].LeftEnd == edit.LeftStart && merged[^1].RightEnd == edit.RightStart)
            {
                var previous = merged[^1];
                merged[^1] = previous with { LeftLength = previous.LeftLength + edit.LeftLength, RightLength = previous.RightLength + edit.RightLength };
            }
            else merged.Add(edit);
        }
        return merged;
    }

    private static List<(int A, int B)> Anchors(int[] left, int start, int end,
        Dictionary<int, List<int>> right, CancellationToken ct)
    {
        var counts = new Dictionary<int, (int Count, int Position)>();
        for (int i = start; i < end; i++)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            var value = counts.GetValueOrDefault(left[i]);
            counts[left[i]] = (value.Count + 1, value.Count == 0 ? i : value.Position);
        }
        var candidates = counts.Where(p => p.Value.Count == 1 && right.TryGetValue(p.Key, out var list) && list.Count == 1)
            .Select(p => (A: p.Value.Position, B: right[p.Key][0])).OrderBy(p => p.A).ToArray();
        if (candidates.Length > 0)
        {
            var tails = new int[candidates.Length];
            var previous = new int[candidates.Length];
            Array.Fill(previous, -1);
            int length = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                int low = 0, high = length;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (candidates[tails[middle]].B < candidates[i].B) low = middle + 1; else high = middle;
                }
                if (low > 0) previous[i] = tails[low - 1];
                tails[low] = i;
                if (low == length) length++;
            }
            var result = new List<(int, int)>();
            for (int i = tails[length - 1]; i >= 0; i = previous[i]) result.Add(candidates[i]);
            result.Reverse();
            return result;
        }
        // Histogram-style split: rare exact lines remain useful when no unique lines exist.
        int rarity = int.MaxValue, bestA = -1, bestB = -1;
        foreach (var entry in counts)
        {
            ct.ThrowIfCancellationRequested();
            if (!right.TryGetValue(entry.Key, out var positions) || positions.Count > 64 || entry.Value.Count > 64) continue;
            int score = Math.Max(entry.Value.Count, positions.Count);
            if (score < rarity || (score == rarity && entry.Value.Position < bestA))
            { rarity = score; bestA = entry.Value.Position; bestB = positions[0]; }
        }
        return bestA < 0 ? new() : new() { (bestA, bestB) };
    }

    private static void SmallDiff(int[] a, int startA, int endA, int[] b, int startB, int endB,
        List<SequenceEdit> edits, CancellationToken ct)
    {
        int n = endA - startA, m = endB - startB;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[startA + i] == b[startB + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && a[startA + x] == b[startB + y]) { x++; y++; continue; }
            int beginX = x, beginY = y;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[startA + x] == b[startB + y]) break;
                if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1])) x++; else y++;
            }
            edits.Add(new(startA + beginX, x - beginX, startB + beginY, y - beginY));
        }
    }

    private static (int A, int B) Bisect(int[] a, int startA, int endA, int[] b, int startB, int endB, CancellationToken ct)
    {
        int n = endA - startA, m = endB - startB;
        int limit = (n + m + 1) / 2, offset = limit + 1, size = limit * 2 + 3;
        int[] forward = ArrayPool<int>.Shared.Rent(size), reverse = ArrayPool<int>.Shared.Rent(size);
        try
        {
            Array.Fill(forward, -1, 0, size); Array.Fill(reverse, -1, 0, size);
            forward[offset + 1] = reverse[offset + 1] = 0;
            int delta = n - m;
            bool odd = (delta & 1) != 0;
            for (int depth = 0; depth <= limit; depth++)
            {
                ct.ThrowIfCancellationRequested();
                for (int k = -depth; k <= depth; k += 2)
                {
                    int at = offset + k;
                    int x = k == -depth || (k != depth && forward[at - 1] < forward[at + 1]) ? forward[at + 1] : forward[at - 1] + 1;
                    int y = x - k;
                    while (x < n && y < m && x >= 0 && y >= 0 && a[startA + x] == b[startB + y])
                    { x++; y++; if ((x & 1023) == 0) ct.ThrowIfCancellationRequested(); }
                    forward[at] = x;
                    int rk = delta - k;
                    if (odd && rk >= -(depth - 1) && rk <= depth - 1 && reverse[offset + rk] >= 0 && x >= n - reverse[offset + rk])
                        return (startA + x, startB + y);
                }
                for (int k = -depth; k <= depth; k += 2)
                {
                    int at = offset + k;
                    int x = k == -depth || (k != depth && reverse[at - 1] < reverse[at + 1]) ? reverse[at + 1] : reverse[at - 1] + 1;
                    int y = x - k;
                    while (x < n && y < m && x >= 0 && y >= 0 && a[endA - x - 1] == b[endB - y - 1])
                    { x++; y++; if ((x & 1023) == 0) ct.ThrowIfCancellationRequested(); }
                    reverse[at] = x;
                    int fk = delta - k;
                    if (!odd && fk >= -depth && fk <= depth && forward[offset + fk] >= 0 && forward[offset + fk] >= n - x)
                    {
                        int fx = forward[offset + fk];
                        return (startA + fx, startB + fx - fk);
                    }
                }
            }
            throw new InvalidOperationException("Myers did not find a middle frontier.");
        }
        finally { ArrayPool<int>.Shared.Return(forward); ArrayPool<int>.Shared.Return(reverse); }
    }
}
