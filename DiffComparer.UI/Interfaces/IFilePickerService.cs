using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DiffComparer.UI.Interfaces
{
    public interface IFilePickerService
    {
        Task<string?> OpenTextFileAsync();
    }
}
