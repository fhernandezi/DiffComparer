using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace DiffComparer.UI.Views;

public partial class MainWindow : Window
{
    private bool _isSyncing = false;

    public MainWindow()
    {
        InitializeComponent();

        // Conectar scroll sincronizado después de cargar la ventana
        this.Opened += (_, _) => ConnectScrollSync();
    }

    private void ConnectScrollSync()
    {
        var scrollLeft = this.FindControl<ScrollViewer>("ScrollLeft");
        var scrollRight = this.FindControl<ScrollViewer>("ScrollRight");

        if (scrollLeft is null || scrollRight is null) return;

        scrollLeft.ScrollChanged += (_, _) =>
        {
            if (_isSyncing) return;
            _isSyncing = true;
            scrollRight.Offset = scrollLeft.Offset;
            _isSyncing = false;
        };

        scrollRight.ScrollChanged += (_, _) =>
        {
            if (_isSyncing) return;
            _isSyncing = true;
            scrollLeft.Offset = scrollRight.Offset;
            _isSyncing = false;
        };
    }
}