using DiffComparer.Core;

namespace DiffComparer.Tests;

public class SequenceDifferTests
{
    [Fact]
    public void MinimalScripts_MatchExhaustiveLcsAndDiffPlex()
    {
        var random = new Random(12345);
        for (int sample = 0; sample < 600; sample++)
        {
            var left = Enumerable.Range(0, random.Next(0, 110)).Select(_ => random.Next(12)).ToArray();
            var right = Enumerable.Range(0, random.Next(0, 110)).Select(_ => random.Next(12)).ToArray();
            var script = SequenceDiffer.Compute(left, right, DiffStrategy.Minimal);
            AssertScript(left, right, script);
            int cost = script.Sum(e => e.LeftLength + e.RightLength);
            var lcs = new int[left.Length + 1, right.Length + 1];
            for (int i = left.Length - 1; i >= 0; i--)
                for (int j = right.Length - 1; j >= 0; j--)
                    lcs[i, j] = left[i] == right[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            Assert.Equal(left.Length + right.Length - 2 * lcs[0, 0], cost);
            string[] Chunk(string text) => text.Length == 0 ? Array.Empty<string>() : text.Split(',');
            var reference = DiffPlex.Differ.Instance.CreateCustomDiffs(string.Join(',', left), string.Join(',', right), false, Chunk);
            Assert.Equal(reference.DiffBlocks.Sum(b => b.DeleteCountA + b.InsertCountB), cost);
            AssertScript(left, right, SequenceDiffer.Compute(left, right, DiffStrategy.Readable));
        }
    }

    [Theory]
    [InlineData(1, 10000)]
    [InlineData(10000, 1)]
    [InlineData(2000, 2000)]
    public void UnbalancedAndRepeatedSequences_RemainValid(int n, int m)
    {
        var a = Enumerable.Range(0, n).Select(i => i % 3).ToArray();
        var b = Enumerable.Range(0, m).Select(i => (i + 1) % 3).ToArray();
        AssertScript(a, b, SequenceDiffer.Compute(a, b, DiffStrategy.Minimal));
    }

    private static void AssertScript(int[] a, int[] b, IReadOnlyList<SequenceEdit> edits)
    {
        int x = 0, y = 0;
        var result = new List<int>();
        foreach (var edit in edits)
        {
            Assert.InRange(edit.LeftStart, x, a.Length);
            Assert.InRange(edit.RightStart, y, b.Length);
            Assert.Equal(a[x..edit.LeftStart], b[y..edit.RightStart]);
            result.AddRange(a[x..edit.LeftStart]);
            result.AddRange(b[edit.RightStart..edit.RightEnd]);
            x = edit.LeftEnd; y = edit.RightEnd;
        }
        Assert.Equal(a[x..], b[y..]);
        result.AddRange(a[x..]);
        Assert.Equal(b, result);
    }
}
