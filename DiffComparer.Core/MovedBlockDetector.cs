using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DiffComparer.Core;

public static class MovedBlockDetector
{
    // Only unambiguous, identical text is marked as moved. The original edit script is preserved.
    public static LineMap[] Detect(IReadOnlyList<LineMap> lines, CancellationToken ct = default)
    {
        var result = lines.ToArray();
        var deleted = new Dictionary<string, int>(StringComparer.Ordinal);
        var added = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < result.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = result[i];
            var map = line.Status == LineStatus.Deleted ? deleted : line.Status == LineStatus.Added ? added : null;
            var text = line.Status == LineStatus.Deleted ? line.LeftText : line.RightText;
            if (map is null || string.IsNullOrWhiteSpace(text)) continue;
            if (!map.TryAdd(text, i)) map[text] = -1;
        }
        var matches = new Dictionary<int, int>();
        foreach (var pair in deleted)
        {
            ct.ThrowIfCancellationRequested();
            if (pair.Value >= 0 && added.TryGetValue(pair.Key, out int index) && index >= 0)
                matches[pair.Value] = index;
        }
        int id = 0;
        for (int i = 0; i < result.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!matches.TryGetValue(i, out int destination)) continue;
            int count = 1;
            while (matches.TryGetValue(i + count, out int next) && next == destination + count) count++;
            // Avoid labelling isolated braces/short boilerplate as a code movement.
            if (count > 1 || result[i].LeftText.Trim().Length >= 8)
            {
                id++;
                for (int offset = 0; offset < count; offset++)
                {
                    var sourceLine = result[i + offset];
                    var to = result[destination + offset];
                    result[i + offset] = sourceLine with { MoveId = id, MovedPartnerLine = to.RightLine };
                    result[destination + offset] = to with { MoveId = id, MovedPartnerLine = sourceLine.LeftLine };
                }
            }
            i += count - 1;
        }
        return result;
    }
}

