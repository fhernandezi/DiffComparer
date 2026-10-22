using Avalonia.Headless.XUnit;
using System.Text;
using DiffComparer.Core;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Models;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;

namespace DiffComparer.Tests;

public class AnalysisFeatureTests
{
    private static ComparisonSnapshot Snapshot() => new(new[]
    {
        new LineMap(0, 0, "header", "header", LineStatus.Equal, 100),
        new LineMap(1, null, "old", "", LineStatus.Deleted, 0),
        new LineMap(2, 1, "middle", "middle", LineStatus.Equal, 100),
        new LineMap(3, 2, "Total: 100", "Total: 120", LineStatus.Modified, 80),
        new LineMap(4, 3, "footer", "footer", LineStatus.Equal, 100),
        new LineMap(null, 4, "", "new", LineStatus.Added, 0)
    });
    private static ViewOptions Options() => new(false, 0, true, true, true, false, false, "", false);

    [Fact]
    public void ExactComparison_DoesNotHideWhitespaceInsideStrings()
    {
        var left = new[] { "var message = \"Hello  world\";" };
        var right = new[] { "var message = \"Hello world\";" };
        Assert.Equal(LineStatus.Modified, Assert.Single(new DiffEngine { IgnoreWhitespace = false }.Compare(left, right)).Status);
        Assert.Equal(LineStatus.Equal, Assert.Single(new DiffEngine { IgnoreWhitespace = true }.Compare(left, right)).Status);
    }

    [Fact]
    public void ContextProjection_PreservesRealNumbersAndMapsVisibleChanges()
    {
        var snapshot = Snapshot();
        var view = ComparisonView.Build(snapshot, Options() with { OnlyDifferences = true }, new());
        Assert.Equal(new[] { 1, 3, 5 }, view.Rows.Where(r => !r.Gap).Select(r => r.SourceIndex));
        Assert.Equal(new[] { 0, 1, 2 }, view.VisibleBlocks);
        Assert.Equal(3, snapshot.Lines[3].LeftLine);
        Assert.Equal(2, snapshot.Lines[3].RightLine);
        Assert.Equal(3, view.Rows.Single(r => r.SourceIndex == 3).Line.LeftLine);
        Assert.True(view.SourceToVisual.ContainsKey(5));
        Assert.Contains(view.Rows, r => r.Gap);
    }

    [Fact]
    public void StatusFilter_DoesNotLeakExcludedChangesThroughContext()
    {
        var snapshot = Snapshot();
        var view = ComparisonView.Build(snapshot, Options() with { OnlyDifferences = true, Deleted = false, Modified = false, Context = 20 }, new());
        Assert.Equal(new[] { 2 }, view.VisibleBlocks);
        Assert.DoesNotContain(view.Rows, r => r.Line.Status is LineStatus.Deleted or LineStatus.Modified);
        Assert.Contains(view.Rows, r => r.Line.Status == LineStatus.Equal && !r.Gap);
        Assert.Equal(1, snapshot.Deleted);
    }

    [Fact]
    public void Search_FindsEveryOccurrenceInBothPanels_AndCanFilter()
    {
        var view = ComparisonView.Build(Snapshot(), Options() with { Search = "TOTAL", SearchOnly = true }, new());
        Assert.Equal(2, view.SearchHits.Length);
        Assert.Contains(view.SearchHits, h => h.Left);
        Assert.Contains(view.SearchHits, h => !h.Left);
        Assert.Equal(new[] { 1 }, view.VisibleBlocks);
        Assert.Single(view.Rows.Where(r => !r.Gap));
    }

