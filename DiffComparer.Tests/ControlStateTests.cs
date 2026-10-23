using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Views;
using SkiaSharp;

namespace DiffComparer.Tests;

public class ControlStateTests
{
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
    }
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double F(byte b) { double s = b / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B);
        }
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ButtonsHaveStableNormalHoverPressedAndDisabledSurfaces(bool dark)
    {
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        var buttons = new[]
        {
            new Button { Content = "Normal", Width = 210 },
            new Button { Content = "Acción principal", Width = 210, Classes = { "primary" } },
            new Button { Content = "Acción secundaria", Width = 210, Classes = { "quiet" } },
            new Button { Content = "Acción destructiva", Width = 210, Classes = { "danger" } },
            new Button { Content = "Menú", Width = 210, Classes = { "toolbarMenu" } },
            new Button { Content = "No disponible", Width = 210, IsEnabled = false }
        };
        foreach (var b in buttons) stack.Children.Add(b);
        var window = new Window { Width = 420, Height = 420, Content = stack,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Render();
            foreach (var button in buttons.Where(b => b.IsEnabled))
            {
                window.MouseMove(new Point(390, 390)); Render();
                Color normal = ColorOf(button.Background);
                Assert.Equal(1, button.Opacity);
                Assert.True(button.Transitions is null || button.Transitions.Count == 0);
                var surface = button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "ActionSurface");
                Assert.Equal(normal, ColorOf(surface.Background));
                if (normal.A != 0) Assert.True(Contrast(ColorOf(surface.Foreground), normal) >= 4.5);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                Color? lastHover = null;
                // Repeated hover, including a late sample: this catches the reported delayed white layer.
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    window.MouseMove(point); Render();
                    Color hover = ColorOf(surface.Background);
                    if (lastHover is { } previous) Assert.Equal(previous, hover);
                    lastHover = hover;
                    Assert.True(Contrast(ColorOf(surface.Foreground), hover) >= 4.5, $"{button.Content}: hover text contrast");
                    Assert.NotEqual(Colors.White, dark ? hover : Colors.Black);
                    await Task.Delay(80); Render();
                    Assert.Equal(hover, ColorOf(surface.Background));
                    await Task.Delay(560); Render();
                    Assert.Equal(hover, ColorOf(surface.Background));
                    using (var frame = window.CaptureRenderedFrame())
                    using (var stream = new MemoryStream())
                    {
                        frame!.Save(stream);
                        using var pixels = SKBitmap.Decode(stream.ToArray());
                        var sample = button.TranslatePoint(new Point(5, button.Bounds.Height / 2), window)!.Value;
                        var pixel = pixels.GetPixel((int)sample.X, (int)sample.Y);
                        Assert.Equal(hover, Color.FromArgb(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue));
                    }
                    window.MouseDown(point, MouseButton.Left); Render();
                    Assert.True(Contrast(ColorOf(surface.Foreground), ColorOf(surface.Background)) >= 4.5, $"{button.Content}: pressed contrast");
                    window.MouseUp(point, MouseButton.Left);
                    window.MouseMove(new Point(390, 390)); Render();
                    Assert.Equal(normal, ColorOf(surface.Background));
                }
            }
            var disabled = buttons[^1];
            Color disabledBackground = ColorOf(disabled.Background), disabledForeground = ColorOf(disabled.Foreground);
            var disabledPoint = disabled.TranslatePoint(new Point(15, 15), window)!.Value;
            window.MouseMove(disabledPoint); Render();
            await Task.Delay(700); Render();
            Assert.Equal(disabledBackground, ColorOf(disabled.Background));
            Assert.Equal(disabledForeground, ColorOf(disabled.Foreground));
            Assert.NotEqual(ColorOf(buttons[0].Foreground), disabledForeground);
            Assert.NotEqual(ColorOf(buttons[0].BorderBrush), ColorOf(disabled.BorderBrush));
            Assert.True(Contrast(ColorOf(buttons[0].BorderBrush), ColorOf(buttons[0].Background)) >= 3);
            buttons[0].Focus(NavigationMethod.Tab); Render();
            Assert.Equal(new Thickness(2), buttons[0].BorderThickness);
            Assert.True(Contrast(ColorOf(buttons[0].BorderBrush), ColorOf(buttons[0].Background)) >= 3);
            Save(window, dark ? "controls-dark.png" : "controls-light.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Oscuro", "#273449")]
    [InlineData("Claro", "#F3F4F6")]
    public async Task RealSearchFieldKeepsItsSurfaceWhenFocusedTypedAndHovered(string appearance, string background)
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { SelectedAppearance = appearance, LeftText = "head\nold value\ntail", RightText = "head\nnew value\ntail" };
        var window = new MainWindow(vm) { Width = 1100, Height = 700 };
        try
        {
            window.Show(); await vm.PendingComparison; Render();
            var search = window.FindControl<TextBox>("SearchBox")!;
            var border = search.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Color expected = Color.Parse(background);
            Assert.Equal(expected, ColorOf(border.Background));
            search.Focus(NavigationMethod.Tab);
            window.KeyTextInput("value");
            await vm.PendingViewUpdate; Render();
            Assert.Equal("value", search.Text);
            Assert.Equal(expected, ColorOf(border.Background));
            var point = search.TranslatePoint(new Point(20, search.Bounds.Height / 2), window)!.Value;
            for (int i = 0; i < 2; i++)
            {
                window.MouseMove(point); Render();
                await Task.Delay(700); Render();
                Assert.Equal(expected, ColorOf(border.Background));
                Assert.True(Contrast(ColorOf(search.Foreground), expected) >= 4.5);
                window.MouseMove(new Point(10, 650)); Render();
            }
            search.Text = "sin coincidencias";
            await vm.PendingViewUpdate; Render();
            var findNext = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.FindNextCommand));
            Assert.False(findNext.IsEffectivelyEnabled);
            Assert.Equal(expected, ColorOf(border.Background));
            using (var frame = window.CaptureRenderedFrame())
            using (var stream = new MemoryStream())
            {
                frame!.Save(stream);
                using var pixels = SKBitmap.Decode(stream.ToArray());
                var sample = search.TranslatePoint(new Point(search.Bounds.Width - 12, search.Bounds.Height / 2), window)!.Value;
                var pixel = pixels.GetPixel((int)sample.X, (int)sample.Y);
                Assert.Equal(expected, Color.FromArgb(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue));
            }
            Save(window, appearance == "Oscuro" ? "search-focused-dark.png" : "search-focused-light.png");
        }
        finally { window.Close(); }
    }

    private static void Save(Window window, string name)
    {
        string? artifacts = Environment.GetEnvironmentVariable("DIFFCOMPARER_TEST_ARTIFACTS");
        if (artifacts is null) return;
        Directory.CreateDirectory(artifacts);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(artifacts, name));
    }
}
