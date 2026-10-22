using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;

namespace DiffComparer.Tests;

public class LiveComparisonTests
{
    [AvaloniaFact]
    public async Task TypingOnBothSides_RecomparesWithoutReplacingDocumentsOrMovingCaret()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = "header\nold value\ntail", RightText = "header\nnew value\ntail" };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            await vm.PendingComparison;
            Assert.Equal(1, vm.ModifiedCount);
            Assert.False(left.IsReadOnly);
            Assert.False(right.IsReadOnly);

            // Insert a line on one side: decorations must use each source's own line numbers.
            left.Document.Insert(0, "inserted\n");
            right.Document.Insert(right.Document.TextLength, "!");
            right.Select(7, 3);
            int caret = right.CaretOffset;
            await vm.PendingComparison;
            Assert.True(vm.IsResultCurrent);
            Assert.Same(vm.LeftDocument, left.Document);
            Assert.Same(vm.RightDocument, right.Document);
            Assert.Equal("inserted\nheader\nold value\ntail", left.Text);
            Assert.Equal("header\nnew value\ntail!", right.Text);
            Assert.Equal(caret, right.CaretOffset);
            Assert.Equal(7, right.SelectionStart);
            Assert.Equal(3, right.SelectionLength);

            right.TextArea.Focus();
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            await vm.PendingComparison;
            Assert.Equal("header\nnew value\ntail", right.Text);

            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                frame.Save(Path.Combine(artifacts, "live-comparison.png"));
            }

            right.Document.Text = left.Text;
            await vm.PendingComparison;
            Assert.True(vm.IsResultCurrent);
            Assert.Equal(0, vm.DiscrepancyCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RapidEditsAndOptions_PublishLatestInputAutomatically()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService());
        vm.LeftText = "obsolete";
        var obsolete = vm.PendingComparison;
        vm.LeftText = "a  b";
        vm.RightText = "a b";
        await vm.PendingComparison;
        await obsolete;
        Assert.True(vm.IsResultCurrent);
        Assert.Equal(1, vm.DiscrepancyCount);
        vm.IgnoreWhitespace = true;
        await vm.PendingComparison;
        Assert.True(vm.IsResultCurrent);
        Assert.Equal(0, vm.DiscrepancyCount);
    }

    [AvaloniaFact]
    public async Task Dispose_CancelsScheduledComparison()
    {
        var vm = new MainWindowViewModel(new FakeFilePickerService()) { LeftText = "pending" };
        vm.Dispose();
        await vm.PendingComparison;
        Assert.Null(vm.CanonicalResult);
    }
}