    [AvaloniaFact]
    public async Task ReviewAndFilters_UpdateNavigationWithoutChangingTotals()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = "header\nold\nmiddle\nTotal: 100\nfooter", RightText = "header\nmiddle\nTotal: 120\nfooter\nnew" };
        await vm.CompareCommand.ExecuteAsync(null);
        vm.ToggleReviewedCommand.Execute(null);
        await vm.PendingViewUpdate;
        Assert.Contains("1 de 3", vm.ReviewText);
        Assert.True(vm.CurrentReviewed);
        vm.UnreviewedOnly = true;
        await vm.PendingViewUpdate;
        Assert.Equal(2, vm.View.VisibleBlocks.Length);
        Assert.False(vm.CurrentReviewed);
        Assert.Equal(3, vm.DiscrepancyCount);
        vm.ShowModified = false;
        await vm.PendingViewUpdate;
        Assert.Single(vm.View.VisibleBlocks);
        Assert.Contains("1 visibles", vm.NavigationText);
        vm.UnreviewedOnly = false;
        await vm.PendingViewUpdate;
        vm.PreviousChangeCommand.Execute(null);
        Assert.True(vm.CurrentReviewed);
    }

    [AvaloniaFact]
    public async Task RapidFilterAndSearchChanges_PublishOnlyLatestView()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService()) { LeftText = "A\nold\nB", RightText = "A\nnew\nB" };
        await vm.CompareCommand.ExecuteAsync(null);
        vm.SearchOnly = true;
        vm.SearchText = "missing";
        vm.SearchText = "old";
        vm.SearchText = "new";
        await vm.PendingViewUpdate;
        Assert.Equal("new", vm.SearchText);
        Assert.Single(vm.View.Rows.Where(r => r.Line.RightText == "new"));
        Assert.DoesNotContain(vm.View.Rows, r => r.Line.LeftText == "old");
        Assert.False(vm.IsUpdatingView);
        vm.SearchText = "missing again";
        vm.EditCommand.Execute(null);
        await vm.PendingViewUpdate;
        Assert.Empty(vm.View.Rows);
        Assert.True(vm.IsEditMode);
    }

    [AvaloniaFact]
    public async Task SearchNavigation_WrapsAndResetsAfterQueryChanges()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService()) { LeftText = "apple apple", RightText = "apple pear" };
        await vm.CompareCommand.ExecuteAsync(null);
        vm.SearchText = "apple";
        await vm.PendingViewUpdate;
        vm.FindPreviousCommand.Execute(null);
        Assert.Equal(2, vm.CurrentSearchIndex);
        vm.FindNextCommand.Execute(null);
        Assert.Equal(0, vm.CurrentSearchIndex);
        vm.SearchText = "not present";
        await vm.PendingViewUpdate;
        Assert.False(vm.FindNextCommand.CanExecute(null));
    }

    [Fact]
    public void MoveDetection_GroupsUniqueAdjacentLinesWithoutLosingSourceMapping()
    {
        var input = new[]
        {
            new LineMap(0, null, "moved first line", "", LineStatus.Deleted, 0),
            new LineMap(1, null, "moved second line", "", LineStatus.Deleted, 0),
            new LineMap(2, 0, "stays", "stays", LineStatus.Equal, 100),
            new LineMap(null, 1, "", "moved first line", LineStatus.Added, 0),
            new LineMap(null, 2, "", "moved second line", LineStatus.Added, 0)
        };
        var detected = MovedBlockDetector.Detect(input);
        Assert.Equal(1, new ComparisonSnapshot(detected).MovedBlocks);
        Assert.Equal(detected[0].MoveId, detected[4].MoveId);
        Assert.Equal(1, detected[0].MovedPartnerLine);
        Assert.Equal(0, detected[3].MovedPartnerLine);
        Assert.Null(input[0].MoveId);
    }

    [Fact]
    public void MoveDetection_DoesNotGuessDuplicateOrChangedLines()
    {
        var lines = new[]
        {
            new LineMap(0, null, "duplicate text", "", LineStatus.Deleted, 0),
            new LineMap(1, null, "duplicate text", "", LineStatus.Deleted, 0),
            new LineMap(null, 0, "", "duplicate text", LineStatus.Added, 0)
        };
        Assert.All(MovedBlockDetector.Detect(lines), l => Assert.Null(l.MoveId));
    }

    [Theory]
    [InlineData("\n", "LF")]
    [InlineData("\r\n", "CRLF")]
    [InlineData("\r", "CR")]
    [InlineData("\r\n\n", "Mixtos")]
    [InlineData("text", "Sin saltos")]
    public void LineEndingMetadata_IsAccurate(string text, string expected) => Assert.Equal(expected, TextFileContent.DetectLineEndings(text));

    [Fact]
    public async Task FileReader_RespectsBomAndReportsActualEncoding()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("España\r\nárbol")).ToArray();
        using var stream = new MemoryStream(bytes);
        var result = await TextFileReader.ReadAsync(stream, "Automático");
        Assert.Equal("España\r\nárbol", result.Text);
        Assert.Equal("utf-16", result.Encoding);
    }

    [Fact]
    public async Task FileReader_RejectsInvalidUtf8InsteadOfReplacingCharacters()
    {
        using var stream = new MemoryStream(new byte[] { 0x63, 0x61, 0x66, 0xe9 });
        await Assert.ThrowsAsync<InvalidDataException>(() => TextFileReader.ReadAsync(stream, "Automático"));
        stream.Position = 0;
        var result = await TextFileReader.ReadAsync(stream, "Windows-1252");
        Assert.Equal("café", result.Text);
    }

    [Fact]
    public async Task FileReader_RejectsMalformedUtf16EvenWithBom()
    {
        using var stream = new MemoryStream(new byte[] { 0xff, 0xfe, 0x00, 0xd8 });
        await Assert.ThrowsAsync<InvalidDataException>(() => TextFileReader.ReadAsync(stream, "Automático"));
    }

    [Fact]
    public void Search_OnVeryRepetitiveText_HasBoundedResults()
    {
        var snapshot = new ComparisonSnapshot(new[] { new LineMap(0, 0, new string('a', 60000), "", LineStatus.Deleted, 0) });
        var view = ComparisonView.Build(snapshot, Options() with { Search = "a" }, new());
        Assert.Equal(50_000, view.SearchHits.Length);
        Assert.True(view.SearchLimitReached);
        Assert.Single(view.LeftSearchHits);
    }

    [Fact]
    public void HtmlReport_EscapesInputAndIncludesReviewAndComparisonRules()
    {
        var snapshot = new ComparisonSnapshot(new[] { new LineMap(0, 0, "<script>alert(1)</script>", "&new", LineStatus.Modified, 0) }, true);
        var html = ComparisonExporter.Html(snapshot, "<left>", "right", new HashSet<int> { 0 });
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;left&gt;", html);
        Assert.Contains("1/1 revisados", html);
        Assert.Contains("ignorar diferencias de espacios", html);
    }

    [Theory]
    [InlineData("", "new\n", "@@ -0,0 +1,1 @@")]
    [InlineData("old\n", "", "@@ -1,1 +0,0 @@")]
    [InlineData("old", "new", "\\ No newline at end of file")]
    [InlineData("same", "same\n", "-same\n\\ No newline at end of file\n+same\n")]
    public void Patch_HandlesEmptyFilesAndFinalNewline(string left, string right, string expected)
    {
        var patch = ComparisonExporter.Patch(left, right, "file.txt", "file.txt");
        Assert.Contains(expected, patch);
    }

    [Fact]
    public void Patch_IsExactAndIndependentOfWhitespaceIgnoreRules()
    {
        var patch = ComparisonExporter.Patch("a  b\n", "a b\n", "f.txt", "f.txt");
        Assert.Contains("-a  b\n+a b\n", patch);
        Assert.Empty(ComparisonExporter.Patch("same\n", "same\n", "f", "f"));
    }

    private sealed class Exporter : IReportExportService
    {
        public string Content { get; private set; } = "";
        public Task<bool> SaveTextAsync(string suggestedName, string content, bool html) { Content = content; return Task.FromResult(true); }
    }

    [AvaloniaFact]
    public async Task Export_AlwaysIncludesRowsHiddenByUiFilters()
    {
        var exporter = new Exporter();
        using var vm = new MainWindowViewModel(new FakeFilePickerService(), exporter) { LeftText = "original", RightText = "replacement" };
        await vm.CompareCommand.ExecuteAsync(null);
        vm.ShowAdded = vm.ShowDeleted = vm.ShowModified = false;
        await vm.PendingViewUpdate;
        Assert.Empty(vm.View.Rows);
        await vm.ExportHtmlCommand.ExecuteAsync(null);
        Assert.Contains("original", exporter.Content);
        Assert.Contains("replacement", exporter.Content);
        await vm.ExportPatchCommand.ExecuteAsync(null);
        Assert.Contains("-original", exporter.Content);
        Assert.Contains("+replacement", exporter.Content);
    }

    [Fact]
    public void HugeChangedLines_KeepCompleteTextWithBoundedCharacterDetail()
    {
        string left = "start" + new string('a', 15000) + "end";
        string right = "start" + new string('b', 15000) + "end";
        var diff = DiffEngine.WordDiff(left, right);
        Assert.Equal(left, string.Concat(diff.Left.Select(s => s.Text)));
        Assert.Equal(right, string.Concat(diff.Right.Select(s => s.Text)));
        Assert.Contains(diff.Left, s => s.Text == "start" && !s.IsChanged);
        Assert.Contains(diff.Right, s => s.Text == "end" && !s.IsChanged);
    }
}
