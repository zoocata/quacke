using QuakeServerManager.Views;
using System.Windows;

namespace QuakeServerManager.Services
{
    public class DialogService : IDialogService
    {
        public string? ShowInputDialog(string title, string message, Window? owner = null)
        {
            var dialog = new InputDialog(title, message);
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            if (dialog.ShowDialog() == true)
            {
                return dialog.ResponseText;
            }
            return null;
        }
    }
}
