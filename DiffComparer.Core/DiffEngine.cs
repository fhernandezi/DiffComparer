using System;
using System.Collections.Generic;
using System.Linq;

namespace DiffComparer.Core
{
    public class DiffEngine
    {
        public int SimilarityThreshold { get; set; } = 60;

        public List<LineMap> Compare(string[] left, string[] right)
        {
            var anchors = FindAnchors(left, right);
            var result = new List<LineMap>();
            int leftPos = 0, rightPos = 0;

            foreach (var (leftAnchor, rightAnchor) in anchors)
            {
                var leftSegment = left[leftPos..leftAnchor];
                var rightSegment = right[rightPos..rightAnchor];

                result.AddRange(AlignSegmentMyers(
                    leftSegment,
                    rightSegment,
                    leftPos,
                    rightPos));

                AddAnchorLine(
                    result,
                    leftAnchor,
                    rightAnchor,
                    left[leftAnchor],
                    right[rightAnchor]);

                leftPos = leftAnchor + 1;
                rightPos = rightAnchor + 1;
            }

            result.AddRange(AlignSegmentMyers(
                left[leftPos..],
                right[rightPos..],
                leftPos,
                rightPos));

            return result;
        }

        private List<LineMap> AlignSegmentMyers(
            string[] left,
            string[] right,
            int leftOffset,
            int rightOffset)
        {
            var ops = MyersDiff(left, right);
            return BuildLineMapsFromOps(ops, left, right, leftOffset, rightOffset);
        }

        private enum EditType
        {
            Equal,
            Delete,
            Insert
        }

        private sealed class EditOp
        {
            public EditType Type { get; }
            public int LeftIndex { get; }
            public int RightIndex { get; }

            public EditOp(EditType type, int leftIndex, int rightIndex)
            {
                Type = type;
                LeftIndex = leftIndex;
                RightIndex = rightIndex;
            }
        }

        private List<EditOp> MyersDiff(string[] left, string[] right)
        {
            int n = left.Length;
            int m = right.Length;
            int max = n + m;

            var trace = new List<Dictionary<int, int>>();
            var v = new Dictionary<int, int>
            {
                [1] = 0
            };

            for (int d = 0; d <= max; d++)
            {
                trace.Add(new Dictionary<int, int>(v));

                for (int k = -d; k <= d; k += 2)
                {
                    int x;

                    if (k == -d || (k != d && GetV(v, k - 1) < GetV(v, k + 1)))
                    {
                        x = GetV(v, k + 1); // insert
                    }
                    else
                    {
                        x = GetV(v, k - 1) + 1; // delete
                    }

                    int y = x - k;

                    while (x < n && y < m && Normalize(left[x]) == Normalize(right[y]))
                    {
                        x++;
                        y++;
                    }

                    v[k] = x;

                    if (x >= n && y >= m)
                    {
                        trace.Add(new Dictionary<int, int>(v));
                        return Backtrack(trace, left, right);
                    }
                }
            }

            return new List<EditOp>();
        }

        private List<EditOp> Backtrack(
            List<Dictionary<int, int>> trace,
            string[] left,
            string[] right)
        {
            int x = left.Length;
            int y = right.Length;
            var result = new List<EditOp>();

            for (int d = trace.Count - 1; d > 0; d--)
            {
                var v = trace[d - 1];
                int k = x - y;

                int prevK;
                if (k == -(d - 1) || (k != (d - 1) && GetV(v, k - 1) < GetV(v, k + 1)))
                {
                    prevK = k + 1;
                }
                else
                {
                    prevK = k - 1;
                }

                int prevX = GetV(v, prevK);
                int prevY = prevX - prevK;

                while (x > prevX && y > prevY)
                {
                    result.Add(new EditOp(EditType.Equal, x - 1, y - 1));
                    x--;
                    y--;
                }

                if (d == 1)
                    break;

                if (x == prevX)
                {
                    result.Add(new EditOp(EditType.Insert, -1, y - 1));
                    y--;
                }
                else
                {
                    result.Add(new EditOp(EditType.Delete, x - 1, -1));
                    x--;
                }
            }

            while (x > 0 && y > 0)
            {
                if (Normalize(left[x - 1]) == Normalize(right[y - 1]))
                {
                    result.Add(new EditOp(EditType.Equal, x - 1, y - 1));
                    x--;
                    y--;
                }
                else
                {
                    break;
                }
            }

            while (x > 0)
            {
                result.Add(new EditOp(EditType.Delete, x - 1, -1));
                x--;
            }

            while (y > 0)
            {
                result.Add(new EditOp(EditType.Insert, -1, y - 1));
                y--;
            }

            result.Reverse();
            return result;
        }

