using System.Threading.Tasks;
namespace DiffComparer.UI.Interfaces;

public interface IReportExportService
{
    Task<bool> SaveTextAsync(string suggestedName, string content, bool html);
}
