using System;
using System.Threading.Tasks;
using DiffComparer.UI.Models;

namespace DiffComparer.UI.Interfaces;

public interface ITextFileSaveService
{
    Task<TextFileContent?> SaveFileAsAsync(TextFileContent content);
}
