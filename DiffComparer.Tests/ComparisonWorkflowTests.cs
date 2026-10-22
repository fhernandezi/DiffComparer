using Avalonia.Headless.XUnit;
using DiffComparer.Core;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;

namespace DiffComparer.Tests;

public class ComparisonWorkflowTests
{
    private static MainWindowViewModel Create(string left = "", string right = "") =>
        new(new FakeFilePickerService()) { LeftText = left, RightText = right };

    [AvaloniaFact]
    public async Task Compare_CountsLinesAndBlocks_AndNavigationWraps()
    {
        var vm = Create("start\nold\nmiddle\nTotal: 100\nend",
            "start\nmiddle\nTotal: 120\nend\nnew");
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.AddedCount);
        Assert.Equal(1, vm.DeletedCount);
        Assert.Equal(1, vm.ModifiedCount);
        Assert.Equal(3, vm.DiscrepancyCount);
        Assert.Equal(3, vm.ChangeBlockCount);
        Assert.Equal("Bloque 1 de 3", vm.NavigationText);
        var first = vm.CurrentChangeIndex;
        vm.NextChangeCommand.Execute(null);
        Assert.Equal("Bloque 2 de 3", vm.NavigationText);
        vm.NextChangeCommand.Execute(null);
        Assert.Equal("Bloque 3 de 3", vm.NavigationText);
        vm.NextChangeCommand.Execute(null);
        Assert.Equal(first, vm.CurrentChangeIndex);
        vm.PreviousChangeCommand.Execute(null);
        Assert.Equal("Bloque 3 de 3", vm.NavigationText);
    }

    [AvaloniaFact]
    public async Task Compare_AdjacentChangesFormOneBlock()
    {
        var vm = Create("start\nend", "start\nx\ny\nend");
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.DiscrepancyCount);
        Assert.Equal(1, vm.ChangeBlockCount);
    }

    [AvaloniaTheory]
    [InlineData("", "", 0, 0)]
    [InlineData("", "A\nB", 2, 0)]
    [InlineData("A\nB", "", 0, 2)]
    [InlineData("A\r\nB", "A\nB", 0, 0)]
    public async Task Compare_HandlesEmptyDocumentsAndLineEndings(string left, string right, int added, int deleted)
    {
        var vm = Create(left, right);
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(added, vm.AddedCount);
        Assert.Equal(deleted, vm.DeletedCount);
        Assert.False(vm.IsEditMode);
        if (added + deleted == 0)
        {
            Assert.Equal(-1, vm.CurrentChangeIndex);
            Assert.False(vm.NextChangeCommand.CanExecute(null));
            Assert.Equal("Sin discrepancias", vm.NavigationText);
        }
    }

    [AvaloniaFact]
    public async Task Edit_RestoresOriginalTextsAndClearsNavigation()
    {
        var vm = Create("A\nB", "A\nx\nB");
        await vm.CompareCommand.ExecuteAsync(null);
        vm.EditCommand.Execute(null);
        Assert.Equal("A\nB", vm.LeftText);
        Assert.Equal("A\nx\nB", vm.RightText);
        Assert.Empty(vm.DiffLines);
        Assert.Equal(-1, vm.CurrentChangeIndex);
        Assert.False(vm.NextChangeCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task OpenFile_AfterComparison_ClearsOldResultsAndLoadsEmptyFile()
    {
        var picker = new FakeFilePickerService { NextFileContent = "" };
        var vm = new MainWindowViewModel(picker) { LeftText = "A", RightText = "B" };
        await vm.CompareCommand.ExecuteAsync(null);
        await vm.OpenLeftFileCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditMode);
        Assert.Empty(vm.LeftText);
        Assert.Equal("B", vm.RightText);
        Assert.Equal(0, vm.DiscrepancyCount);
    }

    private sealed class FailingPicker : IFilePickerService
    {
        public Task<string?> OpenTextFileAsync() => throw new IOException("Acceso denegado");
    }

    [AvaloniaFact]
    public async Task OpenFile_WhenReadFails_PreservesTextAndShowsError()
    {
        var vm = new MainWindowViewModel(new FailingPicker()) { LeftText = "original" };
        await vm.OpenLeftFileCommand.ExecuteAsync(null);
        Assert.Equal("original", vm.LeftText);
        Assert.Contains("No se pudo abrir", vm.StatsText);
    }

    [AvaloniaFact]
    public void VisualDocument_InsertsGhostRowsWithoutChangingSource()
    {
        var lines = new DiffEngine().Compare(new[] { "A", "B" }, new[] { "A", "x", "B" });
        var document = VisualDiffBuilder.Build(lines);
        Assert.Equal($"A{Environment.NewLine}{Environment.NewLine}B", document.LeftText);
        Assert.Equal($"A{Environment.NewLine}x{Environment.NewLine}B", document.RightText);
        Assert.True(document.Lines[1].IsLeftGhost);
    }

    [AvaloniaFact]
    public void Compare_ReplacementsPreserveOrderOnBothSides()
    {
        var engine = new DiffEngine();
        var left = new[] { "remove", "abc1", "xyz1" };
        var right = new[] { "xyz2", "abc2" };
        var result = engine.Compare(left, right);
        Assert.Equal(Enumerable.Range(0, left.Length), result.Where(x => x.LeftLine.HasValue).Select(x => x.LeftLine!.Value));
        Assert.Equal(Enumerable.Range(0, right.Length), result.Where(x => x.RightLine.HasValue).Select(x => x.RightLine!.Value));
    }

    [AvaloniaFact]
    public void Compare_WithRepeatedAndReorderedLines_PreservesEverySourceLineInOrder()
    {
        var random = new Random(42);
        var vocabulary = new[] { "", "A", "B", "abc1", "abc2", "abc3", "xyz1", "xyz2", "long repeated text" };
        for (int sample = 0; sample < 200; sample++)
        {
            var left = Enumerable.Range(0, random.Next(15)).Select(_ => vocabulary[random.Next(vocabulary.Length)]).ToArray();
            var right = Enumerable.Range(0, random.Next(15)).Select(_ => vocabulary[random.Next(vocabulary.Length)]).ToArray();
            var result = new DiffEngine().Compare(left, right);
            Assert.Equal(Enumerable.Range(0, left.Length), result.Where(x => x.LeftLine.HasValue).Select(x => x.LeftLine!.Value));
            Assert.Equal(Enumerable.Range(0, right.Length), result.Where(x => x.RightLine.HasValue).Select(x => x.RightLine!.Value));
            Assert.Equal(left, result.Where(x => x.LeftLine.HasValue).Select(x => x.LeftText));
            Assert.Equal(right, result.Where(x => x.RightLine.HasValue).Select(x => x.RightText));
        }
    }

    [AvaloniaFact]
    public async Task Compare_CanCancelWorkAlreadyRunning()
    {
        using var cancellation = new CancellationTokenSource();
        var lines = Enumerable.Range(0, 30000).Select(i => $"original line {i}").ToArray();
        var other = Enumerable.Range(0, 30000).Select(i => $"different content {i}").ToArray();
        var work = Task.Run(() => new DiffEngine(cancellation.Token).Compare(lines, other));
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await work.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [AvaloniaFact]
    public void CharacterComparison_ObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DiffEngine.WordDiff("abc", "xyz", cancellation.Token));
    }
}
