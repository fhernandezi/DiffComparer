using DiffComparer.Merge;

namespace DiffComparer.Tests;

public class ThreeWayMergeTests
{
    [Theory]
    [InlineData("", "", "")]
    [InlineData("", "insert", "")]
    [InlineData("a\nb\n", "a\n", "a\nb\n")]
    [InlineData("a\r\nb\r\n", "a\nb\n", "a\r\nb\r\n")]
    [InlineData("a\n", "a", "a\n")]
    public void OneChangedSideIsPreservedExactly(string basis, string left, string right)
    {
        var session = Session(basis, left, right);
        Assert.Equal(0, session.UnresolvedCount);
        Assert.Equal(left, session.Complete());
    }

    [Fact]
    public void IndependentAndAdjacentEditsCombine()
    {
        Assert.Equal("A\nB\nc\n", Session("a\nb\nc\n", "A\nb\nc\n", "a\nB\nc\n").Complete());
        Assert.Equal("a\nx\nB\nc\n", Session("a\nb\nc\n", "a\nx\nb\nc\n", "a\nB\nc\n").Complete());
        Assert.Equal("a\nB\nx\nc\n", Session("a\nb\nc\n", "a\nb\nx\nc\n", "a\nB\nc\n").Complete());
    }

    [Fact]
    public void IdenticalOverlappingChangesAreAutomatic()
    {
        Assert.Equal("new", Session("old", "new", "new").Complete());
        Assert.Equal("insert", Session("", "insert", "insert").Complete());
        Assert.Equal("", Session("old", "", "").Complete());
    }

    [Theory]
    [InlineData("old", "left", "right")]
    [InlineData("", "left", "right")]
    [InlineData("a\nb\n", "a\n", "a\nB\n")]
    public void CompetingChangesRequireExplicitDecision(string basis, string left, string right)
    {
        var session = Session(basis, left, right);
        Assert.Equal(1, session.UnresolvedCount);
        Assert.Throws<InvalidOperationException>(() => session.Complete());
        var conflict = Assert.Single(session.Plan.Blocks.Where(b => b.IsConflict));
        session.Resolve(conflict.Id, MergeChoice.Left);
        Assert.Equal(left, session.Complete());
        session.Undo();
        Assert.Equal(1, session.UnresolvedCount);
        session.Redo();
        Assert.Equal(left, session.Complete());
        session.Resolve(conflict.Id, MergeChoice.Right);
        Assert.Equal(right, session.Complete());
    }

    [Fact]
    public void TransitiveOverlapsFormOneConflictWithSurroundingText()
    {
        var session = Session("head\na\nb\nc\ntail\n", "head\nA\nb\nC\ntail\n", "head\nwhole\ntail\n");
        var conflict = Assert.Single(session.Plan.Blocks.Where(b => b.IsConflict));
        Assert.Equal("a\nb\nc\n", conflict.BaseText);
        session.Resolve(conflict.Id, MergeChoice.Left);
        Assert.Equal("head\nA\nb\nC\ntail\n", session.Complete());
    }

    [Fact]
    public void BothOrdersBaseManualAndUndoAreExplicit()
    {
        var session = Session("base", "left", "right");
        int id = Assert.Single(session.Plan.Blocks).Id;
        session.Resolve(id, MergeChoice.BothLeftFirst);
        Assert.Equal("leftright", session.Complete());
        session.Resolve(id, MergeChoice.BothRightFirst);
        Assert.Equal("rightleft", session.Complete());
        session.Resolve(id, MergeChoice.Base);
        Assert.Equal("base", session.Complete());
        session.Resolve(id, MergeChoice.Manual, "custom");
        Assert.Equal("custom", session.Complete());
        session.Undo();
        session.Resolve(id, MergeChoice.Left);
        Assert.False(session.CanRedo);
        Assert.Throws<ArgumentException>(() => session.Resolve(999, MergeChoice.Left));
    }

    [Fact]
    public void ExactIdentityPropertiesHoldForRepeatedLinesAndMixedTerminators()
    {
        var random = new Random(52);
        string[] tokens = ["a\n", "b\r\n", "a\r", "\n", "🙂\n", "x"];
        for (int i = 0; i < 250; i++)
        {
            string basis = string.Concat(Enumerable.Range(0, random.Next(20)).Select(_ => tokens[random.Next(tokens.Length)]));
            string side = string.Concat(Enumerable.Range(0, random.Next(20)).Select(_ => tokens[random.Next(tokens.Length)]));
            Assert.Equal(side, Session(basis, basis, side).Complete());
            Assert.Equal(side, Session(basis, side, basis).Complete());
            Assert.Equal(side, Session(basis, side, side).Complete());
        }
    }

    [Fact]
    public void IndependentSessionsDoNotShareDecisionsAndCancellationIsSupported()
    {
        var first = Session("a", "b", "c");
        var second = Session("a", "b", "c");
        first.Resolve(0, MergeChoice.Left);
        Assert.Equal(1, second.UnresolvedCount);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ThreeWayMerge.Compute("a", "b", "c", cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => ThreeWayMerge.Compute("", "", "", cancellation.Token));
    }

    [Fact]
    public void MultipleConflictsKeepAutomaticChangesAndResolveIndependently()
    {
        var session = Session("head\na\nseparator\nb\ntail\n", "HEAD\nA\nseparator\nB\ntail\n",
            "head\nother A\nseparator\nother B\ntail\n");
        var conflicts = session.Plan.Blocks.Where(b => b.IsConflict).ToArray();
        Assert.Equal(2, conflicts.Length);
        session.Resolve(conflicts[0].Id, MergeChoice.Left);
        Assert.Equal(1, session.UnresolvedCount);
        session.Resolve(conflicts[1].Id, MergeChoice.Right);
        // The first left change overlaps the right change at a: it is one conflict region.
        Assert.Equal("HEAD\nA\nseparator\nother B\ntail\n", session.Complete());
    }

    private static MergeSession Session(string basis, string left, string right) => new(ThreeWayMerge.Compute(basis, left, right));
}