        private List<LineMap> BuildLineMapsFromOps(
     List<EditOp> ops,
     string[] left,
     string[] right,
     int leftOffset,
     int rightOffset)
        {
            var result = new List<LineMap>();
            int i = 0;

            while (i < ops.Count)
            {
                if (ops[i].Type == EditType.Equal)
                {
                    AddEqual(result, ops[i], left, right, leftOffset, rightOffset);
                    i++;
                    continue;
                }

                if (ops[i].Type == EditType.Delete)
                {
                    var deletes = new List<EditOp>();

                    while (i < ops.Count && ops[i].Type == EditType.Delete)
                    {
                        deletes.Add(ops[i]);
                        i++;
                    }

                    var inserts = new List<EditOp>();
                    int j = i;

                    while (j < ops.Count && ops[j].Type == EditType.Insert)
                    {
                        inserts.Add(ops[j]);
                        j++;
                    }

                    if (inserts.Count == 0)
                    {
                        foreach (var del in deletes)
                            AddDeleted(result, del, left, leftOffset);

                        continue;
                    }

                    var pairs = PairDeletesAndInserts(deletes, inserts, left, right);

                    var pairedDeletes = new HashSet<EditOp>();
                    var pairedInserts = new HashSet<EditOp>();

                    foreach (var pair in pairs.OrderBy(p => p.Delete.LeftIndex))
                    {
                        if (pair.Similarity >= SimilarityThreshold)
                        {
                            string l = left[pair.Delete.LeftIndex];
                            string r = right[pair.Insert.RightIndex];

                            var (leftSegs, rightSegs) = WordDiff(l, r);

                            result.Add(new LineMap(
                                leftOffset + pair.Delete.LeftIndex,
                                rightOffset + pair.Insert.RightIndex,
                                l,
                                r,
                                LineStatus.Modified,
                                pair.Similarity)
                            {
                                LeftSegments = leftSegs,
                                RightSegments = rightSegs
                            });

                            pairedDeletes.Add(pair.Delete);
                            pairedInserts.Add(pair.Insert);
                        }
                    }

                    foreach (var del in deletes)
                    {
                        if (!pairedDeletes.Contains(del))
                            AddDeleted(result, del, left, leftOffset);
                    }

                    foreach (var ins in inserts)
                    {
                        if (!pairedInserts.Contains(ins))
                            AddAdded(result, ins, right, rightOffset);
                    }

                    i = j;
                    continue;
                }

                if (ops[i].Type == EditType.Insert)
                {
                    AddAdded(result, ops[i], right, rightOffset);
                    i++;
                }
            }

            return result;
        }

        private static void AddEqual(
    List<LineMap> result,
    EditOp op,
    string[] left,
    string[] right,
    int leftOffset,
    int rightOffset)
        {
            string l = left[op.LeftIndex];
            string r = right[op.RightIndex];

            result.Add(new LineMap(
                leftOffset + op.LeftIndex,
                rightOffset + op.RightIndex,
                l,
                r,
                LineStatus.Equal,
                100)
            {
                LeftSegments = new List<WordSegment> { new(l, false) },
                RightSegments = new List<WordSegment> { new(r, false) }
            });
        }

        private static void AddDeleted(
            List<LineMap> result,
            EditOp op,
            string[] left,
            int leftOffset)
        {
            string txt = left[op.LeftIndex];

            result.Add(new LineMap(
                leftOffset + op.LeftIndex,
                null,
                txt,
                "",
                LineStatus.Deleted,
                0)
            {
                LeftSegments = new List<WordSegment> { new(txt, false) },
                RightSegments = new List<WordSegment>()
            });
        }

