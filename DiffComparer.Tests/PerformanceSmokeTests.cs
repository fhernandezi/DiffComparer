using System.Diagnostics;
using System.Text.Json;
using DiffComparer.Core;
using DiffComparer.UI.Models;
using DiffComparer.UI.Services;
using Xunit.Abstractions;

namespace DiffComparer.Tests;

public class PerformanceSmokeTests
{
    private readonly ITestOutputHelper _output;
    public PerformanceSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TwentyThousandLines_KeepMappingsAndBuildFilteredView()
    {
        var left = Enumerable.Range(0, 20_000).Select(i => $"    var item{i:D6} = {i};").ToArray();
        var right = left.ToArray();
        for (int i = 100; i < right.Length; i += 400) right[i] += " // changed";
        _ = new DiffEngine { IgnoreWhitespace = false }.Compare(left[..100], right[..100]);
        var samples = new List<object>();
        for (int run = 0; run < 3; run++)
        {
            long allocated = GC.GetTotalAllocatedBytes(false);
            var watch = Stopwatch.StartNew();
            var engine = new DiffEngine { IgnoreWhitespace = false };
            var result = engine.Analyze(new TextSnapshot(string.Join('\n', left)), new TextSnapshot(string.Join('\n', right)));
            var lines = result.Lines.ToArray();
            double compareMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var snapshot = new ComparisonSnapshot(lines, canonical: result);
            var view = ComparisonView.Build(snapshot, new(true, 3, true, true, true, false, false, "changed", false), new());
            double viewMs = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(50, snapshot.Modified);
            Assert.Equal(50, snapshot.Blocks.Length);
            Assert.Equal(50, view.SearchHits.Length);
            Assert.Equal(Enumerable.Range(0, left.Length), lines.Select(l => l.LeftLine!.Value));
            samples.Add(new { compareMs, viewMs, allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated, totalLines = left.Length, visibleRows = view.Rows.Length });
            _output.WriteLine($"20.000 líneas: motor {compareMs:F1} ms; detección, índices y vista {viewMs:F1} ms; {view.Rows.Length} filas visibles");
        }
        string? path = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
        if (!string.IsNullOrEmpty(path))
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "performance.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            var cases = new[] { ("", "new\n"), ("old\n", ""), ("old", "new"), ("same", "same\n"), ("a  b\n", "a b\n"), ("α\nb\n", "α\nx\nb\n") };
            for (int i = 0; i < cases.Length; i++)
            {
                string directory = Path.Combine(path, "patch-verification", i.ToString());
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "file.txt"), cases[i].Item1, new System.Text.UTF8Encoding(false));
                File.WriteAllText(Path.Combine(directory, "expected.txt"), cases[i].Item2, new System.Text.UTF8Encoding(false));
                File.WriteAllText(Path.Combine(directory, "change.patch"), ComparisonExporter.Patch(cases[i].Item1, cases[i].Item2, "file.txt", "file.txt"), new System.Text.UTF8Encoding(false));
            }
        }
    }

    [Fact]
    public void EntirelyDifferentLargeBlock_PreservesLinesWithoutTruncatingTheResult()
    {
        var engine = new DiffEngine { IgnoreWhitespace = false, IncludeCharacterDetails = false };
        var left = Enumerable.Repeat("AAAA", 1200).ToArray();
        var right = Enumerable.Repeat("ZZZZ", 1200).ToArray();
        var result = engine.Compare(left, right);
        Assert.False(engine.UsedCoarseAlignment);
        Assert.Equal(Enumerable.Range(0, left.Length), result.Where(l => l.LeftLine.HasValue).Select(l => l.LeftLine!.Value));
        Assert.Equal(Enumerable.Range(0, right.Length), result.Where(l => l.RightLine.HasValue).Select(l => l.RightLine!.Value));
        Assert.DoesNotContain(result, l => l.Status == LineStatus.Equal);
    }
}
