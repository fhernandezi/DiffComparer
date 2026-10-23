using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.Controls;
using DiffComparer.UI.Editing;
using DiffComparer.UI.Merge;
using DiffComparer.UI.Renderers;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;

namespace DiffComparer.Tests;

public class EditorTypographyTests
{
    [Fact]
    public void SettingsBoundValuesAndResetZoomIndependently()
    {
        var settings = new EditorTypography { FontSize = 18, Zoom = 200, LineSpacing = 1.8, WordWrap = true };
        Assert.Equal(36, settings.EffectiveFontSize);
        settings.ResetZoomCommand.Execute(null);
        Assert.Equal(18, settings.EffectiveFontSize);
        Assert.Equal(1.8, settings.LineSpacing);
        settings.Zoom = double.NaN;
        settings.FontSize = double.PositiveInfinity;
        settings.LineSpacing = double.NegativeInfinity;
        Assert.Equal(100, settings.Zoom);
        Assert.Equal(18, settings.FontSize);
        settings.Zoom = 1000;
        settings.FontSize = 0;
        settings.LineSpacing = -1;
        Assert.Equal(300, settings.Zoom);
        Assert.Equal(8, settings.FontSize);
        Assert.Equal(1, settings.LineSpacing);
        settings.ResetTypographyCommand.Execute(null);
        Assert.Equal(13, settings.EffectiveFontSize);
        Assert.False(settings.WordWrap);
    }

    [Fact]
    public void PreferencesRoundTripAndCorruptFileFallsBack()
    {
        string path = Path.Combine(Path.GetTempPath(), $"diffcomparer-typography-{Guid.NewGuid():N}.json");
        try
        {
            var store = new EditorTypographyStore(path);
            Assert.Equal(13, store.Load().FontSize);
            Assert.True(store.Save(new() { FontName = "Courier New", FontSize = 19, Zoom = 150, LineSpacing = 1.9, WordWrap = true }));
            var loaded = store.Load();
            Assert.Equal("Courier New", loaded.FontName);
            Assert.Equal(19, loaded.FontSize);
            Assert.Equal(150, loaded.Zoom);
            Assert.Equal(1.9, loaded.LineSpacing);
            Assert.True(loaded.WordWrap);
            File.WriteAllText(path, "broken json");
            Assert.Equal(13, store.Load().FontSize);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task TypographyKeepsDocumentsSelectionUndoAndComparisonIntact()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
            { LeftText = "head\nleft\ntail", RightText = "head\nright\ntail" };
        var settings = new EditorTypography();
        var window = new MainWindow(vm, settings);
        try
        {
            window.Show();
            await vm.CompareCommand.ExecuteAsync(null);
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            var right = window.FindControl<TextEditor>("RightEditor")!;
            var document = left.Document;
            left.Document.Insert(0, "edit\n");
            await vm.PendingComparison;
            left.Select(5, 4);
            int caret = left.CaretOffset;
            var comparison = vm.CanonicalResult;
            var pending = vm.PendingComparison;
            Dispatcher.UIThread.RunJobs();
            double originalHeight = left.TextArea.TextView.DefaultLineHeight;
            var margin = left.TextArea.LeftMargins.OfType<OriginalLineNumberMargin>().Single();
            double originalWidth = margin.Bounds.Width;
            settings.FontSize = 20;
            settings.Zoom = 150;
            settings.LineSpacing = 2;
            settings.FontName = "Courier New";
            settings.WordWrap = true;
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.Equal(30, left.FontSize);
            Assert.Equal(30, right.FontSize);
            Assert.Equal(2, left.Options.LineHeightFactor);
            Assert.Contains("Courier New", left.FontFamily.Name);
            Assert.True(right.WordWrap);
            Assert.True(left.TextArea.TextView.DefaultLineHeight > originalHeight * 2);
            Assert.True(margin.Bounds.Width > originalWidth);
            Assert.Same(document, left.Document);
            Assert.Same(comparison, vm.CanonicalResult);
            Assert.Same(pending, vm.PendingComparison);
            Assert.Equal(5, left.SelectionStart);
            Assert.Equal(4, left.SelectionLength);
            Assert.Equal(caret, left.CaretOffset);
            left.Document.UndoStack.Undo();
            Assert.Equal("head\nleft\ntail", left.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EditorKeyboardZoomAndSharedMergeSettingsWork()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            var left = window.FindControl<TextEditor>("LeftEditor")!;
            Dispatcher.UIThread.RunJobs();
            var wheelPoint = left.TranslatePoint(new Point(left.Bounds.Width / 2, left.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(wheelPoint, new Vector(0, 1), RawInputModifiers.Control);
            Assert.Equal(110, window.Typography.Zoom);
            window.MouseWheel(wheelPoint, new Vector(0, -1), RawInputModifiers.Control);
            Assert.Equal(100, window.Typography.Zoom);
            left.TextArea.Focus();
            window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
            Assert.Equal(110, window.Typography.Zoom);
            window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.Minus, RawInputModifiers.Control);
            Assert.Equal(100, window.Typography.Zoom);
            window.Typography.Zoom = 170;
            window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
            Assert.Equal(100, window.Typography.Zoom);
            window.FindControl<Button>("OpenMergeButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var merge = Assert.Single(window.OwnedWindows.OfType<MergeWindow>());
            Assert.Same(window.Typography, merge.Typography);
            merge.Typography.FontSize = 22;
            merge.Typography.LineSpacing = 1.7;
            foreach (var editor in merge.GetVisualDescendants().OfType<TextEditor>())
            { Assert.Equal(22, editor.FontSize); Assert.Equal(1.7, editor.Options.LineHeightFactor); }
            Assert.Equal(22, left.FontSize);
            merge.Close();
            window.Typography.FontSize = 16;
            Assert.Equal(16, left.FontSize);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TextPanelBindsControlsAndFitsCompactWindow()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = "head\nold value\ntail", RightText = "head\nnew value\ntail" };
        var window = new MainWindow(vm) { Width = 850, Height = 700 };
        try
        {
            window.Show();
            await vm.CompareCommand.ExecuteAsync(null);
            ToolbarTestAccess.RevealView(window);
            var control = window.FindControl<EditorTypographyButton>("TypographyButton")!;
            var button = control.GetVisualDescendants().OfType<Button>().Single();
            button.Flyout!.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            var flyout = Assert.IsType<Flyout>(button.Flyout);
            var panel = Assert.IsType<StackPanel>(flyout.Content);
            var fields = panel.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            fields[0].Value = 18;
            fields[1].Value = 1.6m;
            Assert.Equal(18, window.Typography.FontSize);
            Assert.Equal(1.6, window.Typography.LineSpacing);
            var picker = panel.GetVisualDescendants().OfType<ComboBox>().Single();
            picker.SelectedItem = "Courier New";
            Assert.Equal("Courier New", window.Typography.FontName);
            Assert.True(button.Bounds.Width > 0);
            string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
            await Task.Delay(150);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            if (artifacts is not null) { Directory.CreateDirectory(artifacts); frame?.Save(Path.Combine(artifacts, "typography-panel.png")); }
            button.Flyout.Hide();
        }
        finally { window.Close(); }
    }
}
