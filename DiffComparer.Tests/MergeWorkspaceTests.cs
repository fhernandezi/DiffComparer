using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using DiffComparer.Core;
using DiffComparer.Merge;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Merge;
using DiffComparer.UI.Models;
using DiffComparer.UI.Renderers;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;
using DiffComparer.Tests.Fakes;

namespace DiffComparer.Tests;

public class MergeWorkspaceTests
{
    private sealed class Files : IFilePickerService, ITextFileSaveService
    {
        public TextFileContent? Saved { get; private set; }
        public string? NextText { get; set; }
        public Task<string?> OpenTextFileAsync() => Task.FromResult(NextText);
        public Task<TextFileContent?> SaveFileAsAsync(TextFileContent content)
        { Saved = content; return Task.FromResult<TextFileContent?>(content with { Path = "result.txt" }); }
    }
    private static void Choose(MergeWorkspaceViewModel vm, int id, bool incoming, bool include = true) =>
        vm.ChooseCommand.Execute(new SectionRequest(vm.Session!.Id, id, incoming, include));

    [AvaloniaFact]
    public async Task OnChangeComparesBothInputsAndChecksApplyOneSectionWithoutChangingSources()
    {
        var files = new Files();
        using var vm = new MergeWorkspaceViewModel(files, files);
        vm.LeftDocument.Text = "header\nold A\ncontext\nold B\nend\n";
        vm.RightDocument.Text = "header\nnew A\ncontext\nnew B\nend\n";
        Assert.False(vm.CanSave);
        await vm.PendingComparison;
        Assert.Equal(2, vm.Comparison!.Hunks.Length);
        Assert.Equal(vm.LeftDocument.Text, vm.ResultDocument.Text);
        Choose(vm, 1, true);
        Assert.Equal("header\nold A\ncontext\nold B\nnew B\nend\n", vm.ResultDocument.Text);
        Assert.Equal(MergeChoice.Left, vm.Session!.ChoiceFor(0));
        Assert.Equal(MergeChoice.BothLeftFirst, vm.Session.ChoiceFor(1));
        vm.UndoCommand.Execute(null);
        Assert.Equal(vm.LeftDocument.Text, vm.ResultDocument.Text);
        vm.RedoCommand.Execute(null);
        Assert.Contains("new B", vm.ResultDocument.Text);
        Choose(vm, 1, true, false);
        Assert.Equal(vm.LeftDocument.Text, vm.ResultDocument.Text);
        Choose(vm, 0, false, false);
        Assert.Equal("header\ncontext\nold B\nend\n", vm.ResultDocument.Text);
        Assert.Contains("old A", vm.LeftDocument.Text);
        Assert.Contains("new A", vm.RightDocument.Text);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(vm.ResultDocument.Text, files.Saved!.Text);
    }

    [AvaloniaFact]
    public async Task RapidEditsInvalidateOldChecksAndPublishOnlyLatestComparison()
    {
        var files = new Files();
        using var vm = new MergeWorkspaceViewModel(files, files);
        vm.LeftDocument.Text = "head\na\ntail\n";
        vm.RightDocument.Text = "head\nb\ntail\n";
        await vm.PendingComparison;
        var old = new SectionRequest(vm.Session!.Id, 0, true, true);
        vm.RightDocument.Text = "head\nc\ntail\n";
        Assert.False(vm.CanChoose);
        vm.ChooseCommand.Execute(old);
        vm.RightDocument.Text = "head\nlatest\ntail\n";
        await vm.PendingComparison;
        Assert.Equal("head\nlatest\ntail\n", vm.Comparison!.Right.Text);
        vm.ChooseCommand.Execute(old);
        Assert.Equal(vm.LeftDocument.Text, vm.ResultDocument.Text);
        Choose(vm, 0, true);
        Assert.Equal("head\na\nlatest\ntail\n", vm.ResultDocument.Text);
        vm.RightDocument.Text = "head\nlatest\ntail\nunrelated";
        await vm.PendingComparison;
        Assert.Contains("latest", vm.ResultDocument.Text); // unchanged unique section keeps its decision
    }

