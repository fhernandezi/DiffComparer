using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using DiffComparer.UI.Interfaces;

namespace DiffComparer.UI.Services;

public sealed class WindowClipboardService(TopLevel window) : IClipboardService
{
    public Task SetTextAsync(string text) => window.Clipboard?.SetTextAsync(text)
        ?? throw new InvalidOperationException("No se pudo acceder al portapapeles. Intenta otra vez.");
}
