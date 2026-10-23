using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using DiffComparer.UI.Controls;
using DiffComparer.Core;
using DiffComparer.UI.Models;
using DiffComparer.UI.Renderers;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;

namespace DiffComparer.UI.Views;

public partial class MainWindow : Window
{
    private void CloseCommandMenus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Command: not null } && e.Source is not Button { Name: "OpenMergeButton" }) return;
        // Button executes its command after raising Click. Keep bindings attached until then.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (string name in new[] { "FileToolsButton", "EditToolsButton", "ToolsButton" })
                this.FindControl<Button>(name)?.Flyout?.Hide();
        });
    }
    public DiffComparer.UI.Editing.EditorTypography Typography { get; }
    private DiffComparer.UI.Merge.MergeWindow? _mergeWindow;
    private void OpenMerge(object? sender, RoutedEventArgs e)
    {
        if (_mergeWindow is not null) { _mergeWindow.Activate(); return; }
        _mergeWindow = new DiffComparer.UI.Merge.MergeWindow(null, Typography) { RequestedThemeVariant = ActualThemeVariant };
        _mergeWindow.Closed += (_, _) => _mergeWindow = null;
        _mergeWindow.Show(this);
    }

    private void CloseOptions(object? sender, RoutedEventArgs e)
    {
        var options = this.FindControl<Expander>("OptionsExpander")!;
        options.IsExpanded = false;
        options.Focus();
    }

    public MainWindow() : this(null) { }
    public MainWindow(MainWindowViewModel? viewModel, DiffComparer.UI.Editing.EditorTypography? typography = null)
    {
        InitializeComponent();
        Typography = typography ?? new();
        this.FindControl<EditorTypographyButton>("TypographyButton")!.DataContext = Typography;
        var vm = viewModel ?? new MainWindowViewModel(new FilePickerService(this));
        DataContext = vm;
        vm.ClipboardService ??= new WindowClipboardService(this);
        var left = this.FindControl<TextEditor>("LeftEditor")!;
        var right = this.FindControl<TextEditor>("RightEditor")!;
        left.Document = vm.LeftDocument;
        right.Document = vm.RightDocument;
        left.TextArea.GotFocus += (_, _) => vm.ActiveSide = DocumentSide.Left;
        right.TextArea.GotFocus += (_, _) => vm.ActiveSide = DocumentSide.Right;
        var gutter = this.FindControl<ChangeActionGutter>("ChangeGutter")!;
        gutter.Connect(vm, left.TextArea.TextView, right.TextArea.TextView);
        var leftBackground = new DiffLineBackgroundRenderer(true);
        var rightBackground = new DiffLineBackgroundRenderer(false);
        var leftWords = new DiffWordColorizer(true);
        var rightWords = new DiffWordColorizer(false);
        var leftMargin = new OriginalLineNumberMargin(left.TextArea.TextView, true);
        var rightMargin = new OriginalLineNumberMargin(right.TextArea.TextView, false);
        left.TextArea.LeftMargins.Add(leftMargin);
        right.TextArea.LeftMargins.Add(rightMargin);
        var presentation = new DiffComparer.UI.Editing.EditorPresentation(this, Typography, left, right);
        const string lineHelp = "Los números indican las líneas del texto. + significa que se añadió; − que se eliminó; ~ que cambió. ✓ significa que ya revisaste esa sección; ¶ indica un cambio en el salto de línea; ↔ indica texto que cambió de lugar.";
        ToolTip.SetTip(leftMargin, lineHelp); ToolTip.SetTip(rightMargin, lineHelp);
        left.TextArea.TextView.BackgroundRenderers.Add(leftBackground);
        right.TextArea.TextView.BackgroundRenderers.Add(rightBackground);
        left.TextArea.TextView.LineTransformers.Add(leftWords);
        right.TextArea.TextView.LineTransformers.Add(rightWords);

        void Redraw()
        {
            left.TextArea.TextView.Redraw();
            right.TextArea.TextView.Redraw();
        }
        void SelectChange(bool scroll)
        {
            void SelectSide(TextEditor editor, DiffLineBackgroundRenderer renderer, bool isLeft)
            {
                int start = -1, end = -1;
                if (vm.IsResultCurrent && vm.CurrentChangeIndex >= 0)
                    for (int i = vm.CurrentChangeIndex; i < vm.DiffLines.Count
                        && vm.DiffLines[i].HunkId == vm.DiffLines[vm.CurrentChangeIndex].HunkId; i++)
                    {
                        var line = vm.DiffLines[i];
                        int? number = isLeft ? line.LeftLine : line.RightLine;
                        if (number.HasValue && vm.View.SourceToVisual.ContainsKey(i))
                        { if (start < 0) start = number.Value; end = number.Value; }
                    }
                renderer.SetSelection(start, end);
                if (scroll && vm.IsResultCurrent && vm.CanonicalResult is { } result)
                {
                    var hunk = result.Hunks.FirstOrDefault(h => h.RowStart == vm.CurrentChangeIndex);
                    if (hunk is not null)
                    {
                        int anchor = isLeft ? hunk.LeftLines.Start : hunk.RightLines.Start;
                        editor.ScrollToLine(Math.Min((start >= 0 ? start : anchor) + 1, editor.Document.LineCount));
                    }
                }
            }
            SelectSide(left, leftBackground, true);
            SelectSide(right, rightBackground, false);
            Redraw();
        }
        void RefreshEditors()
        {
            // Draw on the source documents without replacing text or inserting alignment rows.
            var lines = vm.IsResultCurrent ? vm.View.Rows.Where(row => !row.Gap).Select(row => row.Line).ToArray()
                : Array.Empty<LineMap>();
            leftBackground.SetDiffLines(lines, true);
            rightBackground.SetDiffLines(lines, true);
            leftWords.SetDiffLines(lines, true);
            rightWords.SetDiffLines(lines, true);
            System.Collections.Generic.Dictionary<int, SearchHit[]> Hits(bool isLeft) =>
                vm.View.SearchHits.Where(h => h.Left == isLeft && vm.IsResultCurrent)
                    .Select(h => (Hit: h, Line: isLeft ? vm.View.Rows[h.Row].Line.LeftLine : vm.View.Rows[h.Row].Line.RightLine))
                    .Where(x => x.Line.HasValue).GroupBy(x => x.Line!.Value)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Hit with { Row = g.Key }).ToArray());
            leftWords.SetSearchHits(Hits(true));
            rightWords.SetSearchHits(Hits(false));
            bool focus = vm.IsResultCurrent && vm.HasViewFilter;
            leftWords.SetFocusLines(focus ? lines.Where(l => l.LeftLine.HasValue).Select(l => l.LeftLine!.Value).ToHashSet() : null);
            rightWords.SetFocusLines(focus ? lines.Where(l => l.RightLine.HasValue).Select(l => l.RightLine!.Value).ToHashSet() : null);
            var rows = vm.IsResultCurrent ? vm.View.Rows : Array.Empty<ProjectedRow>();
            leftMargin.SetSourceRows(rows);
            rightMargin.SetSourceRows(rows);
            SelectChange(false);
            gutter.Refresh();
        }
        void UpdateSyntax()
        {
            IHighlightingDefinition? Definition(string name)
            {
                if (vm.SelectedLanguage == "Texto") return null;
                var definition = vm.SelectedLanguage == "Automático"
                    ? HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(name))
                    : HighlightingManager.Instance.GetDefinition(vm.SelectedLanguage)
                        ?? HighlightingManager.Instance.GetDefinition(vm.SelectedLanguage switch { "SQL" => "TSQL", "JSON" => "Json", _ => vm.SelectedLanguage });
                if (definition is not null)
                    foreach (var color in definition.NamedHighlightingColors)
                    {
                        string token = color.Name ?? "";
                        bool dark = ActualThemeVariant == ThemeVariant.Dark;
                        string? value = token.Contains("comment", StringComparison.OrdinalIgnoreCase) ? (dark ? "#A0BC8C" : "#526B3E")
                            : token.Contains("string", StringComparison.OrdinalIgnoreCase) || token.Contains("char", StringComparison.OrdinalIgnoreCase) ? (dark ? "#E6AE99" : "#9D462F")
                            : token.Contains("keyword", StringComparison.OrdinalIgnoreCase) ? (dark ? "#A6C8FF" : "#2857A8")
                            : token.Contains("number", StringComparison.OrdinalIgnoreCase) || token.Contains("digit", StringComparison.OrdinalIgnoreCase) ? (dark ? "#D0BCEB" : "#755AA0")
                            : null;
                        if (value is not null) color.Foreground = new SimpleHighlightingBrush(Color.Parse(value));
                    }
                return definition;
            }
            left.SyntaxHighlighting = Definition(vm.LeftName);
            right.SyntaxHighlighting = Definition(vm.RightName);
        }
        void UpdateDensity()
        {
            bool compact = (Bounds.Width > 0 ? Bounds.Width : Width) < 1100 || (Bounds.Height > 0 ? Bounds.Height : Height) < 740;
            Classes.Set("compact", compact);
            foreach (var item in new[] {
                ("PreviousButton", "↑ Anterior", "↑"), ("NextButton", "↓ Siguiente", "↓") })
                this.FindControl<Button>(item.Item1)!.Content = compact ? item.Item3 : item.Item2;
        }
        void RefreshTheme()
        {
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            leftBackground.IsDarkTheme = rightBackground.IsDarkTheme = dark;
            leftWords.IsDarkTheme = rightWords.IsDarkTheme = dark;
            leftMargin.IsDarkTheme = rightMargin.IsDarkTheme = dark;
            leftMargin.InvalidateVisual(); rightMargin.InvalidateVisual();
            UpdateSyntax(); Redraw(); gutter.Refresh();
        }
        void ApplyAppearance()
        {
            RequestedThemeVariant = vm.SelectedAppearance switch
            { "Claro" => ThemeVariant.Light, "Oscuro" => ThemeVariant.Dark, _ => ThemeVariant.Default };
            RefreshTheme();
        }
        void UpdateWhitespace()
        {
            left.Options.ShowSpaces = right.Options.ShowSpaces = vm.ShowWhitespace;
            left.Options.ShowTabs = right.Options.ShowTabs = vm.ShowWhitespace;
            left.Options.ShowEndOfLine = right.Options.ShowEndOfLine = vm.ShowWhitespace;
            Redraw();
        }
        void SelectSearch()
        {
            if (vm.CurrentSearchIndex < 0 || vm.CurrentSearchIndex >= vm.View.SearchHits.Length) return;
            var hit = vm.View.SearchHits[vm.CurrentSearchIndex];
            var editor = hit.Left ? left : right;
            var source = vm.View.Rows[hit.Row].Line;
            int? number = hit.Left ? source.LeftLine : source.RightLine;
            if (number is null || number + 1 > editor.Document.LineCount) return;
            var line = editor.Document.GetLineByNumber(number.Value + 1);
            editor.Select(line.Offset + hit.Column, hit.Length);
            editor.ScrollToLine(number.Value + 1);
        }
        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(vm.SelectedAppearance): ApplyAppearance(); break;
                case nameof(vm.IsEditMode): RefreshEditors(); break;
                case nameof(vm.View): RefreshEditors(); break;
                case nameof(vm.IsResultCurrent): RefreshEditors(); break;
                case nameof(vm.IsBusy): gutter.Refresh(); break;
                case nameof(vm.LeftText): if (vm.IsEditMode && !ReferenceEquals(left.Document, vm.LeftDocument)) RefreshEditors(); break;
                case nameof(vm.RightText): if (vm.IsEditMode && !ReferenceEquals(right.Document, vm.RightDocument)) RefreshEditors(); break;
                case nameof(vm.CurrentChangeIndex): SelectChange(false); break;
                case nameof(vm.CurrentSearchIndex): SelectSearch(); break;
                case nameof(vm.SelectedLanguage):
                case nameof(vm.LeftName):
                case nameof(vm.RightName): UpdateSyntax(); break;
                case nameof(vm.ShowWhitespace): UpdateWhitespace(); break;
            }
        };
        vm.ChangeNavigationRequested += () => SelectChange(true);
        vm.SearchFocusRequested += () => this.FindControl<TextBox>("SearchBox")?.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && this.FindControl<Expander>("OptionsExpander")!.IsExpanded)
            { CloseOptions(this, e); e.Handled = true; }
        };
        SizeChanged += (_, _) => UpdateDensity();
        ActualThemeVariantChanged += (_, _) => RefreshTheme();
        Closed += (_, _) => { presentation.Dispose(); vm.Dispose(); };
        RefreshEditors();
        ApplyAppearance();
        UpdateDensity();
        UpdateWhitespace();
    }
}
