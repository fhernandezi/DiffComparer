using DiffComparer.Core;
using DiffComparer.Merge;

namespace DiffComparer.Tests;

public class SectionMergeSessionTests
{
    private static SectionMergeSession Create(string original, string incoming) => new(new DiffEngine().Analyze(
        new TextSnapshot(original), new TextSnapshot(incoming), new DiffOptions(CompareLineEndings: true)));

    [Theory]
    [InlineData("", "added")]
    [InlineData("deleted", "")]
    [InlineData("a", "a\nx")]
    [InlineData("a\nx", "a")]
    [InlineData("a\r\nb\r\n", "a\nb\n")]
    [InlineData("a\n", "a")]
    public void ChoosingIncomingPreservesInsertionsDeletionsAndTerminators(string original, string incoming)
    {
        var session = Create(original, incoming);
        Assert.Equal(original, session.ResultText);
        foreach (var h in session.Comparison.Hunks) session.Choose(h.Id, MergeChoice.Right);
        Assert.Equal(incoming, session.ResultText);
        session.Undo();
        Assert.NotEqual(incoming, session.ResultText);
        session.Redo();
        Assert.Equal(incoming, session.ResultText);
    }

    [Fact]
    public void ExactRoundTripForRepeatedLinesUnicodeAndMixedEndings()
    {
        var random = new Random(785);
        string[] parts = ["a\n", "b\r\n", "\n", "🙂\r", "x", "a\n"];
        for (int i = 0; i < 300; i++)
        {
            string a = string.Concat(Enumerable.Range(0, random.Next(12)).Select(_ => parts[random.Next(parts.Length)]));
            string b = string.Concat(Enumerable.Range(0, random.Next(12)).Select(_ => parts[random.Next(parts.Length)]));
            var session = Create(a, b);
            Assert.Equal(a, session.ResultText);
            foreach (var h in session.Comparison.Hunks) session.Choose(h.Id, MergeChoice.Right);
            Assert.Equal(b, session.ResultText);
        }
    }

    [Fact]
    public void DecisionsAreIndependentAndInvalidatedWhenTheirTextChanges()
    {
        var before = Create("head\na\ncontext\nb\ntail", "head\nA\ncontext\nB\ntail");
        before.Choose(0, MergeChoice.Right);
        before.Choose(1, MergeChoice.BothLeftFirst);
        Assert.Equal("head\nA\ncontext\nb\nB\ntail", before.ResultText);
        var after = Create(before.Comparison.Left.Text, "head\nA\ncontext\nchanged\ntail");
        after.CarryDecisionsFrom(before);
        Assert.Equal(MergeChoice.Right, after.ChoiceFor(0));
        Assert.Equal(MergeChoice.Left, after.ChoiceFor(1));
        Assert.Equal("head\nA\ncontext\nb\ntail", after.ResultText);
        Assert.False(after.CanUndo);
    }

    [Fact]
    public void CheckTogglesAreIndependentAndBothBlocksHaveALineBoundary()
    {
        var session = Create("void Mine() {}", "void Theirs() {}");
        session.SetIncluded(0, incoming: true, include: true);
        Assert.Equal(MergeChoice.BothLeftFirst, session.ChoiceFor(0));
        Assert.Equal("void Mine() {}\nvoid Theirs() {}", session.ResultText);
        session.SetIncluded(0, incoming: false, include: false);
        Assert.Equal("void Theirs() {}", session.ResultText);
        session.SetIncluded(0, incoming: false, include: true);
        Assert.Equal("void Mine() {}\nvoid Theirs() {}", session.ResultText);
        session.SetIncluded(0, incoming: true, include: false);
        Assert.Equal("void Mine() {}", session.ResultText);
        session.SetIncluded(0, incoming: false, include: false);
        Assert.Equal("", session.ResultText);
    }

    [Fact]
    public void BothOrderOmissionAndHistoryAreExplicit()
    {
        var session = Create("left", "right");
        session.Choose(0, MergeChoice.BothLeftFirst);
        Assert.Equal("left\nright", session.ResultText);
        session.Choose(0, MergeChoice.BothRightFirst);
        Assert.Equal("right\nleft", session.ResultText);
        session.Choose(0, MergeChoice.Unresolved);
        Assert.Equal("", session.ResultText);
        session.Undo(); session.Choose(0, MergeChoice.Right);
        Assert.False(session.CanRedo);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Choose(5, MergeChoice.Right));
    }
}
