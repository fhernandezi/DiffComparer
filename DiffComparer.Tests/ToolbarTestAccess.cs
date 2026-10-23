using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DiffComparer.Tests;

internal static class ToolbarTestAccess
{
    private static readonly string[] Groups = ["FileToolsButton", "EditToolsButton", "ViewToolsButton", "ToolsButton"];
    public static Button Reveal(Window window, Func<Button, bool> predicate)
    {
        Dispatcher.UIThread.RunJobs();
        foreach (string group in Groups) window.FindControl<Button>(group)?.Flyout?.Hide();
        Dispatcher.UIThread.RunJobs();
        var direct = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => predicate(b)
            && !b.GetVisualAncestors().OfType<FlyoutPresenter>().Any());
        if (direct is not null && direct.IsEffectivelyVisible) return direct;
        foreach (string group in Groups)
        {
            var owner = window.FindControl<Button>(group);
            if (owner?.Flyout is not Flyout flyout) continue;
            flyout.ShowAt(owner);
            Dispatcher.UIThread.RunJobs();
            if (flyout.Content is Control content)
            {
                var match = content.GetVisualDescendants().Prepend(content).OfType<Button>().FirstOrDefault(predicate);
                if (match is not null) return match;
            }
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
        }
        throw new InvalidOperationException("Toolbar action was not found.");
    }
    public static void RevealView(Window window)
    {
        var owner = window.FindControl<Button>("ViewToolsButton")!;
        owner.Flyout!.ShowAt(owner);
        Dispatcher.UIThread.RunJobs();
    }
}
