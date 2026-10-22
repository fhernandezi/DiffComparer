using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.Rendering;
using DiffComparer.Core;
using DiffComparer.UI.ViewModels;
using DiffComparer.UI.Renderers;

namespace DiffComparer.UI.Controls;

/// <summary>Shows the source and destination extents of each complete copy operation.</summary>
public sealed class ChangeActionGutter : Canvas
{
    private MainWindowViewModel? _model;
    private TextView? _left, _right;
    private Guid _snapshot;
    private readonly Dictionary<int, StackPanel> _buttons = new();
    private readonly List<(SectionExtent? Left, SectionExtent? Right, double Center, LineStatus Status)> _sections = new();
    private static readonly Pen Connector = new(new SolidColorBrush(Color.Parse("#906969")), 1);
    private const double ButtonHeight = 25;
    private readonly SectionDecoration _decoration;

    public ChangeActionGutter()
    {
        _decoration = new SectionDecoration(this) { IsHitTestVisible = false };
        Children.Add(_decoration);
    }

    private void ClearButtons()
    {
        foreach (var panel in _buttons.Values) Children.Remove(panel);
        _buttons.Clear();
    }

    public void Connect(MainWindowViewModel model, TextView left, TextView right)
    {
        _model = model; _left = left; _right = right;
        foreach (var view in new[] { left, right })
        {
            view.VisualLinesChanged += (_, _) => Refresh();
            view.ScrollOffsetChanged += (_, _) => Refresh();
        }
        // The gutter and TextViews have different origins (headers, margins, scrollbars).
        // Recompute after layout, rather than compensating with a fixed pixel offset.
        LayoutUpdated += (_, _) => Refresh();
    }

    private Viewport? CaptureViewport(TextView view)
    {
        if (!view.VisualLinesValid || view.TranslatePoint(default, this) is not Point origin) return null;
        var lines = view.VisualLines.Where(v => !v.FirstDocumentLine.IsDeleted).ToArray();
        return lines.Length == 0 ? null : new(view, origin, lines,
            Math.Max(0, origin.Y), Math.Min(Bounds.Height, origin.Y + view.Bounds.Height));
    }

    private SectionExtent? Extent(Viewport? viewport, LineRange range)
    {
        if (viewport is null) return null;
        var (view, origin, visuals, viewportTop, viewportBottom) = viewport;
        if (range.Count > 0 && (range.End <= visuals[0].FirstDocumentLine.LineNumber - 1
            || range.Start > visuals[^1].LastDocumentLine.LineNumber - 1)) return null;
        if (range.Count == 0)
        {
            int anchor = Math.Min(range.Start, view.Document.LineCount - 1);
            var visual = visuals.FirstOrDefault(v => v.FirstDocumentLine.LineNumber - 1 <= anchor
                && v.LastDocumentLine.LineNumber - 1 >= anchor);
            if (visual is null) return null;
            double y = origin.Y + visual.VisualTop - view.VerticalOffset
                + (range.Start >= view.Document.LineCount ? visual.Height : 0);
            return y < viewportTop || y > viewportBottom ? null : new(y, y, false, false, false);
        }
        var intersecting = visuals.Where(v => v.LastDocumentLine.LineNumber - 1 >= range.Start
            && v.FirstDocumentLine.LineNumber - 1 < range.End).ToArray();
        if (intersecting.Length == 0) return null;
        var first = intersecting[0];
        var last = intersecting[^1];
        double top = origin.Y + first.VisualTop - view.VerticalOffset;
        double bottom = origin.Y + last.VisualTop + last.Height - view.VerticalOffset;
        if (bottom <= viewportTop || top >= viewportBottom) return null;
        return new(Math.Max(top, viewportTop), Math.Min(bottom, viewportBottom), true,
            first.FirstDocumentLine.LineNumber - 1 > range.Start || top < viewportTop,
            last.LastDocumentLine.LineNumber < range.End || bottom > viewportBottom);
    }

