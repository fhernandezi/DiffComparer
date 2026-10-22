using System.Threading.Tasks;

namespace DiffComparer.UI.Interfaces;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}
