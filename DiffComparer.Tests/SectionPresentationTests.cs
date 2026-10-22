using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DiffComparer.Core;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.Controls;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;
using SkiaSharp;

namespace DiffComparer.Tests;

public class SectionPresentationTests
{
    [AvaloniaTheory]
    [InlineData(true, 13)]
    [InlineData(false, 13)]
    [InlineData(true, 19)]
    public async Task MultilineSection_HasOneOutlineAndBracket_AndArrowsCenterOnSource(bool insertRight, double sourceFontSize)
    {
        string prefix = string.Join('\n', Enumerable.Range(1, 8).Select(i => $"same {i}")) + "\n";
        string original = prefix + "tail";
        string expanded = prefix + "\nfirst inserted line\nsecond inserted line\nthird inserted line\ntail";
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = insertRight ? original : expanded, RightText = insertRight ? expanded : original };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            var source = window.FindControl<TextEditor>(insertRight ? "RightEditor" : "LeftEditor")!;
            source.FontSize = sourceFontSize;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                frame.Save(Path.Combine(artifacts, insertRight ? $"section-right-{sourceFontSize}.png" : "section-left.png"));
            }
            var view = source.TextArea.TextView;
            var first = view.VisualLines.Single(v => v.FirstDocumentLine.LineNumber == 9);
            var last = view.VisualLines.Single(v => v.FirstDocumentLine.LineNumber == 12);
            var origin = view.TranslatePoint(default, window)!.Value;
            double top = origin.Y + first.VisualTop - view.VerticalOffset;
            double bottom = origin.Y + last.VisualTop + last.Height - view.VerticalOffset;
            var gutter = window.FindControl<ChangeActionGutter>("ChangeGutter")!;
            var button = gutter.GetVisualDescendants().OfType<Button>().Single(b =>
                b.CommandParameter is CopyRequest request && request.Direction ==
                (insertRight ? CopyDirection.RightToLeft : CopyDirection.LeftToRight));
            var center = button.TranslatePoint(new Point(0, button.Bounds.Height / 2), window)!.Value;
            Assert.InRange(Math.Abs(center.Y - (top + bottom) / 2), 0, 1);
            Assert.Contains("líneas 9–12", ToolTip.GetTip(button)!.ToString());

            using var stream = new MemoryStream();
            frame.Save(stream);
            using var bitmap = SKBitmap.Decode(stream.ToArray());
            double scale = bitmap.Width / window.Bounds.Width;
            SKColor Pixel(double x, double y) => bitmap.GetPixel((int)Math.Round(x * scale), (int)Math.Round(y * scale));
            // No blue horizontal borders at any of the three internal line boundaries.
            foreach (int line in new[] { 10, 11, 12 })
            {
                double y = origin.Y + view.VisualLines.Single(v => v.FirstDocumentLine.LineNumber == line).VisualTop - view.VerticalOffset;
                foreach (double offset in new[] { -1.0, 0.0, 1.0 })
                {
                    var color = Pixel(origin.X + 200, y + offset);
                    Assert.True((insertRight ? color.Green > color.Red + 15 : color.Red > color.Green + 15), $"Unexpected seam at line {line}: {color}");
                }
            }
            var gutterOrigin = gutter.TranslatePoint(default, window)!.Value;
            double bracketX = gutterOrigin.X + (insertRight ? gutter.Bounds.Width - 5 : 5);
            var bracket = Pixel(bracketX, top + 4);
            Assert.True((insertRight ? bracket.Green > bracket.Red + 15 : bracket.Red > bracket.Green + 15), $"Missing source bracket: {bracket}");

            button.Command!.Execute(button.CommandParameter);
            await vm.ApplyChangeCommand.ExecutionTask!;
            Assert.Equal(expanded, vm.LeftText);
            Assert.Equal(expanded, vm.RightText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SectionStartingAboveViewport_StillShowsCopyAction_AfterIndependentScroll()
    {
        string prefix = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"same {i}")) + "\n";
        string block = string.Join('\n', Enumerable.Range(1, 70).Select(i => $"inserted {i}")) + "\n";
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = prefix + "tail", RightText = prefix + block + "tail" };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
            using var initialFrame = window.CaptureRenderedFrame();
            left.ScrollToVerticalOffset(0);
            right.ScrollToLine(80);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            var gutter = window.FindControl<ChangeActionGutter>("ChangeGutter")!;
            var buttons = gutter.GetVisualDescendants().OfType<Button>().ToArray();
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts)) frame!.Save(Path.Combine(artifacts, "section-scrolled.png"));
            Assert.True(buttons.Length == 2, $"Buttons {buttons.Length}, current {vm.IsResultCurrent}, hunks {vm.CanonicalResult!.Hunks.Length}, visible {vm.View.FullyVisibleHunks.Count}, offset {right.VerticalOffset}, visual {right.TextArea.TextView.VisualLines.First().FirstDocumentLine.LineNumber}–{right.TextArea.TextView.VisualLines.Last().LastDocumentLine.LineNumber}, ranges {string.Join(';', vm.CanonicalResult.Hunks.Select(h => h.RightLines))}");
            Assert.All(buttons, b => Assert.True(b.IsEnabled));
            Assert.Equal(0, left.VerticalOffset);
            Assert.True(right.TextArea.TextView.VisualLines[0].FirstDocumentLine.LineNumber > 31);
        }
        finally { window.Close(); }
    }
}
