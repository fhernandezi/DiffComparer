using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaEdit;
using DiffComparer.Core;
using DiffComparer.UI.Services;
using DiffComparer.UI.Renderers;

namespace DiffComparer.UI.Merge;

public partial class MergeWindow : Window
{
    private void CloseCommandMenus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Command: not null }) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (string name in new[] { "FileToolsButton", "EditToolsButton" })
                this.FindControl<Button>(name)?.Flyout?.Hide();
        });
    }
    public DiffComparer.UI.Editing.EditorTypography Typography { get; }
    public MergeWindow() : this(null) { }
    public MergeWindow(MergeWorkspaceViewModel? viewModel, DiffComparer.UI.Editing.EditorTypography? typography = null)
    {
        InitializeComponent();
        Typography = typography ?? new();
        this.FindControl<DiffComparer.UI.Controls.EditorTypographyButton>("TypographyButton")!.DataContext = Typography;
        var files = new FilePickerService(this);
        var vm = viewModel ?? new MergeWorkspaceViewModel(files, files);
        DataContext = vm;
        var left = this.FindControl<TextEditor>("LeftInput")!;
        var right = this.FindControl<TextEditor>("RightInput")!;
        var result = this.FindControl<TextEditor>("ResultEditor")!;
        left.Document = vm.LeftDocument; right.Document = vm.RightDocument; result.Document = vm.ResultDocument;
        var presentation = new DiffComparer.UI.Editing.EditorPresentation(this, Typography, left, right, result);
        var leftGutter = this.FindControl<MergeSectionGutter>("LeftGutter")!;
        var rightGutter = this.FindControl<MergeSectionGutter>("RightGutter")!;
        leftGutter.Connect(vm, left, false); rightGutter.Connect(vm, right, true);
        var leftBackground = new DiffLineBackgroundRenderer(true);
        var rightBackground = new DiffLineBackgroundRenderer(false);
        var leftWords = new DiffWordColorizer(true);
        var rightWords = new DiffWordColorizer(false);
        left.TextArea.TextView.BackgroundRenderers.Add(leftBackground);
        right.TextArea.TextView.BackgroundRenderers.Add(rightBackground);
        left.TextArea.TextView.LineTransformers.Add(leftWords);
        right.TextArea.TextView.LineTransformers.Add(rightWords);
        void Refresh()
        {
            var rows = vm.Comparison?.Lines.ToArray() ?? Array.Empty<LineMap>();
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            leftBackground.IsDarkTheme = rightBackground.IsDarkTheme = dark;
            leftWords.IsDarkTheme = rightWords.IsDarkTheme = dark;
            leftBackground.SetDiffLines(rows, true); rightBackground.SetDiffLines(rows, true);
            leftWords.SetDiffLines(rows, true); rightWords.SetDiffLines(rows, true);
            left.TextArea.TextView.Redraw(); right.TextArea.TextView.Redraw();
            leftGutter.Refresh(); rightGutter.Refresh();
        }
        void Navigate()
        {
            if (vm.CurrentSection is not { } h) return;
            left.ScrollToLine(Math.Min(h.LeftLines.Start + 1, left.Document.LineCount));
            right.ScrollToLine(Math.Min(h.RightLines.Start + 1, right.Document.LineCount));
            int offset = Math.Clamp(vm.Session!.ResultOffset(h.Id), 0, result.Document.TextLength);
            result.ScrollToLine(result.Document.GetLineByOffset(offset).LineNumber);
        }
        vm.PresentationChanged += Refresh;
        vm.SectionNavigationRequested += Navigate;
        ActualThemeVariantChanged += (_, _) => Refresh();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsFinalEditing) && vm.IsFinalEditing) result.Focus();
        };
        Closed += (_, _) =>
        {
            vm.PresentationChanged -= Refresh; vm.SectionNavigationRequested -= Navigate;
            presentation.Dispose(); vm.Dispose();
        };
        Refresh();
    }
}