        private static void AddAdded(
            List<LineMap> result,
            EditOp op,
            string[] right,
            int rightOffset)
        {
            string txt = right[op.RightIndex];

            result.Add(new LineMap(
                null,
                rightOffset + op.RightIndex,
                "",
                txt,
                LineStatus.Added,
                0)
            {
                LeftSegments = new List<WordSegment>(),
                RightSegments = new List<WordSegment> { new(txt, false) }
            });
        }

        private static int GetV(Dictionary<int, int> v, int k)
        {
            return v.TryGetValue(k, out var value) ? value : 0;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;

            return string.Join(" ", s.Trim().Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries));
        }

        private List<(int Left, int Right)> FindAnchors(string[] left, string[] right)
        {
            var anchors = FindExactUniqueAnchors(left, right);

            anchors.AddRange(FindSimilarAnchors(
                left,
                right,
                anchors,
                minSimilarity: 85));

            anchors = anchors
                .Distinct()
                .OrderBy(a => a.Left)
                .ThenBy(a => a.Right)
                .ToList();

            return FilterMonotonicByBestSequence(anchors);
        }

        private List<(int Left, int Right)> FindSimilarAnchors(
            string[] left,
            string[] right,
            List<(int Left, int Right)> existingAnchors,
            int minSimilarity)
        {
            var result = new List<(int Left, int Right)>();

            var usedLeft = existingAnchors.Select(a => a.Left).ToHashSet();
            var usedRight = existingAnchors.Select(a => a.Right).ToHashSet();

            const int window = 20;

            for (int i = 0; i < left.Length; i++)
            {
                if (usedLeft.Contains(i)) continue;

                string l = Normalize(left[i]);
                if (string.IsNullOrWhiteSpace(l) || l.Length < 8) continue;

                int bestRight = -1;
                int bestScore = 0;

                int from = Math.Max(0, i - window);
                int to = Math.Min(right.Length - 1, i + window);

                for (int j = from; j <= to; j++)
                {
                    if (usedRight.Contains(j)) continue;

                    string r = Normalize(right[j]);
                    if (string.IsNullOrWhiteSpace(r) || r.Length < 8) continue;

                    int score = Similarity(l, r);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRight = j;
                    }
                }

                if (bestRight >= 0 && bestScore >= minSimilarity)
                {
                    result.Add((i, bestRight));
                    usedLeft.Add(i);
                    usedRight.Add(bestRight);
                }
            }

