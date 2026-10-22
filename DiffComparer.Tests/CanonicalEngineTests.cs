using System.Globalization;
using System.Text;
using DiffComparer.Core;

namespace DiffComparer.Tests;

public class CanonicalEngineTests
{
    [Fact]
    public void GeneratedHunkCopies_ConvergeInBothDirectionsWithoutLosingSeparators()
    {
        var random = new Random(4512);
        string[] tokens = { "a", "b", "", "👩‍💻", "é", "a b", "a  b" };
        string[] endings = { "\n", "\r\n", "\r", "" };
        string Document() => string.Concat(Enumerable.Range(0, random.Next(9))
            .Select(_ => tokens[random.Next(tokens.Length)] + endings[random.Next(endings.Length)]));
        foreach (var direction in new[] { CopyDirection.LeftToRight, CopyDirection.RightToLeft })
            for (int sample = 0; sample < 150; sample++)
            {
                string a = Document(), b = Document();
                string expected = direction == CopyDirection.LeftToRight ? a : b;
                var leftId = Guid.NewGuid(); var rightId = Guid.NewGuid();
                int steps = 0;
                while (a != b)
                {
                    Assert.True(steps++ < 30, "Copying hunks must make progress");
                    var result = new DiffEngine().Analyze(new(a, leftId, steps), new(b, rightId, steps), new(CompareLineEndings: true));
                    Assert.NotEmpty(result.Hunks);
                    var edit = CopyPlanner.Plan(result, new(result.Id, 0, direction), leftId, steps, rightId, steps, false);
                    string Apply(string text) => text.Remove(edit.Range.Offset, edit.Range.Length).Insert(edit.Range.Offset, edit.ReplacementText);
                    if (direction == CopyDirection.LeftToRight) b = Apply(b); else a = Apply(a);
                }
                Assert.Equal(expected, a); Assert.Equal(expected, b);
            }
    }

    [Fact]
    public void OriginalLongLineRegression_OnlyHighlightsTwoChangedCharacters()
    {
        string left = new string('a', 2100) + "X" + new string('m', 2100) + "Y" + new string('z', 100);
        string right = new string('a', 2100) + "U" + new string('m', 2100) + "V" + new string('z', 100);
        var result = DiffEngine.WordDiff(left, right);
        Assert.Equal(2, result.Left.Where(s => s.IsChanged).Sum(s => s.Text.Length));
        Assert.Equal(2, result.Right.Where(s => s.IsChanged).Sum(s => s.Text.Length));
        Assert.True(DiffEngine.Similarity(left, right) < 100);
    }

    [Theory]
    [InlineData("😀", "😁")]
    [InlineData("e\u0301", "e\u0300")]
    [InlineData("👨‍👩‍👧‍👦 family", "👩🏽‍💻 family")]
    public void IntrablockRanges_RespectGraphemeBoundaries(string left, string right)
    {
        var diff = DiffEngine.WordDiff(left, right);
        void Validate(string text, List<WordSegment> segments)
        {
            var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
            int offset = 0;
            foreach (var segment in segments)
            {
                Assert.Contains(offset, boundaries);
                new UTF8Encoding(false, true).GetBytes(segment.Text);
                offset += segment.Text.Length;
                Assert.Contains(offset, boundaries);
            }
            Assert.Equal(text, string.Concat(segments.Select(s => s.Text)));
        }
        Validate(left, diff.Left); Validate(right, diff.Right);
    }

    [Fact]
    public void RepeatedCommonIsland_IsRetainedBetweenLargeChanges()
    {
        var left = Enumerable.Repeat("A", 300).Concat(Enumerable.Repeat("COMMON", 20)).Concat(Enumerable.Repeat("B", 300)).ToArray();
        var right = Enumerable.Repeat("X", 300).Concat(Enumerable.Repeat("COMMON", 20)).Concat(Enumerable.Repeat("Y", 300)).ToArray();
        var result = new DiffEngine { IgnoreWhitespace = false }.Compare(left, right);
        Assert.Equal(20, result.Count(l => l.Status == LineStatus.Equal));
    }

