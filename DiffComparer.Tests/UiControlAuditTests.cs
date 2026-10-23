using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using DiffComparer.Core;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Models;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;
using SkiaSharp;

namespace DiffComparer.Tests;

public class UiControlAuditTests
{
    private static T NamedByContent<T>(Window window, string label) where T : ContentControl
    {
        if (typeof(T) == typeof(Button))
        {
            string? name = label switch { "Abrir izquierda" => "OpenLeftButton", "Abrir derecha" => "OpenRightButton",
                "Guardar izq. como…" => "SaveLeftButton", "Guardar der. como…" => "SaveRightButton",
                "↑ Anterior" => "PreviousButton", "↓ Siguiente" => "NextButton", _ => null };
            return (T)(ContentControl)ToolbarTestAccess.Reveal(window,
                b => name is not null ? b.Name == name : b.Content?.ToString() == label);
        }
        var match = window.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.Content?.ToString() == label);
        if (match is null || !match.IsEffectivelyVisible)
        {
            window.FindControl<Expander>("OptionsExpander")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
        }
        return window.GetVisualDescendants().OfType<T>().Single(c => c.Content?.ToString() == label);
    }

    private static void RenderForLayout()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
    }
    private static async Task Click(Window window, Control control)
    {
        if (window.DataContext is MainWindowViewModel vm)
        {
            await vm.PendingComparison;
            await vm.PendingViewUpdate;
        }
        var card = window.FindControl<Border>("OptionsCard")!;
        if (!control.GetVisualAncestors().Contains(card)) window.FindControl<Expander>("OptionsExpander")!.IsExpanded = false;
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.IsEffectivelyEnabled);
        var surface = (TopLevel?)control.GetVisualRoot() ?? window;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), surface)!.Value;
        var hit = surface.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Contains(control) == true,
            $"Click missed {control}: {point}, hit {hit}");
        var action = (control as Button)?.Command as IAsyncRelayCommand;
        surface.MouseDown(point, MouseButton.Left);
        surface.MouseUp(point, MouseButton.Left);
        if (action?.ExecutionTask is { } task) await task;
    }

    private static SKBitmap Snapshot(Window window)
    {
        // Settings now float above the editors; inspect the source after closing that layer.
        var options = window.FindControl<Expander>("OptionsExpander")!;
        bool expanded = options.IsExpanded;
        options.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        using var stream = new MemoryStream();
        frame!.Save(stream);
        var bitmap = SKBitmap.Decode(stream.ToArray());
        options.IsExpanded = expanded;
        return bitmap;
    }

    private static SKColor Background(Window window, TextEditor editor, int line)
    {
        using var bitmap = Snapshot(window);
        var view = editor.TextArea.TextView;
        var visual = view.VisualLines.Single(v => v.FirstDocumentLine.LineNumber == line);
        var point = view.TranslatePoint(new Point(240, visual.VisualTop - view.VerticalOffset + visual.Height / 2), window)!.Value;
        return bitmap.GetPixel((int)point.X, (int)point.Y);
    }

    private static int TextBrightness(Window window, TextEditor editor, int line)
    {
        using var bitmap = Snapshot(window);
        var view = editor.TextArea.TextView;
        var visual = view.VisualLines.Single(v => v.FirstDocumentLine.LineNumber == line);
        var point = view.TranslatePoint(new Point(0, visual.VisualTop - view.VerticalOffset), window)!.Value;
        int max = 0;
        for (int y = (int)point.Y + 2; y < point.Y + visual.Height - 2; y++)
            for (int x = (int)point.X + 2; x < point.X + 50; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                max = Math.Max(max, pixel.Red + pixel.Green + pixel.Blue);
            }
        return max;
    }

    [AvaloniaFact]
    public async Task HighlightCheckboxes_ContextAndReview_ChangeRenderedEditors()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        {
            LeftText = "header\nold removed\nsame boundary\nvalue: 100\nfooter\n",
            RightText = "header\nsame boundary\nvalue: 120\nfooter\nnew added\n"
        };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            Assert.Equal(3, vm.ChangeBlockCount);
            int bright = TextBrightness(window, left, 1);
            await Click(window, NamedByContent<CheckBox>(window, "Resaltar solo diferencias"));
            await vm.PendingViewUpdate;
            var context = window.GetVisualDescendants().OfType<NumericUpDown>().Single();
            Assert.True(context.IsEffectivelyEnabled);
            context.Value = 0;
            await vm.PendingViewUpdate;
            Assert.True(TextBrightness(window, left, 1) < bright - 100);
            context.Value = 1;
            await vm.PendingViewUpdate;
            Assert.True(TextBrightness(window, left, 1) >= bright - 5);
            context.Value = 0;
            await vm.PendingViewUpdate;

            foreach (var item in new[] { ("Agregadas", right, 5), ("Eliminadas", left, 2), ("Modificadas", right, 3) })
            {
                var before = Background(window, item.Item2, item.Item3);
                var checkbox = NamedByContent<CheckBox>(window, item.Item1);
                await Click(window, checkbox);
                await vm.PendingViewUpdate;
                var after = Background(window, item.Item2, item.Item3);
                Assert.NotEqual(before, after);
                Assert.Equal(Background(window, item.Item2, 1), after);
                Assert.Equal(3, vm.DiscrepancyCount);
                await Click(window, checkbox);
                await vm.PendingViewUpdate;
                Assert.Equal(before, Background(window, item.Item2, item.Item3));
            }
            await Click(window, NamedByContent<Button>(window, "Marcar revisado"));
            await vm.PendingViewUpdate;
            Assert.True(vm.CurrentReviewed);
            await Click(window, NamedByContent<Button>(window, "Quitar revisión"));
            await vm.PendingViewUpdate;
            Assert.False(vm.CurrentReviewed);
            await Click(window, NamedByContent<Button>(window, "Marcar revisado"));
            await vm.PendingViewUpdate;
            await Click(window, NamedByContent<CheckBox>(window, "Sin revisar"));
            await vm.PendingViewUpdate;
            Assert.Equal(2, vm.View.VisibleBlocks.Length);
            Assert.False(vm.CurrentReviewed);
            await Click(window, NamedByContent<CheckBox>(window, "Sin revisar"));
            await vm.PendingViewUpdate;
            await Click(window, NamedByContent<Button>(window, "↓ Siguiente"));
            int index = vm.CurrentChangeIndex;
            await Click(window, NamedByContent<Button>(window, "↑ Anterior"));
            Assert.NotEqual(index, vm.CurrentChangeIndex);
            Assert.Equal("header\nold removed\nsame boundary\nvalue: 100\nfooter\n", left.Text);
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(artifacts, "audited-filters.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SearchOptionsSyntaxAndWhitespace_AreConnectedAndKeepSourcesEditable()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = "class Demo\n  value: 100\n", RightText = "class Demo\n value: 120\n" };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            var search = window.FindControl<TextBox>("SearchBox")!;
            search.Text = "value";
            await vm.PendingViewUpdate;
            Assert.Equal(2, vm.View.SearchHits.Length);
            await Click(window, NamedByContent<Button>(window, "↓"));
            Assert.Equal("value", window.FindControl<TextEditor>("LeftEditor")!.SelectedText);
            await Click(window, NamedByContent<Button>(window, "↑"));
            Assert.Equal("value", right.SelectedText);
            await Click(window, NamedByContent<CheckBox>(window, "Enfocar resultados"));
            await vm.PendingViewUpdate;
            Assert.True(vm.SearchOnly);
            search.Text = "no such content";
            await vm.PendingViewUpdate;
            Assert.False(NamedByContent<Button>(window, "↓").IsEffectivelyEnabled);
            Assert.Empty(vm.View.SearchHits);
            await Click(window, NamedByContent<CheckBox>(window, "Enfocar resultados"));
            search.Text = "";
            await vm.PendingViewUpdate;
            await Click(window, NamedByContent<CheckBox>(window, "Espacios visibles"));
            Assert.True(right.Options.ShowSpaces && right.Options.ShowTabs && right.Options.ShowEndOfLine);
            var language = window.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.Languages));
            language.SelectedItem = "C#";
            Assert.NotNull(right.SyntaxHighlighting);
            language.SelectedItem = "Texto";
            Assert.Null(right.SyntaxHighlighting);
            var strategy = window.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.Strategies));
            strategy.SelectedItem = "Mínimo";
            await vm.PendingComparison;
            Assert.Equal(DiffStrategy.Minimal, vm.CanonicalResult!.Options.Strategy);
            await Click(window, NamedByContent<CheckBox>(window, "Conservar EOL destino"));
            Assert.False(vm.PreserveDestinationEndings);
            await Click(window, NamedByContent<CheckBox>(window, "Comparar EOL"));
            await vm.PendingComparison;
            Assert.False(NamedByContent<CheckBox>(window, "Conservar EOL destino").IsEffectivelyEnabled);
            Assert.True(vm.CanonicalResult!.Options.CompareLineEndings);
            vm.LeftText = "a  b\r\n";
            vm.RightText = "a b\n";
            await vm.PendingComparison;
            await Click(window, NamedByContent<CheckBox>(window, "Comparar EOL"));
            await vm.PendingComparison;
            await Click(window, NamedByContent<CheckBox>(window, "Ignorar espacios"));
            await vm.PendingComparison;
            Assert.Equal(0, vm.DiscrepancyCount);
            Assert.False(right.IsReadOnly);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ToolbarUndoRedo_WorkForTypingAndBothCopyDirections()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService()) { LeftText = "original", RightText = "original" };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            using var initial = window.CaptureRenderedFrame();
            right.TextArea.Focus();
            right.Document.Insert(right.Document.TextLength, "!");
            await vm.PendingComparison;
            Assert.Equal(DocumentSide.Right, vm.ActiveSide);
            await Click(window, NamedByContent<Button>(window, "Deshacer"));
            Assert.Equal("original", vm.RightText);
            await Click(window, NamedByContent<Button>(window, "Rehacer"));
            Assert.Equal("original!", vm.RightText);
            await Click(window, NamedByContent<Button>(window, "Copiar →"));
            Assert.Equal("original", vm.RightText);
            await Click(window, NamedByContent<Button>(window, "Deshacer"));
            Assert.Equal("original!", vm.RightText);
            await Click(window, NamedByContent<Button>(window, "Rehacer"));
            Assert.Equal("original", vm.RightText);
            await Click(window, NamedByContent<Button>(window, "Deshacer"));
            await Click(window, NamedByContent<Button>(window, "← Copiar"));
            Assert.Equal("original!", vm.LeftText);
            Assert.Equal(0, vm.DiscrepancyCount);
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            left.TextArea.Focus();
            left.Document.Insert(left.Document.TextLength, "?");
            await vm.PendingComparison;
            await Click(window, NamedByContent<Button>(window, "Deshacer"));
            Assert.Equal("original!", vm.LeftText);
            await Click(window, NamedByContent<Button>(window, "Rehacer"));
            Assert.Equal("original!?", vm.LeftText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MovedFilterAndFinalNewlineIndicators_WorkInCompactWindow()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        {
            LeftText = "header\nmoved first unique line\nmoved second unique line\nanchor first unique line\nanchor second unique line\ntail\n",
            RightText = "header\nanchor first unique line\nanchor second unique line\nmoved first unique line\nmoved second unique line\ntail\n"
        };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            Assert.True(vm.MovedBlockCount > 0);
            await Click(window, NamedByContent<CheckBox>(window, "Solo movidas"));
            await vm.PendingViewUpdate;
            window.GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 0;
            await vm.PendingViewUpdate;
            Assert.All(vm.View.Rows.Where(row => !row.Gap), row => Assert.NotNull(row.Line.MoveId));
            await Click(window, NamedByContent<CheckBox>(window, "Solo movidas"));
            await vm.PendingViewUpdate;
            vm.LeftText = "same";
            vm.RightText = "same\n";
            await vm.PendingComparison;
            Assert.Contains("terminador o salto final", vm.CurrentChangeDetails);
            window.Width = 850;
            window.Height = 520;
            using var bitmap = Snapshot(window);
            // Every toolbar button remains reachable after the rows wrap at minimum size.
            foreach (var label in new[] { "Abrir izquierda", "Abrir derecha", "Guardar izq. como…", "Guardar der. como…",
                "↑ Anterior", "↓ Siguiente", "← Copiar", "Copiar →", "Deshacer", "Rehacer", "Marcar revisado", "Informe HTML", "Parche" })
            {
                var button = NamedByContent<Button>(window, label);
                button.BringIntoView();
                RenderForLayout();
                var surface = (TopLevel?)button.GetVisualRoot() ?? window;
                var point = button.TranslatePoint(default, surface)!.Value;
                Assert.InRange(point.X, 0, surface.Bounds.Width - button.Bounds.Width);
                Assert.InRange(point.Y, 0, surface.Bounds.Height - button.Bounds.Height);
            }
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(artifacts, "audited-compact-final-newline.png"));
            }
        }
        finally { window.Close(); }
    }

    private sealed class RecordingFiles : IFilePickerService, ITextFileSaveService, IReportExportService
    {
        public string Next = "";
        public string? Encoding;
        public readonly List<TextFileContent> Saves = new();
        public readonly List<(string Name, string Content)> Reports = new();
        public Task<string?> OpenTextFileAsync() => Task.FromResult<string?>(Next);
        public Task<TextFileContent?> OpenFileAsync(string encodingName = "Automático")
        {
            Encoding = encodingName;
            return Task.FromResult<TextFileContent?>(new(Next, "opened.txt", "opened.txt", "utf-8", "LF"));
        }
        public Task<TextFileContent?> SaveFileAsAsync(TextFileContent content)
        { Saves.Add(content); return Task.FromResult<TextFileContent?>(content with { Name = "saved.txt" }); }
        public Task<bool> SaveTextAsync(string suggestedName, string content, bool html)
        { Reports.Add((suggestedName, content)); return Task.FromResult(true); }
    }

    [AvaloniaFact]
    public async Task OpenSaveAndExportButtons_UseSelectedEncodingAndCompleteSourceTexts()
    {
        var files = new RecordingFiles { Next = "header\nold\ntail\n" };
        using var vm = new MainWindowViewModel(files);
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            using var initial = window.CaptureRenderedFrame();
            window.FindControl<Expander>("OptionsExpander")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var encoding = window.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.Encodings));
            encoding.SelectedItem = "Windows-1252";
            await Click(window, NamedByContent<Button>(window, "Abrir izquierda"));
            Assert.Equal("Windows-1252", files.Encoding);
            files.Next = "header\nnew\ntail\n";
            await Click(window, NamedByContent<Button>(window, "Abrir derecha"));
            Assert.Equal("header\nnew\ntail\n", vm.RightText);
            await vm.PendingComparison;
            await Click(window, NamedByContent<Button>(window, "Guardar izq. como…"));
            await Click(window, NamedByContent<Button>(window, "Guardar der. como…"));
            Assert.Equal(new[] { vm.LeftText, vm.RightText }, files.Saves.Select(s => s.Text));
            await Click(window, NamedByContent<CheckBox>(window, "Modificadas"));
            await vm.PendingViewUpdate;
            await Click(window, NamedByContent<Button>(window, "Informe HTML"));
            await Click(window, NamedByContent<Button>(window, "Parche"));
            Assert.Contains("old", files.Reports[0].Content);
            Assert.Contains("new", files.Reports[0].Content);
            Assert.Contains("-old", files.Reports[1].Content);
            Assert.Contains("+new", files.Reports[1].Content);
        }
        finally { window.Close(); }
    }
}
