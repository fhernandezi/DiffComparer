using DiffComparer.UI.Interfaces;

namespace DiffComparer.Tests.Fakes;

public sealed class FakeFilePickerService : IFilePickerService
{
    public string? NextFileContent { get; set; }

    public Task<string?> OpenTextFileAsync()
    {
        return Task.FromResult(NextFileContent);
    }
}