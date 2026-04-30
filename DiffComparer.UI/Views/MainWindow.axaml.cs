using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using DiffComparer.Core;
using DiffComparer.UI.Renderers;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;

namespace DiffComparer.UI.Views;

public partial class MainWindow : Window
{
    private bool _isSyncing = false;

    private readonly DiffEngine _diffEngine = new();

    private DiffLineBackgroundRenderer? _leftRenderer;
    private DiffLineBackgroundRenderer? _rightRenderer;
    private DiffWordColorizer? _leftWordColorizer;
    private DiffWordColorizer? _rightWordColorizer;
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel(new FilePickerService(this));
        DataContext = vm;

        Opened += (_, _) =>
        {
            ConnectEditors(vm);
            ConnectScrollSync();

            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.CurrentChangeIndex))
                    ScrollToChange(vm.CurrentChangeIndex);
            };
        };
    }

    private void ConnectEditors(MainWindowViewModel vm)
    {
        var leftEditor = this.FindControl<TextEditor>("LeftEditor");
        var rightEditor = this.FindControl<TextEditor>("RightEditor");

        if (leftEditor is null || rightEditor is null)
            return;

        leftEditor.IsReadOnly = false;
        rightEditor.IsReadOnly = false;

        leftEditor.Focusable = true;
        rightEditor.Focusable = true;

        leftEditor.Text = vm.LeftText ?? "";
        rightEditor.Text = vm.RightText ?? "";

        _leftRenderer = new DiffLineBackgroundRenderer(isLeftSide: true);
        _rightRenderer = new DiffLineBackgroundRenderer(isLeftSide: false);

        leftEditor.TextArea.TextView.BackgroundRenderers.Add(_leftRenderer);
        rightEditor.TextArea.TextView.BackgroundRenderers.Add(_rightRenderer);

        _leftWordColorizer = new DiffWordColorizer(isLeftSide: true);
        _rightWordColorizer = new DiffWordColorizer(isLeftSide: false);

        leftEditor.TextArea.TextView.LineTransformers.Add(_leftWordColorizer);
        rightEditor.TextArea.TextView.LineTransformers.Add(_rightWordColorizer);

        leftEditor.TextChanged += (_, _) =>
        {
            vm.LeftText = leftEditor.Text;
            UpdateEditorDiff(leftEditor, rightEditor);
        };

        rightEditor.TextChanged += (_, _) =>
        {
            vm.RightText = rightEditor.Text;
            UpdateEditorDiff(leftEditor, rightEditor);
        };

        UpdateEditorDiff(leftEditor, rightEditor);

        leftEditor.Focus();
    }

    private void UpdateEditorDiff(TextEditor leftEditor, TextEditor rightEditor)
    {
        var leftLines = (leftEditor.Text ?? "")
            .Replace("\r\n", "\n")
            .Split('\n');

        var rightLines = (rightEditor.Text ?? "")
            .Replace("\r\n", "\n")
            .Split('\n');

        var diff = _diffEngine.Compare(leftLines, rightLines);

        _leftRenderer?.SetDiffLines(diff);
        _rightRenderer?.SetDiffLines(diff);

        _leftWordColorizer?.SetDiffLines(diff);
        _rightWordColorizer?.SetDiffLines(diff);

        leftEditor.TextArea.TextView.Redraw();
        rightEditor.TextArea.TextView.Redraw();
    }

    private void ConnectScrollSync()
    {
        var scrollLeft = this.FindControl<ScrollViewer>("ScrollLeft");
        var scrollRight = this.FindControl<ScrollViewer>("ScrollRight");

        if (scrollLeft is null || scrollRight is null)
            return;

        scrollLeft.ScrollChanged += (_, _) =>
            SyncScroll(scrollLeft, scrollRight);

        scrollRight.ScrollChanged += (_, _) =>
            SyncScroll(scrollRight, scrollLeft);
    }

    private void SyncScroll(ScrollViewer source, ScrollViewer target)
    {
        if (_isSyncing)
            return;

        _isSyncing = true;
        target.Offset = source.Offset;
        _isSyncing = false;
    }

    private void ScrollToChange(int index)
    {
        if (index < 0)
            return;

        var scrollLeft = this.FindControl<ScrollViewer>("ScrollLeft");
        var scrollRight = this.FindControl<ScrollViewer>("ScrollRight");

        if (scrollLeft is null || scrollRight is null)
            return;

        var y = index * 22;

        _isSyncing = true;

        scrollLeft.Offset = new Vector(scrollLeft.Offset.X, y);
        scrollRight.Offset = new Vector(scrollRight.Offset.X, y);

        _isSyncing = false;
    }
}