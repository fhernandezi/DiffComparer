using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DiffComparer.Core;

namespace DiffComparer.UI.Models;

public sealed record ChangeBlock(int Start, int End);

public sealed class ComparisonSnapshot
{
    public static ComparisonSnapshot Empty { get; } = new(Array.Empty<LineMap>());
    public LineMap[] Lines { get; }
    public ChangeBlock[] Blocks { get; }
    public int[] BlockForLine { get; }
    public int Added { get; }
    public int Deleted { get; }
    public int Modified { get; }
    public int MovedBlocks { get; }
    public bool IgnoreWhitespace { get; }
    public bool CoarseAlignment { get; }
    public ComparisonResult? Canonical { get; }

    public ComparisonSnapshot(LineMap[] lines, bool ignoreWhitespace = false, bool coarseAlignment = false, ComparisonResult? canonical = null)
    {
        Lines = lines;
        IgnoreWhitespace = ignoreWhitespace;
        CoarseAlignment = coarseAlignment;
        Canonical = canonical;
        BlockForLine = new int[lines.Length];
        Array.Fill(BlockForLine, -1);
        var blocks = new List<ChangeBlock>();
        var moves = new HashSet<int>();
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Status == LineStatus.Added) Added++;
            if (line.Status == LineStatus.Deleted) Deleted++;
            if (line.Status == LineStatus.Modified) Modified++;
            if (line.MoveId is int move) moves.Add(move);
            if (line.Status != LineStatus.Equal)
            {
                if (start < 0) start = i;
                BlockForLine[i] = blocks.Count;
            }
            else if (start >= 0) { blocks.Add(new(start, i - 1)); start = -1; }
        }
        if (start >= 0) blocks.Add(new(start, lines.Length - 1));
        Blocks = blocks.ToArray();
        if (canonical is not null)
        {
            Blocks = canonical.Hunks.Select(h => new ChangeBlock(h.RowStart, h.RowStart + h.RowCount - 1)).ToArray();
            Array.Fill(BlockForLine, -1);
            foreach (var hunk in canonical.Hunks)
                for (int i = hunk.RowStart; i < hunk.RowStart + hunk.RowCount; i++) BlockForLine[i] = hunk.Id;
        }
        MovedBlocks = moves.Count;
    }
}

public sealed record ViewOptions(bool OnlyDifferences, int Context, bool Added, bool Deleted,
    bool Modified, bool MovedOnly, bool UnreviewedOnly, string Search, bool SearchOnly);
public sealed record ProjectedRow(int SourceIndex, LineMap Line, bool Reviewed, bool Gap = false);
public sealed record SearchHit(int Row, int Column, int Length, bool Left);

public sealed class ComparisonView
{
    public static ComparisonView Empty { get; } = new();
    public ProjectedRow[] Rows { get; init; } = Array.Empty<ProjectedRow>();
    public LineMap[] RenderLines { get; init; } = Array.Empty<LineMap>();
    public string LeftText { get; init; } = "";
    public string RightText { get; init; } = "";
    public Dictionary<int, int> SourceToVisual { get; init; } = new();
    public int[] VisibleBlocks { get; init; } = Array.Empty<int>();
    public SearchHit[] SearchHits { get; init; } = Array.Empty<SearchHit>();
    public HashSet<int> FullyVisibleHunks { get; init; } = new();
    public bool SearchLimitReached { get; init; }
    public IReadOnlyDictionary<int, SearchHit[]> LeftSearchHits { get; init; } = new Dictionary<int, SearchHit[]>();
    public IReadOnlyDictionary<int, SearchHit[]> RightSearchHits { get; init; } = new Dictionary<int, SearchHit[]>();