            return result;
        }

        private List<(EditOp Delete, EditOp Insert, int Similarity)> PairDeletesAndInserts(
            List<EditOp> deletes,
            List<EditOp> inserts,
            string[] left,
            string[] right)
        {
            var pairs = new List<(EditOp Delete, EditOp Insert, int Similarity)>();
            var usedInserts = new HashSet<int>();

            foreach (var del in deletes)
            {
                int bestIndex = -1;
                int bestScore = 0;

                for (int i = 0; i < inserts.Count; i++)
                {
                    if (usedInserts.Contains(i)) continue;

                    int score = Similarity(
                        left[del.LeftIndex],
                        right[inserts[i].RightIndex]);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                {
                    usedInserts.Add(bestIndex);
                    pairs.Add((del, inserts[bestIndex], bestScore));
                }
            }

            return pairs;
        }
        private List<(int Left, int Right)> FilterMonotonicByBestSequence(
            List<(int Left, int Right)> anchors)
        {
            if (anchors.Count == 0)
                return anchors;

            anchors = anchors
                .OrderBy(a => a.Left)
                .ThenBy(a => a.Right)
                .ToList();

            int n = anchors.Count;
            int[] dp = new int[n];
            int[] prev = new int[n];

            Array.Fill(dp, 1);
            Array.Fill(prev, -1);

            int bestIndex = 0;

            for (int i = 1; i < n; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (anchors[j].Right < anchors[i].Right && dp[j] + 1 > dp[i])
                    {
                        dp[i] = dp[j] + 1;
                        prev[i] = j;
                    }
                }

                if (dp[i] > dp[bestIndex])
                    bestIndex = i;
            }

            var result = new List<(int Left, int Right)>();

            for (int i = bestIndex; i >= 0; i = prev[i])
            {
                result.Add(anchors[i]);
                if (prev[i] == -1) break;
            }

            result.Reverse();
            return result;
        }
        private List<(int Left, int Right)> FindExactUniqueAnchors(string[] left, string[] right)
        {
            var leftCount = CountOccurrences(left);
            var rightCount = CountOccurrences(right);

            var rightUniqueIndex = new Dictionary<string, int>();

            for (int j = 0; j < right.Length; j++)
            {
                var t = Normalize(right[j]);
                if (string.IsNullOrWhiteSpace(t)) continue;

                if (rightCount.TryGetValue(t, out int rc) && rc == 1)
                    rightUniqueIndex[t] = j;
            }

            var anchors = new List<(int Left, int Right)>();

            for (int i = 0; i < left.Length; i++)
            {
                var t = Normalize(left[i]);
                if (string.IsNullOrWhiteSpace(t)) continue;

                if (leftCount.TryGetValue(t, out int lc) && lc == 1 &&
                    rightUniqueIndex.TryGetValue(t, out int rightIdx))
                {
                    anchors.Add((i, rightIdx));
                }
            }

            return anchors;
        }

        private List<(int Left, int Right)> FilterMonotonic(
            List<(int Left, int Right)> anchors)
        {
            var result = new List<(int Left, int Right)>();
            int lastRight = -1;

            foreach (var anchor in anchors)
            {
                if (anchor.Right > lastRight)
                {
                    result.Add(anchor);
                    lastRight = anchor.Right;
                }
            }

            return result;
        }

        private Dictionary<string, int> CountOccurrences(string[] lines)
        {
            var counts = new Dictionary<string, int>();
            foreach (var line in lines)
            {
                var t = Normalize(line);
                if (string.IsNullOrWhiteSpace(t)) continue;
                counts[t] = counts.GetValueOrDefault(t, 0) + 1;
            }
            return counts;
        }

        public static int Similarity(string a, string b)
        {
            a = Normalize(a);
            b = Normalize(b);

            if (a == b) return 100;
            if (a.Length == 0 || b.Length == 0) return 0;

            int maxLen = Math.Max(a.Length, b.Length);
            int minLen = Math.Min(a.Length, b.Length);

            if ((double)minLen / maxLen < 0.3) return 0;

            a = TrimForSimilarity(a);
            b = TrimForSimilarity(b);

            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }

            int dist = prev[b.Length];
            return (int)((1.0 - (double)dist / Math.Max(a.Length, b.Length)) * 100);
        }

        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();

            if (string.IsNullOrEmpty(text))
                return tokens;

            var current = "";
            TokenKind? currentKind = null;

            foreach (char c in text)
            {
                var kind = GetTokenKind(c);

                if (currentKind == null || kind == currentKind)
                {
                    current += c;
                    currentKind = kind;
                }
                else
                {
                    tokens.Add(current);
                    current = c.ToString();
                    currentKind = kind;
                }
            }

            if (current.Length > 0)
                tokens.Add(current);

            return tokens;
        }

        private static string TrimForSimilarity(string value, int max = 200)
        {
            value = Normalize(value);

            if (value.Length <= max)
                return value;

            int half = max / 2;
            return value[..half] + value[^half..];
        }
        private enum TokenKind
        {
            LetterOrDigit,
            WhiteSpace,
            Symbol
        }

        private static TokenKind GetTokenKind(char c)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                return TokenKind.LetterOrDigit;

            if (char.IsWhiteSpace(c))
                return TokenKind.WhiteSpace;

            return TokenKind.Symbol;
        }

        // Agrupa caracteres contiguos del mismo estado en un solo segmento
        private static List<WordSegment> MergeSegments(List<WordSegment> segments)
        {
            var result = new List<WordSegment>();
            if (segments.Count == 0) return result;

            var current = segments[0];
            for (int i = 1; i < segments.Count; i++)
            {
                if (segments[i].IsChanged == current.IsChanged)
                {
                    current = new WordSegment(current.Text + segments[i].Text, current.IsChanged);
                }
                else
                {
                    result.Add(current);
                    current = segments[i];
                }
            }
            result.Add(current);
            return result;
        }

        private static (List<WordSegment>, List<WordSegment>) BuildWordSegments(
            List<Dictionary<int, int>> trace,
            List<string> left, List<string> right)
        {
            int x = left.Count, y = right.Count;
            var leftSegs = new List<(int idx, bool changed)>();
            var rightSegs = new List<(int idx, bool changed)>();

            for (int d = trace.Count - 1; d > 0; d--)
            {
                var v = trace[d - 1];
                int k = x - y;
                int prevK = (k == -(d - 1) || (k != (d - 1) && GetV(v, k - 1) < GetV(v, k + 1)))
                    ? k + 1 : k - 1;
                int prevX = GetV(v, prevK);
                int prevY = prevX - prevK;

                while (x > prevX && y > prevY)
                {
                    leftSegs.Add((x - 1, false));
                    rightSegs.Add((y - 1, false));
                    x--; y--;
                }
                if (d == 1) break;
                if (x == prevX) { rightSegs.Add((y - 1, true)); y--; }
                else { leftSegs.Add((x - 1, true)); x--; }
            }

            while (x > 0) { leftSegs.Add((x - 1, true)); x--; }
            while (y > 0) { rightSegs.Add((y - 1, true)); y--; }

            leftSegs.Reverse();
            rightSegs.Reverse();

            return (
                leftSegs.Select(s => new WordSegment(left[s.idx], s.changed)).ToList(),
                rightSegs.Select(s => new WordSegment(right[s.idx], s.changed)).ToList()
            );
        }

        public static (List<WordSegment> Left, List<WordSegment> Right) WordDiff(
    string leftText,
    string rightText)
        {
            var leftTokens = Tokenize(leftText);
            var rightTokens = Tokenize(rightText);

            int n = leftTokens.Count;
            int m = rightTokens.Count;
            int max = n + m;

            var trace = new List<Dictionary<int, int>>();
            var v = new Dictionary<int, int> { [1] = 0 };

            for (int d = 0; d <= max; d++)
            {
                trace.Add(new Dictionary<int, int>(v));

                for (int k = -d; k <= d; k += 2)
                {
                    int x = (k == -d || (k != d && GetV(v, k - 1) < GetV(v, k + 1)))
                        ? GetV(v, k + 1)
                        : GetV(v, k - 1) + 1;

                    int y = x - k;

                    while (x < n && y < m && leftTokens[x] == rightTokens[y])
                    {
                        x++;
                        y++;
                    }

                    v[k] = x;

                    if (x >= n && y >= m)
                    {
                        trace.Add(new Dictionary<int, int>(v));

                        var (ls, rs) = BuildWordSegments(trace, leftTokens, rightTokens);

                        return (MergeSegments(ls), MergeSegments(rs));
                    }
                }
            }

            return (
                new List<WordSegment> { new(leftText, true) },
                new List<WordSegment> { new(rightText, true) }
            );
        }

        private void AddAnchorLine(
    List<LineMap> result,
    int leftIndex,
    int rightIndex,
    string leftText,
    string rightText)
        {
            if (Normalize(leftText) == Normalize(rightText))
            {
                result.Add(new LineMap(
                    leftIndex,
                    rightIndex,
                    leftText,
                    rightText,
                    LineStatus.Equal,
                    100)
                {
                    LeftSegments = new List<WordSegment> { new(leftText, false) },
                    RightSegments = new List<WordSegment> { new(rightText, false) }
                });

                return;
            }

            int sim = Similarity(leftText, rightText);
            var (leftSegs, rightSegs) = WordDiff(leftText, rightText);

            result.Add(new LineMap(
                leftIndex,
                rightIndex,
                leftText,
                rightText,
                LineStatus.Modified,
                sim)
            {
                LeftSegments = leftSegs,
                RightSegments = rightSegs
            });
        }


    }

}