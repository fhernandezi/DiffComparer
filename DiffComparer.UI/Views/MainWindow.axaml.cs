using Avalonia;
using Avalonia.Controls;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;

namespace DiffComparer.UI.Views;

public partial class MainWindow : Window
{
    private bool _isSyncing = false;

    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel(new FilePickerService(this));
        DataContext = vm;

        Opened += (_, _) =>
        {
            ConnectScrollSync();

            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.CurrentChangeIndex))
                    ScrollToChange(vm.CurrentChangeIndex);
            };
        };
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