    [AvaloniaFact]
    public async Task RealMarginClicksSelectDifferentSectionsAndRecalculateWhileTyping()
    {
        var files = new Files();
        var vm = new MergeWorkspaceViewModel(files, files);
        var window = new MergeWindow(vm) { RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            vm.LeftDocument.Text = "function greet() {\n    const name = \"Ana\";\n    const separator = \" / \";\n    return \"Hola \" + name;\n}\n";
            vm.RightDocument.Text = "function greet() {\n    const name = \"María\";\n    const separator = \" / \";\n    return \"Bienvenida \" + name;\n}\n";
            await vm.PendingComparison;
            Dispatcher.UIThread.RunJobs();
            using var initial = window.CaptureRenderedFrame();
            var left = window.FindControl<TextEditor>("LeftInput")!;
            var right = window.FindControl<TextEditor>("RightInput")!;
            var gutter = window.FindControl<MergeSectionGutter>("RightGutter")!;
            Assert.False(left.IsReadOnly);
            Assert.False(right.IsReadOnly);
            Assert.Equal(0, left.SelectionLength);
            Assert.Equal(2, gutter.GetVisualDescendants().OfType<CheckBox>().Count());
            Assert.Contains(right.TextArea.TextView.BackgroundRenderers, r => r is DiffLineBackgroundRenderer);
            Assert.Contains(right.TextArea.TextView.LineTransformers, r => r is DiffWordColorizer);
            async Task Click(CheckBox check)
            {
                var position = check.TranslatePoint(new Point(10, check.Bounds.Height / 2), window)!.Value;
                window.MouseDown(position, MouseButton.Left);
                window.MouseUp(position, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                await Task.Yield();
            }
            var second = gutter.GetVisualDescendants().OfType<CheckBox>().Single(c => (int)c.Tag! == 1);
            await Click(second);
            Assert.Contains("const name = \"Ana\"", vm.ResultDocument.Text);
            Assert.Contains("Bienvenida", vm.ResultDocument.Text);
            Assert.True(second.IsChecked);
            var leftSecond = window.FindControl<MergeSectionGutter>("LeftGutter")!.GetVisualDescendants().OfType<CheckBox>().Single(c => (int)c.Tag! == 1);
            Assert.True(leftSecond.IsChecked);
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            Dispatcher.UIThread.RunJobs();
            using var mixed = window.CaptureRenderedFrame();
            var section = vm.Comparison!.Hunks[1];
            double textTop = right.TextArea.TextView.TranslatePoint(default, window)!.Value.Y
                + right.TextArea.TextView.GetVisualTopByDocumentLine(section.RightLines.Start + 1) - right.VerticalOffset;
            double checkCenter = second.TranslatePoint(new Point(0, second.Bounds.Height / 2), window)!.Value.Y;
            Assert.InRange(checkCenter, textTop - 1, textTop + right.TextArea.TextView.DefaultLineHeight + 1);
            right.Document.Replace(right.Document.GetLineByNumber(2).Offset, right.Document.GetLineByNumber(2).Length, "    const name = \"Pedro\";");
            Assert.Empty(gutter.GetVisualDescendants().OfType<CheckBox>());
            await vm.PendingComparison;
            Assert.Contains("Pedro", vm.Comparison!.Right.Text);
            Dispatcher.UIThread.RunJobs();
            var first = gutter.GetVisualDescendants().OfType<CheckBox>().Single(c => (int)c.Tag! == 0);
            await Click(first);
            Assert.Contains("const name = \"Ana\";\n    const name = \"Pedro\";", vm.ResultDocument.Text);
            Assert.Contains("return \"Hola \" + name;\n    return \"Bienvenida \" + name;", vm.ResultDocument.Text);
            await Task.Delay(150);
            Dispatcher.UIThread.RunJobs();
            using var selected = window.CaptureRenderedFrame();
            window.Width = 900; window.Height = 700;
            window.Typography.Zoom = 120;
            Dispatcher.UIThread.RunJobs();
            using var compact = window.CaptureRenderedFrame();
            var lp = window.FindControl<Border>("LeftPanel")!;
            var rp = window.FindControl<Border>("RightPanel")!;
            var bp = window.FindControl<Border>("ResultPanel")!;
            Assert.Equal(lp.TranslatePoint(default, window)!.Value.Y, rp.TranslatePoint(default, window)!.Value.Y);
            Assert.True(bp.TranslatePoint(default, window)!.Value.Y >= lp.TranslatePoint(default, window)!.Value.Y + lp.Bounds.Height);
            if (artifacts is not null)
            {
                Directory.CreateDirectory(artifacts);
                initial?.Save(Path.Combine(artifacts, "merge-live-original.png"));
                mixed?.Save(Path.Combine(artifacts, "merge-live-mixed.png"));
                selected?.Save(Path.Combine(artifacts, "merge-live-selected.png"));
                compact?.Save(Path.Combine(artifacts, "merge-live-compact.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task IndependentMarginChecksKeepBothMethodsAndRemoveOnlyTheUncheckedSide()
    {
        var files = new Files();
        var vm = new MergeWorkspaceViewModel(files, files);
        var window = new MergeWindow(vm) { RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            vm.LeftDocument.Text = "class Example\n{\n    void Mine() { RunMine(); }\n}\n";
            vm.RightDocument.Text = "class Example\n{\n    void Teammate() { RunTheirs(); }\n}\n";
            await vm.PendingComparison;
            Dispatcher.UIThread.RunJobs();
            using var initial = window.CaptureRenderedFrame();
            Assert.Single(vm.Comparison!.Hunks);
            var left = window.FindControl<MergeSectionGutter>("LeftGutter")!.GetVisualDescendants().OfType<CheckBox>().Single();
            var right = window.FindControl<MergeSectionGutter>("RightGutter")!.GetVisualDescendants().OfType<CheckBox>().Single();
            void Click(CheckBox check)
            {
                var point = check.TranslatePoint(new Point(10, check.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(left.IsChecked);
            Assert.False(right.IsChecked);
            Click(right);
            Assert.True(left.IsChecked);
            Assert.True(right.IsChecked);
            const string both = "class Example\n{\n    void Mine() { RunMine(); }\n    void Teammate() { RunTheirs(); }\n}\n";
            Assert.Equal(both, vm.ResultDocument.Text);
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            using var frame = window.CaptureRenderedFrame();
            if (artifacts is not null) { Directory.CreateDirectory(artifacts); frame?.Save(Path.Combine(artifacts, "merge-both-methods.png")); }
            Click(left);
            Assert.False(left.IsChecked);
            Assert.True(right.IsChecked);
            Assert.Equal(vm.RightDocument.Text, vm.ResultDocument.Text);
            Click(left);
            Assert.Equal(both, vm.ResultDocument.Text);
            Click(right);
            Assert.True(left.IsChecked);
            Assert.False(right.IsChecked);
            Assert.Equal(vm.LeftDocument.Text, vm.ResultDocument.Text);
            vm.UndoCommand.Execute(null);
            Assert.Equal(both, vm.ResultDocument.Text);
            vm.RedoCommand.Execute(null);
            Assert.Equal(vm.LeftDocument.Text, vm.ResultDocument.Text);
            Click(left);
            Assert.Equal("class Example\n{\n}\n", vm.ResultDocument.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ManualEditsAreProtectedAndCanBecomeTheNewOriginal()
    {
        var files = new Files();
        using var vm = new MergeWorkspaceViewModel(files, files);
        vm.LeftDocument.Text = "original"; vm.RightDocument.Text = "incoming";
        await vm.PendingComparison;
        vm.FinalizeResultCommand.Execute(null);
        vm.ResultDocument.Insert(0, "manual ");
        Assert.False(vm.CanEditSources);
        vm.UndoCommand.Execute(null);
        Assert.Equal("manual original", vm.ResultDocument.Text);
        vm.ResultDocument.UndoStack.Undo();
        Assert.Equal("original", vm.ResultDocument.Text);
        vm.ResultDocument.Insert(0, "manual ");
        vm.EditInputsCommand.Execute(null);
        await vm.PendingComparison;
        Assert.True(vm.CanEditSources);
        Assert.Equal("manual original", vm.LeftDocument.Text);
        Assert.Equal("manual original", vm.ResultDocument.Text);
    }

    [AvaloniaFact]
    public async Task ToolbarMenusOpenSourcesEnableManualEditingAndSaveBeforeClosing()
    {
        var files = new Files();
        var vm = new MergeWorkspaceViewModel(files, files);
        var window = new MergeWindow(vm) { Width = 900, Height = 700 };
        try
        {
            window.Show();
            async Task ClickAction(string label)
            {
                var button = ToolbarTestAccess.Reveal(window, b => b.Content?.ToString() == label);
                await Task.Delay(160);
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                var surface = (TopLevel)button.GetVisualRoot()!;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), surface)!.Value;
                var action = button.Command as IAsyncRelayCommand;
                surface.MouseDown(point, MouseButton.Left);
                surface.MouseUp(point, MouseButton.Left);
                if (action?.ExecutionTask is { } pending) await pending;
                Dispatcher.UIThread.RunJobs();
            }
            files.NextText = "original\n";
            await ClickAction("Abrir original…");
            Assert.Equal("original\n", vm.LeftDocument.Text);
            Assert.False(window.FindControl<Button>("FileToolsButton")!.Flyout!.IsOpen);
            files.NextText = "incoming\n";
            await ClickAction("Abrir entrante…");
            await vm.PendingComparison;
            Assert.Equal("incoming\n", vm.RightDocument.Text);
            Choose(vm, 0, true);
            await ClickAction("Editar resultado manualmente");
            Assert.True(vm.IsFinalEditing);
            Assert.True(window.FindControl<TextEditor>("LeftInput")!.IsReadOnly);
            vm.ResultDocument.Insert(0, "manual\n");
            await ClickAction("Guardar resultado como…");
            Assert.Equal("manual\noriginal\nincoming\n", files.Saved!.Text);
            Assert.False(window.FindControl<Button>("FileToolsButton")!.Flyout!.IsOpen);
            await ClickAction("Usar resultado como original");
            await vm.PendingComparison;
            Assert.Equal(files.Saved.Text, vm.LeftDocument.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ComparerOpensAndReusesIndependentMergeWindow()
    {
        using var comparer = new MainWindowViewModel(new FakeFilePickerService()) { LeftText = "left stays", RightText = "right stays" };
        var main = new MainWindow(comparer);
        try
        {
            main.Show();
            var button = main.FindControl<Button>("OpenMergeButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var merge = Assert.Single(main.OwnedWindows.OfType<MergeWindow>());
            var workspace = Assert.IsType<MergeWorkspaceViewModel>(merge.DataContext);
            Assert.Empty(workspace.LeftDocument.Text); Assert.Empty(workspace.RightDocument.Text);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(merge, Assert.Single(main.OwnedWindows.OfType<MergeWindow>()));
            Assert.Equal("left stays", comparer.LeftText); Assert.Equal("right stays", comparer.RightText);
            merge.Close();
        }
        finally { main.Close(); }
    }
}
