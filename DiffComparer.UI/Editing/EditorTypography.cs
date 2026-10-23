using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiffComparer.UI.Editing;

/// <summary>Presentation preferences; documents and sessions remain independent.</summary>
public partial class EditorTypography : ObservableObject
{
    private string _fontName = "Consolas";
    private double _fontSize = 13, _zoom = 100, _lineSpacing = 1.16;
    private bool _wordWrap;
    public string FontName
    {
        get => _fontName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 200) return;
            try { _ = new FontFamily(value); } catch (ArgumentException) { return; }
            SetProperty(ref _fontName, value.Trim());
        }
    }
    public double FontSize
    {
        get => _fontSize;
        set { if (double.IsFinite(value) && SetProperty(ref _fontSize, Math.Clamp(value, 8, 48))) OnPropertyChanged(nameof(EffectiveFontSize)); }
    }
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!double.IsFinite(value) || !SetProperty(ref _zoom, Math.Clamp(value, 50, 300))) return;
            OnPropertyChanged(nameof(EffectiveFontSize));
            OnPropertyChanged(nameof(ZoomLabel));
        }
    }
    public double LineSpacing
    {
        get => _lineSpacing;
        set { if (double.IsFinite(value)) SetProperty(ref _lineSpacing, Math.Clamp(value, 1, 2.5)); }
    }
    public bool WordWrap { get => _wordWrap; set => SetProperty(ref _wordWrap, value); }
    public double EffectiveFontSize => FontSize * Zoom / 100;
    public string ZoomLabel => $"{Zoom:0}%";
    [RelayCommand] private void ZoomIn() => Zoom += 10;
    [RelayCommand] private void ZoomOut() => Zoom -= 10;
    [RelayCommand] private void ResetZoom() => Zoom = 100;
    [RelayCommand] private void ResetTypography()
    { FontName = "Consolas"; FontSize = 13; Zoom = 100; LineSpacing = 1.16; WordWrap = false; }
}