    public static ComparisonView Build(ComparisonSnapshot snapshot, ViewOptions options,
        HashSet<int> reviewed, CancellationToken ct = default)
    {
        int n = snapshot.Lines.Length;
        int context = Math.Clamp(options.Context, 0, 20);
        var include = new bool[n];
        var eligible = new bool[n];
        bool constrained = options.OnlyDifferences || !options.Added || !options.Deleted || !options.Modified
            || options.MovedOnly || options.UnreviewedOnly || options.SearchOnly;
        bool Contains(string text) => options.Search.Length == 0 || text.Contains(options.Search, StringComparison.OrdinalIgnoreCase);
        var visibleBlocks = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = snapshot.Lines[i];
            bool change = line.Status != LineStatus.Equal;
            bool selected = line.Status switch
            {
                LineStatus.Added => options.Added,
                LineStatus.Deleted => options.Deleted,
                LineStatus.Modified => options.Modified,
                _ => !options.OnlyDifferences
            };
            selected &= !options.MovedOnly || line.MoveId.HasValue;
            selected &= !options.UnreviewedOnly || (change && !reviewed.Contains(snapshot.BlockForLine[i]));
            selected &= !options.SearchOnly || Contains(line.LeftText) || Contains(line.RightText);
            eligible[i] = selected && (change || options.SearchOnly);
            if (eligible[i] && change) visibleBlocks.Add(snapshot.BlockForLine[i]);
            // When constrained, unchanged lines appear only as context around selected rows.
            if (selected && (!constrained || eligible[i]))
                for (int j = Math.Max(0, i - (constrained ? context : 0)); j <= Math.Min(n - 1, i + (constrained ? context : 0)); j++)
                    if (j == i || snapshot.Lines[j].Status == LineStatus.Equal) include[j] = true;
        }
        var rows = new List<ProjectedRow>();
        int skipped = 0;
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!include[i]) { skipped++; continue; }
            if (skipped > 0)
            {
                string label = $"⋯ {skipped} filas ocultas ⋯";
                rows.Add(new(-1, new LineMap(null, null, label, label, LineStatus.Equal, 100), false, true));
                skipped = 0;
            }
            rows.Add(new(i, snapshot.Lines[i], reviewed.Contains(snapshot.BlockForLine[i])));
        }
        if (skipped > 0 && rows.Count > 0)
        {
            string label = $"⋯ {skipped} filas ocultas ⋯";
            rows.Add(new(-1, new LineMap(null, null, label, label, LineStatus.Equal, 100), false, true));
        }
        var left = new System.Text.StringBuilder();
        var right = new System.Text.StringBuilder();
        var render = new LineMap[rows.Count];
        var indices = new Dictionary<int, int>();
        var hits = new List<SearchHit>();
        bool searchLimit = false;
        for (int i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = rows[i];
            if (i > 0) { left.Append('\n'); right.Append('\n'); }
            left.Append(row.Line.LeftText);
            right.Append(row.Line.RightText);
            render[i] = row.Line with { LeftLine = row.Line.LeftLine.HasValue ? i : null, RightLine = row.Line.RightLine.HasValue ? i : null };
            if (!row.Gap) indices[row.SourceIndex] = i;
            if (row.Gap || options.Search.Length == 0) continue;
            void Find(string text, bool isLeft)
            {
                int from = 0;
                while (from <= text.Length - options.Search.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    if (hits.Count >= 50_000) { searchLimit = true; break; }
                    int at = text.IndexOf(options.Search, from, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) break;
                    hits.Add(new(i, at, options.Search.Length, isLeft));
                    from = at + options.Search.Length;
                }
            }
            Find(row.Line.LeftText, true);
            Find(row.Line.RightText, false);
        }
        var visibleCounts = new Dictionary<int, int>();
        foreach (var row in rows)
            if (row.Line.HunkId is int id) visibleCounts[id] = visibleCounts.GetValueOrDefault(id) + 1;
        var fullyVisible = snapshot.Canonical is null ? new HashSet<int>() : snapshot.Canonical.Hunks
            .Where(h => visibleCounts.GetValueOrDefault(h.Id) == h.RowCount).Select(h => h.Id).ToHashSet();
        return new ComparisonView
        {
            Rows = rows.ToArray(), RenderLines = render, LeftText = left.ToString(), RightText = right.ToString(),
            SourceToVisual = indices, VisibleBlocks = visibleBlocks.OrderBy(x => x).ToArray(), SearchHits = hits.ToArray(),
            SearchLimitReached = searchLimit,
            FullyVisibleHunks = fullyVisible,
            LeftSearchHits = hits.Where(h => h.Left).GroupBy(h => h.Row).ToDictionary(g => g.Key, g => g.ToArray()),
            RightSearchHits = hits.Where(h => !h.Left).GroupBy(h => h.Row).ToDictionary(g => g.Key, g => g.ToArray())
        };
    }
}