    [Fact]
    public void ReplacementPairing_OptimizesBothPairsTogether()
    {
        var rows = new DiffEngine { IgnoreWhitespace = false }.Compare(new[] { "abcdef", "abcdeg" }, new[] { "abcxxf", "abcdee" });
        Assert.Equal(2, rows.Count);
        Assert.All(rows, line => Assert.Equal(LineStatus.Modified, line.Status));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "\n")]
    [InlineData("\n", "")]
    [InlineData("A", "A\n")]
    [InlineData("A\n", "A")]
    [InlineData("A", "A\nB")]
    [InlineData("A\nB", "A")]
    [InlineData("A\r\nB\r\n", "A\nB\n")]
    [InlineData("  A  B\r\n", "A B\n")]
    [InlineData("\r\nA\rB\n", "\nA\nB")]
    public void RawEdits_ReconstructExactOriginalsAcrossPolicies(string left, string right)
    {
        foreach (bool whitespace in new[] { false, true })
        foreach (bool endings in new[] { false, true })
        {
            var result = new DiffEngine().Analyze(new(left), new(right), new(whitespace, endings));
            Assert.Equal(right, result.ReconstructRight());
            Assert.Equal(left, new DiffEngine().Analyze(new(right), new(left), new(whitespace, endings)).ReconstructRight());
        }
    }

    [Fact]
    public void SplittingOneLine_RefinesTheWholeBlockWithoutMarkingUnchangedTokens()
    {
        var result = new DiffEngine().Analyze(new("Call(a, b);"), new("Call(\n    a,\n    b\n);"));
        Assert.Equal(0, result.RawEdits.Sum(e => e.Left.Length));
        Assert.Equal(result.Right.Text, result.ReconstructRight());
    }

    [Fact]
    public void FinalNewline_IsSignificant_WhileStyleIsConfigurable()
    {
        var engine = new DiffEngine();
        var newline = engine.Analyze(new("A"), new("A\n"));
        Assert.Single(newline.Hunks);
        Assert.True(Assert.Single(newline.Lines).HasTerminatorChange);
        Assert.Empty(engine.Analyze(new("A\r\n"), new("A\n")).Hunks);
        Assert.Single(engine.Analyze(new("A\r\n"), new("A\n"), new(CompareLineEndings: true)).Hunks);
    }

    [Fact]
    public void CopyPlanner_RejectsStaleVersionsAndSnapshots()
    {
        var result = new DiffEngine().Analyze(new("A"), new("B"));
        var request = new CopyRequest(result.Id, 0, CopyDirection.LeftToRight);
        Assert.Throws<InvalidOperationException>(() => CopyPlanner.Plan(result, request,
            result.Left.DocumentId, result.Left.Version, result.Right.DocumentId, result.Right.Version + 1));
        Assert.Throws<InvalidOperationException>(() => CopyPlanner.Plan(result, request with { SnapshotId = Guid.NewGuid() },
            result.Left.DocumentId, result.Left.Version, result.Right.DocumentId, result.Right.Version));
    }

    [Theory]
    [InlineData("A", "A\nB")]
    [InlineData("A\nB", "A")]
    [InlineData("", "\n")]
    [InlineData("A", "A\n")]
    [InlineData("head\r\nold\r\nend", "head\r\nnew\r\nend")]
    public void Copies_InsertDeleteAndReplaceExactlyInBothDirections(string left, string right)
    {
        foreach (var direction in Enum.GetValues<CopyDirection>())
        {
            string a = left, b = right;
            for (int iteration = 0; iteration < 30; iteration++)
            {
                var result = new DiffEngine().Analyze(new(a), new(b), new(CompareLineEndings: true));
                if (result.Hunks.Length == 0) break;
                var request = new CopyRequest(result.Id, 0, direction);
                var edit = CopyPlanner.Plan(result, request, result.Left.DocumentId, result.Left.Version,
                    result.Right.DocumentId, result.Right.Version, false);
                string target = direction == CopyDirection.LeftToRight ? b : a;
                Assert.Equal(edit.ExpectedText, target.Substring(edit.Range.Offset, edit.Range.Length));
                string changed = target[..edit.Range.Offset] + edit.ReplacementText + target[edit.Range.End..];
                if (direction == CopyDirection.LeftToRight) b = changed; else a = changed;
            }
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void GeneratedDocuments_ReconstructAcrossUnicodeWhitespaceAndMixedTerminators()
    {
        var random = new Random(999);
        var contents = new[] { "A", "B", "", "same", "repeat", "  A B", "A  B", "😀", "e\u0301" };
        var endings = new[] { "\n", "\r\n", "\r" };
        string Document()
        {
            var text = new StringBuilder();
            int count = random.Next(12);
            for (int i = 0; i < count; i++)
            {
                text.Append(contents[random.Next(contents.Length)]);
                if (i < count - 1 || random.Next(2) == 0) text.Append(endings[random.Next(endings.Length)]);
            }
            return text.ToString();
        }
        for (int i = 0; i < 500; i++)
        {
            string left = Document(), right = Document();
            var options = new DiffOptions(IgnoreWhitespace: random.Next(2) == 0, CompareLineEndings: random.Next(2) == 0);
            var result = new DiffEngine().Analyze(new(left), new(right), options);
            Assert.Equal(right, result.ReconstructRight());
        }
    }
}
