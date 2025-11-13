using System.Windows;

namespace QuakeServerManager.Services
{
    public interface IDialogService
    {
        string? ShowInputDialog(string title, string message, Window? owner = null);
    }
}
