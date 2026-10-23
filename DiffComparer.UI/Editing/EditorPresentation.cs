using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DiffComparer.UI.Renderers;

namespace DiffComparer.UI.Editing;

/// <summary>Changes presentation in place, preserving caret, document and undo history.</summary>
public sealed class EditorPresentation : IDisposable
{
    private readonly Window _window;
    private readonly EditorTypography _settings;
    private readonly TextEditor[] _editors;
    public EditorPresentation(Window window, EditorTypography settings, params TextEditor[] editors)
    {
        _window = window; _settings = settings; _editors = editors;
        settings.PropertyChanged += Changed;
        window.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        foreach (var editor in editors) editor.AddHandler(InputElement.PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
        Apply();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorTypography.ZoomLabel) or nameof(EditorTypography.EffectiveFontSize)) return;
        Apply();
    }
    private void Apply()
    {
        var family = new FontFamily($"{_settings.FontName},Consolas,monospace");
        foreach (var editor in _editors)
        {
            editor.FontFamily = family;
            editor.FontSize = _settings.EffectiveFontSize;
            editor.Options.LineHeightFactor = _settings.LineSpacing;
            editor.WordWrap = _settings.WordWrap;
            foreach (var margin in editor.TextArea.LeftMargins.OfType<OriginalLineNumberMargin>())
                margin.SetTypography(family, editor.FontSize);
            editor.TextArea.TextView.Redraw();
        }
    }
    private void Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || e.Delta.Y == 0) return;
        if (e.Delta.Y > 0) _settings.ZoomInCommand.Execute(null); else _settings.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }
    private void KeyDown(object? sender, KeyEventArgs e)
    {
        // Numeric controls and font selectors keep their own keyboard behavior.
        if (e.Source is not Avalonia.Visual source ||
            !source.GetVisualAncestors().Append(source).OfType<TextEditor>().Any(editor => _editors.Contains(editor))) return;
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 || (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
        switch (e.Key)
        {
            case Key.Add: case Key.OemPlus: _settings.ZoomInCommand.Execute(null); break;
            case Key.Subtract: case Key.OemMinus: _settings.ZoomOutCommand.Execute(null); break;
            case Key.D0: case Key.NumPad0: _settings.ResetZoomCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }
    public void Dispose()
    {
        _settings.PropertyChanged -= Changed;
        _window.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        foreach (var editor in _editors) editor.RemoveHandler(InputElement.PointerWheelChangedEvent, Wheel);
    }
}