    public void Refresh()
    {
        _decoration.Width = Bounds.Width;
        _decoration.Height = Bounds.Height;
        _sections.Clear();
        if (_model?.CanonicalResult is not { } result || !_model.IsResultCurrent || _left is null || _right is null)
        {
            ClearButtons(); _decoration.InvalidateVisual(); return;
        }
        if (_snapshot != result.Id) { ClearButtons(); _snapshot = result.Id; }
        var visible = new HashSet<int>();
        var leftViewport = CaptureViewport(_left);
        var rightViewport = CaptureViewport(_right);
        foreach (var hunk in result.Hunks)
        {
            if (!_model.View.FullyVisibleHunks.Contains(hunk.Id)) continue;
            var left = Extent(leftViewport, hunk.LeftLines);
            var right = Extent(rightViewport, hunk.RightLines);
            if (left is null && right is null) continue;
            // An empty destination marks an insertion point, not the source section's center.
            var sources = new[] { left, right }.Where(e => e is { HasText: true }).ToArray();
            double center = sources.Length > 0 ? sources.Average(e => e!.Center)
                : (left ?? right)!.Center;
            double top = Math.Clamp(center - ButtonHeight / 2, 0, Math.Max(0, Bounds.Height - ButtonHeight));
            center = top + ButtonHeight / 2;
            _sections.Add((left, right, center, hunk.LeftLines.Count == 0 ? LineStatus.Added
                : hunk.RightLines.Count == 0 ? LineStatus.Deleted : LineStatus.Modified));
            visible.Add(hunk.Id);
            if (!_buttons.TryGetValue(hunk.Id, out var panel))
            {
                panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                Button Make(string content, CopyDirection direction)
                {
                    bool toRight = direction == CopyDirection.LeftToRight;
                    var source = toRight ? hunk.LeftLines : hunk.RightLines;
                    var destination = toRight ? hunk.RightLines : hunk.LeftLines;
                    string Describe(LineRange range) => range.Count == 0 ? $"se pega antes de la línea {range.Start + 1}"
                        : range.Count == 1 ? $"línea {range.Start + 1}" : $"líneas {range.Start + 1}–{range.End}";
                    var button = new Button { Content = content, Width = 26, MinHeight = ButtonHeight, Height = ButtonHeight,
                        Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), FontSize = 16, Command = _model.ApplyChangeCommand,
                        CommandParameter = new CopyRequest(result.Id, hunk.Id, direction) };
                    ToolTip.SetTip(button, $"Copiar toda esta sección {(toRight ? "izquierda → derecha" : "derecha → izquierda")}\n"
                        + $"De: {Describe(source)}\nA: {Describe(destination)}");
                    return button;
                }
                panel.Children.Add(Make("←", CopyDirection.RightToLeft));
                panel.Children.Add(Make("→", CopyDirection.LeftToRight));
                _buttons[hunk.Id] = panel;
                Children.Add(panel);
            }
            SetLeft(panel, (Bounds.Width - 54) / 2);
            SetTop(panel, top);
        }
        foreach (int id in _buttons.Keys.ToArray())
            if (!visible.Contains(id)) { Children.Remove(_buttons[id]); _buttons.Remove(id); }
        _decoration.InvalidateVisual();
    }

    private void DrawSections(DrawingContext context)
    {
        foreach (var section in _sections)
        {
            var bracket = DiffPalette.Bracket(section.Status, ActualThemeVariant == ThemeVariant.Dark);
            void Draw(SectionExtent? extent, bool left)
            {
                if (extent is null) return;
                double x = left ? 5 : Bounds.Width - 5;
                double edge = left ? 0 : Bounds.Width;
                if (extent.HasText)
                {
                    context.DrawLine(bracket, new(x, extent.Top), new(x, extent.Bottom));
                    if (!extent.ClippedTop) context.DrawLine(bracket, new(edge, extent.Top), new(x, extent.Top));
                    if (!extent.ClippedBottom) context.DrawLine(bracket, new(edge, extent.Bottom), new(x, extent.Bottom));
                }
                else context.DrawLine(bracket, new(edge, extent.Top), new(x, extent.Top));
                double buttonEdge = left ? (Bounds.Width - 54) / 2 : (Bounds.Width + 54) / 2;
                context.DrawLine(Connector, new(x, extent.Center), new(buttonEdge, section.Center));
            }
            Draw(section.Left, true);
            Draw(section.Right, false);
        }
    }

    private sealed class SectionDecoration(ChangeActionGutter owner) : Control
    {
        public override void Render(DrawingContext context) => owner.DrawSections(context);
    }

    private sealed record Viewport(TextView View, Point Origin, VisualLine[] Lines, double Top, double Bottom);

    private sealed record SectionExtent(double Top, double Bottom, bool HasText, bool ClippedTop, bool ClippedBottom)
    {
        public double Center => (Top + Bottom) / 2;
    }
}
