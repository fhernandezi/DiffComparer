using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;

namespace DiffComparer.UI.Views;

public partial class MainWindow : Window
{
    private bool _isSyncing = false;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(
        new FilePickerService(this));
        // Conectar scroll sincronizado después de cargar la ventana
        this.Opened += (_, _) => ConnectScrollSync();
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
}