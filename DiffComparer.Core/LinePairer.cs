namespace DiffComparer.Core;

internal readonly record struct LinePair(int? Left, int? Right);

/// <summary>Joint monotone replacement alignment. Full DP for small blocks, a bounded band for large ones.
/// This only selects display partners; exact text edits are computed independently.</summary>
internal static class LinePairer
{
    public static IReadOnlyList<LinePair> Align(string[] left, string[] right, int threshold, CancellationToken ct, out bool banded)
    {
        int n = left.Length, m = right.Length;
        banded = (long)n * m > 16384;
        if (n == 0) return Enumerable.Range(0, m).Select(i => new LinePair(null, i)).ToArray();
        if (m == 0) return Enumerable.Range(0, n).Select(i => new LinePair(i, null)).ToArray();
        int band = banded ? 64 + (int)Math.Ceiling((double)m / n) : m + 1;
        var trace = new (int Low, byte[] Directions)[n + 1];
        var previous = new double[m + 1];
        var current = new double[m + 1];
        const double gap = 0.65;
        int previousLow = 0, previousHigh = Math.Min(m, band);
        trace[0] = (0, new byte[previousHigh + 1]);
        for (int j = 1; j <= previousHigh; j++) { previous[j] = j * gap; trace[0].Directions[j] = 2; }
        bool exactScores = (long)n * m <= 4096;
        var aFeatures = left.Select(s => new Features(s)).ToArray();
        var bFeatures = right.Select(s => new Features(s)).ToArray();
        var cache = new Dictionary<(string, string), int>();
        for (int i = 1; i <= n; i++)
        {
            ct.ThrowIfCancellationRequested();
            int center = (int)((long)i * m / n);
            int low = Math.Max(0, center - band), high = Math.Min(m, center + band);
            var directions = new byte[high - low + 1];
            trace[i] = (low, directions);
            for (int j = low; j <= high; j++)
            {
                if ((j & 127) == 0) ct.ThrowIfCancellationRequested();
                double deletion = j >= previousLow && j <= previousHigh ? previous[j] + gap : double.PositiveInfinity;
                double insertion = j > low ? current[j - 1] + gap : double.PositiveInfinity;
                double best = deletion;
                byte direction = 1;
                if (insertion < best) { best = insertion; direction = 2; }
                if (j > 0 && j - 1 >= previousLow && j - 1 <= previousHigh)
                {
                    var key = (left[i - 1], right[j - 1]);
                    if (!cache.TryGetValue(key, out int score))
                    {
                        score = exactScores && key.Item1.Length + key.Item2.Length <= 2000
                            ? DiffEngine.Similarity(key.Item1, key.Item2, ct) : aFeatures[i - 1].Score(bFeatures[j - 1]);
                        if (cache.Count < 8192) cache[key] = score;
                    }
                    double pair = score >= threshold ? previous[j - 1] + 1.0 - score / 100.0 : double.PositiveInfinity;
                    if (pair <= best && !double.IsPositiveInfinity(pair)) { best = pair; direction = 3; }
                }
                current[j] = best;
                directions[j - low] = direction;
            }
            (previous, current) = (current, previous);
            previousLow = low; previousHigh = high;
        }
        if (double.IsPositiveInfinity(previous[m])) throw new InvalidOperationException("No monotone display alignment exists in the selected band.");
        var result = new List<LinePair>(n + m);
        int x = n, y = m;
        while (x > 0 || y > 0)
        {
            var row = trace[x];
            switch (row.Directions[y - row.Low])
            {
                case 3: result.Add(new(--x, --y)); break;
                case 1: result.Add(new(--x, null)); break;
                case 2: result.Add(new(null, --y)); break;
                default: throw new InvalidOperationException("Invalid display alignment path.");
            }
        }
        result.Reverse();
        return result;
    }

    private sealed class Features
    {
        private readonly string _text;
        private readonly Dictionary<char, int> _characters = new();
        private readonly Dictionary<int, int> _bigrams = new();
        public Features(string text)
        {
            _text = TextDiffer.Normalize(text);
            for (int i = 0; i < _text.Length; i++)
            {
                _characters[_text[i]] = _characters.GetValueOrDefault(_text[i]) + 1;
                if (i == 0) continue;
                int pair = (_text[i - 1] << 16) | _text[i];
                _bigrams[pair] = _bigrams.GetValueOrDefault(pair) + 1;
            }
        }
        public int Score(Features other)
        {
            if (_text == other._text) return 100;
            if (_text.Length == 0 || other._text.Length == 0) return 0;
            int common = _characters.Sum(p => Math.Min(p.Value, other._characters.GetValueOrDefault(p.Key)));
            int grams = _bigrams.Sum(p => Math.Min(p.Value, other._bigrams.GetValueOrDefault(p.Key)));
            return (int)(100.0 * (common + grams) / (_text.Length + other._text.Length - 1));
        }
    }
}
