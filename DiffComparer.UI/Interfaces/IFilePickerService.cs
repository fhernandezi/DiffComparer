using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DiffComparer.UI.Interfaces
{
    public interface IFilePickerService
    {
        Task<string?> OpenTextFileAsync();

        async Task<DiffComparer.UI.Models.TextFileContent?> OpenFileAsync(string encodingName = "Automático")
        {
            var text = await OpenTextFileAsync();
            return text is null ? null : new(text, "Texto", "", "Desconocida",
                DiffComparer.UI.Models.TextFileContent.DetectLineEndings(text));
        }
    }
}
