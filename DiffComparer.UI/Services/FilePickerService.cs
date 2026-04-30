using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DiffComparer.UI.Interfaces;
using System.IO;
using System.Threading.Tasks;

namespace DiffComparer.UI.Services;

public class FilePickerService : IFilePickerService
{
    private readonly Window _window;

    public FilePickerService(Window window)
    {
        _window = window;
    }

    public async Task<string?> OpenTextFileAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Abrir archivo",
                AllowMultiple = false
            });

        var file = files.Count > 0 ? files[0] : null;

        if (file is null)
            return null;

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }
}