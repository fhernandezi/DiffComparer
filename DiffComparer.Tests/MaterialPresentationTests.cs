using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;
using SkiaSharp;

namespace DiffComparer.Tests;

public class MaterialPresentationTests
{
    private static void RenderReady()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
    }
    private static async Task Click(Window window, Button button)
    {
        if (window.DataContext is MainWindowViewModel vm)
        {
            await vm.PendingComparison;
            await vm.PendingViewUpdate;
        }
        // Let popup dismissal finish before sending the next physical click.
        await Task.Delay(160);
        RenderReady();
        Assert.True(button.IsEffectivelyEnabled);
        var surface = (TopLevel?)button.GetVisualRoot() ?? window;
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), surface)!.Value;
        var hit = surface.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true,
            $"Click missed {button.Content}: {point}, visible {button.IsVisible}/{button.IsEffectivelyVisible}, hit {hit}, parents {string.Join(" > ", hit?.GetVisualAncestors().Select(v => v.ToString()) ?? Array.Empty<string>())}, window {window.Bounds}");
        var action = button.Command as IAsyncRelayCommand;
        surface.MouseDown(point, MouseButton.Left);
        surface.MouseUp(point, MouseButton.Left);
        if (action?.ExecutionTask is { } task) await task;
    }
    private static Button ButtonFor(Window window, object command) =>
        ToolbarTestAccess.Reveal(window, b => ReferenceEquals(b.Command, command));

    [AvaloniaTheory]
    [InlineData("Oscuro")]
    [InlineData("Claro")]
    [InlineData("Sistema")]
    public async Task Themes_HaveReadableText_PastelDiffs_HumanTooltips_AndFlexiblePanels(string appearance)
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        {
            SelectedAppearance = appearance,
            LeftText = "// Ejemplo de comparación\nold removed line\nunchanged anchor\nvalue: 100\nfooter\n",
            RightText = "// Ejemplo de comparación\nunchanged anchor\nvalue: 120\nfooter\nnew added line\n"
        };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            RenderReady();
            Assert.Equal(appearance switch { "Claro" => ThemeVariant.Light, "Oscuro" => ThemeVariant.Dark,
                _ => Application.Current!.ActualThemeVariant }, window.ActualThemeVariant);
            Assert.False(window.FindControl<Expander>("OptionsExpander")!.IsExpanded);
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(left.Foreground).Color;
            var surface = Assert.IsAssignableFrom<ISolidColorBrush>(left.Background).Color;
            Assert.True(Contrast(foreground, surface) >= 7);
            using (var frame = window.CaptureRenderedFrame())
            using (var stream = new MemoryStream())
            {
                frame!.Save(stream);
                using var bitmap = SKBitmap.Decode(stream.ToArray());
                foreach (var source in new[] { (left, 2), (left, 4), (right, 5) })
                {
                    var view = source.Item1.TextArea.TextView;
                    var visual = view.VisualLines.Single(v => v.FirstDocumentLine.LineNumber == source.Item2);
                    var point = view.TranslatePoint(new Point(200, visual.VisualTop - view.VerticalOffset + visual.Height / 2), window)!.Value;
                    var pixel = bitmap.GetPixel((int)point.X, (int)point.Y);
                    Assert.True(Contrast(foreground, Color.FromRgb(pixel.Red, pixel.Green, pixel.Blue)) >= 4.5);
                }
                Save(frame, $"material-{appearance.ToLowerInvariant()}.png");
            }
            double editorHeightBeforeSettings = left.Bounds.Height;
            window.FindControl<Expander>("OptionsExpander")!.IsExpanded = true;
            RenderReady();
            Assert.Equal(editorHeightBeforeSettings, left.Bounds.Height);
            using (var settings = window.CaptureRenderedFrame()) Save(settings, $"material-{appearance.ToLowerInvariant()}-settings.png");
            var options = window.GetVisualDescendants().OfType<Control>().Where(c => c is CheckBox or ComboBox or NumericUpDown or Expander or GridSplitter
                || c.GetType() == typeof(Button) && ((Button)c).Content is string);
            Assert.All(options, control => Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(control)?.ToString()), $"Missing tooltip: {control}"));
            var done = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Listo");
            await Click(window, done);
            Assert.False(window.FindControl<Expander>("OptionsExpander")!.IsExpanded);
            window.FindControl<Expander>("OptionsExpander")!.IsExpanded = true;
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(window.FindControl<Expander>("OptionsExpander")!.IsExpanded);
            window.Width = 850; window.Height = 600;
            RenderReady();
            Assert.True(left.Bounds.Height >= 80, $"Editor height: {left.Bounds.Height}");
            Assert.True(right.Bounds.Height >= 80);
            Assert.True(left.Bounds.Width >= 200 && right.Bounds.Width >= 200);
            Assert.InRange(Math.Abs(left.Bounds.Width - right.Bounds.Width), 0, 2);
            using (var compact = window.CaptureRenderedFrame()) Save(compact, $"material-{appearance.ToLowerInvariant()}-compact.png");
            vm.SelectedAppearance = window.ActualThemeVariant == ThemeVariant.Light ? "Oscuro" : "Claro";
            RenderReady();
            Assert.NotEqual(foreground, Assert.IsAssignableFrom<ISolidColorBrush>(left.Foreground).Color);
            Assert.Same(vm.LeftDocument, left.Document);
            Assert.Same(vm.RightDocument, right.Document);
            Assert.Equal(3, vm.DiscrepancyCount);
        }
        finally { window.Close(); }
    }

    private sealed class Clipboard : IClipboardService
    {
        public string? Text;
        public bool Fail;
        public Task SetTextAsync(string text)
        {
            if (Fail) throw new InvalidOperationException("test unavailable");
            Text = text; return Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task ClearSwapResetAndRecovery_PreserveSourcesAndOptions_AndCopyWholeResult()
    {
        var clipboard = new Clipboard();
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        {
            LeftText = "left  text\r\n", RightText = "right text\n", IgnoreWhitespace = true,
            OnlyDifferences = true, ContextLines = 1, ClipboardService = clipboard
        };
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.PendingComparison;
            var leftDocument = vm.LeftDocument;
            var rightDocument = vm.RightDocument;
            await Click(window, ButtonFor(window, vm.CopyRightTextCommand));
            Assert.Equal("right text\n", clipboard.Text);
            Assert.Contains("copiado", vm.WorkspaceNotice);
            await Click(window, ButtonFor(window, vm.ClearLeftCommand));
            await vm.PendingComparison;
            Assert.Empty(vm.LeftText);
            Assert.True(vm.CanRestoreWorkspace);
            await Click(window, ButtonFor(window, vm.RestoreWorkspaceCommand));
            await vm.PendingComparison;
            Assert.Equal("left  text\r\n", vm.LeftText);
            await Click(window, ButtonFor(window, vm.SwapTextsCommand));
            await vm.PendingComparison;
            Assert.Equal("right text\n", vm.LeftText);
            Assert.Equal("left  text\r\n", vm.RightText);
            await Click(window, ButtonFor(window, vm.ResetWorkspaceCommand));
            await vm.PendingComparison;
            Assert.Empty(vm.LeftText); Assert.Empty(vm.RightText);
            Assert.False(vm.ResetWorkspaceCommand.CanExecute(null));
            vm.ResetWorkspaceCommand.Execute(null); // A repeated reset must not replace the recovery snapshot with empty text.
            Assert.False(vm.IgnoreWhitespace); Assert.False(vm.OnlyDifferences);
            Assert.False(ButtonFor(window, vm.CopyRightTextCommand).IsEffectivelyEnabled);
            await Click(window, ButtonFor(window, vm.RestoreWorkspaceCommand));
            Assert.Contains("Recuperaste", vm.WorkspaceNotice);
            await vm.PendingComparison;
            Assert.Equal("right text\n", vm.LeftText);
            Assert.Equal("left  text\r\n", vm.RightText);
            Assert.True(vm.IgnoreWhitespace && vm.OnlyDifferences);
            Assert.Equal(1, vm.ContextLines);
            Assert.Same(leftDocument, vm.LeftDocument); Assert.Same(rightDocument, vm.RightDocument);
            Assert.True(vm.IsResultCurrent);
            await Click(window, ButtonFor(window, vm.CopyLeftTextCommand));
            Assert.Equal(vm.LeftText, clipboard.Text);
            clipboard.Fail = true;
            await Click(window, ButtonFor(window, vm.CopyRightTextCommand));
            Assert.Contains("No se pudo copiar", vm.WorkspaceNotice);
            Assert.DoesNotContain("test unavailable", vm.WorkspaceNotice);
            await Click(window, ButtonFor(window, vm.ClearRightCommand));
            await vm.PendingComparison;
            Assert.Empty(vm.RightText);
            await Click(window, ButtonFor(window, vm.RestoreWorkspaceCommand));
            await vm.PendingComparison;
            Assert.Equal("left  text\r\n", vm.RightText);
        }
        finally { window.Close(); }
    }

    private static void Save(Avalonia.Media.Imaging.Bitmap? frame, string name)
    {
        string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifacts)) { Directory.CreateDirectory(artifacts); frame!.Save(Path.Combine(artifacts, name)); }
    }
    private static double Contrast(Color foreground, Color background)
    {
        static double Channel(byte value) { double n = value / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        static double L(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        return (Math.Max(L(foreground), L(background)) + .05) / (Math.Min(L(foreground), L(background)) + .05);
    }
}
