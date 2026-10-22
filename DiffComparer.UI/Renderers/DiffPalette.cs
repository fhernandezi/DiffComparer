using Avalonia.Media;
using DiffComparer.Core;

namespace DiffComparer.UI.Renderers;

internal static class DiffPalette
{
    private static readonly IBrush[] DarkLines = { B("#304C3D"), B("#50333F"), B("#494331") };
    private static readonly IBrush[] LightLines = { B("#E0F2E3"), B("#FBE6E8"), B("#F8EDCF") };
    private static readonly IBrush[] DarkFragments = { B("#3C634D"), B("#704352"), B("#665B39") };
    private static readonly IBrush[] LightFragments = { B("#B9DFC2"), B("#F2C3CA"), B("#EAD79E") };
    private static readonly Pen[] DarkBrackets = { P("#A1D7AE"), P("#F0AFB8"), P("#E6CF95") };
    private static readonly Pen[] LightBrackets = { P("#28653B"), P("#963D4C"), P("#775815") };
    private static IBrush B(string color) => new SolidColorBrush(Color.Parse(color));
    private static Pen P(string color) => new(B(color), 1.5);
    private static int Index(LineStatus status) => status == LineStatus.Added ? 0 : status == LineStatus.Deleted ? 1 : 2;
    public static readonly IBrush DarkForeground = B("#E5E9F2");
    public static readonly IBrush LightForeground = B("#202736");
    public static readonly IBrush DarkMuted = B("#939FB3");
    public static readonly IBrush LightMuted = B("#667185");
    public static readonly IBrush DarkSearch = B("#285767");
    public static readonly IBrush LightSearch = B("#BCE8EF");
    private static readonly Pen DarkSelection = P("#AAC7FF");
    private static readonly Pen LightSelection = P("#415F91");
    public static IBrush Foreground(bool dark) => dark ? DarkForeground : LightForeground;
    public static IBrush Muted(bool dark) => dark ? DarkMuted : LightMuted;
    public static IBrush Search(bool dark) => dark ? DarkSearch : LightSearch;
    public static Pen Selection(bool dark) => dark ? DarkSelection : LightSelection;
    public static IBrush Line(LineStatus status, bool dark = true) => (dark ? DarkLines : LightLines)[Index(status)];
    public static IBrush Fragment(LineStatus status, bool dark = true) => (dark ? DarkFragments : LightFragments)[Index(status)];
    public static Pen Bracket(LineStatus status, bool dark = true) => (dark ? DarkBrackets : LightBrackets)[Index(status)];
}
