using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Avalonia.VisualTree;
using DiffComparer.Core;
using DiffComparer.UI.Controls;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI;
using DiffComparer.UI.Renderers;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;

[assembly: AvaloniaTestApplication(typeof(DiffComparer.Tests.TestAppBuilder))]
namespace DiffComparer.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}

public class WindowIntegrationTests
{
    [AvaloniaFact]
    public async Task GutterArrowClick_CopiesTheHunk_AndKeyboardUndoesIt()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = "head\nold\ntail", RightText = "head\nnew\ntail" };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.CompareCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            var gutter = window.GetVisualDescendants().OfType<ChangeActionGutter>().Single();
            var button = gutter.GetVisualDescendants().OfType<Button>().Single(b =>
                b.CommandParameter is CopyRequest { Direction: CopyDirection.LeftToRight });
            Assert.True(button.IsEnabled);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            if (vm.ApplyChangeCommand.ExecutionTask is { } copy) await copy;
            Assert.Equal(vm.LeftText, vm.RightText);
            Assert.Equal(0, vm.ChangeBlockCount);
            window.FindControl<TextEditor>("RightEditor")!.TextArea.Focus();
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            await vm.PendingComparison;
            Assert.Equal("head\nnew\ntail", vm.RightText);
            vm.EditCommand.Execute(null);
            var right = window.FindControl<TextEditor>("RightEditor")!;
            right.TextArea.Focus();
            right.Document.Insert(right.Document.TextLength, "!");
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            Assert.Equal("head\nnew\ntail", vm.RightText);
            window.Width = 850; window.Height = 520;
            Dispatcher.UIThread.RunJobs();
            using var small = window.CaptureRenderedFrame();
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts)) small!.Save(Path.Combine(artifacts, "comparison-small-window.png"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FilesComparisonFilteringAndEditing_StayConnectedToEditors()
    {
        var picker = new FakeFilePickerService { NextFileContent = "A\nB" };
        var vm = new MainWindowViewModel(picker);
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.OpenLeftFileCommand.ExecuteAsync(null);
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            Assert.Equal("A\nB", left.Text);
            picker.NextFileContent = "A\nx\nB";
            await vm.OpenRightFileCommand.ExecuteAsync(null);
            Assert.Equal("A\nx\nB", right.Text);
            await vm.CompareCommand.ExecuteAsync(null);
            Assert.False(left.IsReadOnly);
            Assert.False(right.IsReadOnly);
            Assert.Same(vm.LeftDocument, left.Document);
            Assert.Same(vm.RightDocument, right.Document);
            Assert.Equal("A\nB", left.Text);
            Assert.Equal(1, vm.View.Rows[2].Line.LeftLine);
            Assert.IsType<OriginalLineNumberMargin>(left.TextArea.LeftMargins.Last());
            vm.OnlyDifferences = true;
            vm.ContextLines = 0;
            await vm.PendingViewUpdate;
            Assert.Equal("A\nB", left.Text);
            vm.EditCommand.Execute(null);
            Assert.Equal("A\nB", left.Text);
            Assert.Equal("A\nx\nB", right.Text);
            Assert.False(left.IsReadOnly);
            left.Text = "edited";
            Assert.Equal("edited", vm.LeftText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SearchKeyboardSyntaxAndWhitespace_WorkInRenderedWindow()
    {
        var vm = new MainWindowViewModel(new FakeFilePickerService())
        {
            LeftText = "class Demo\n{\n    string name = \"old\";\n}",
            RightText = "class Demo\n{\n    string name = \"new\";\n    int count = 1;\n}",
            SelectedLanguage = "C#", ShowWhitespace = true
        };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.CompareCommand.ExecuteAsync(null);
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            Assert.NotNull(left.SyntaxHighlighting);
            foreach (string language in vm.Languages.Where(l => l is not "Automático" and not "Texto"))
            {
                vm.SelectedLanguage = language;
                Assert.True(left.SyntaxHighlighting is not null, $"Falta resaltado para {language}");
            }
            vm.SelectedLanguage = "C#";
            Assert.True(left.Options.ShowSpaces);
            vm.SearchText = "name";
            await vm.PendingViewUpdate;
            vm.FocusSearchCommand.Execute(null);
            Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);
            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
            Assert.Equal(0, vm.CurrentSearchIndex);
            window.KeyReleaseQwerty(PhysicalKey.F3, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts)) { Directory.CreateDirectory(artifacts); frame.Save(Path.Combine(artifacts, "comparison-window.png")); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NavigationAndScroll_UseOriginalLineNumbers()
    {
        var source = Enumerable.Range(0, 200).Select(i => $"row {i:D4}").ToArray();
        var target = source.ToList();
        target.Insert(20, "inserted line");
        target.Insert(170, "another inserted line");
        var vm = new MainWindowViewModel(new FakeFilePickerService()) { LeftText = string.Join('\n', source), RightText = string.Join('\n', target) };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.CompareCommand.ExecuteAsync(null);
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            vm.NextChangeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(left.VerticalOffset > 0);
            Assert.True(right.VerticalOffset > 0);
            vm.OnlyDifferences = true;
            vm.ContextLines = 1;
            await vm.PendingViewUpdate;
            vm.PreviousChangeCommand.Execute(null);
            Assert.Equal("Bloque 1 de 2", vm.NavigationText);
            Assert.Equal(2, vm.View.VisibleBlocks.Length);
            Assert.True(vm.View.Rows.Length < 15);
        }
        finally { window.Close(); }
    }
}
