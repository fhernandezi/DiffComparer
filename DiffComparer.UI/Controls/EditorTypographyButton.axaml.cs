using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiffComparer.UI.Controls;

public partial class EditorTypographyButton : UserControl
{
    public EditorTypographyButton()
    {
        InitializeComponent();
        this.FindControl<ComboBox>("FontPicker")!.ItemsSource = FontManager.Current.SystemFonts
            .Select(f => f.Name).Concat(new[] { "Consolas", "Cascadia Code", "Courier New", "monospace" })
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
