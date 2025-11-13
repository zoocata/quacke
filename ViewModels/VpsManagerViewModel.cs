using QuakeServerManager.Models;
using QuakeServerManager.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace QuakeServerManager.ViewModels
{
    public class VpsManagerViewModel : ViewModelBase
    {
        private readonly DataService _dataService;
        private VpsConnection? _selectedVpsConnection;

        public ObservableCollection<VpsConnection> VpsConnections { get; } = new();

        public VpsConnection? SelectedVpsConnection
        {
            get => _selectedVpsConnection;
            set
            {
                SetProperty(ref _selectedVpsConnection, value);
                // Additional logic will be moved here
            }
        }

        private readonly IDialogService _dialogService;
        private readonly Window? _owner;

        public VpsManagerViewModel(DataService dataService, IDialogService dialogService, Window? owner = null)
        {
            _dataService = dataService;
            _dialogService = dialogService;
            _owner = owner;
        }

        public async void LoadVpsConnections()
        {
            var connections = await _dataService.LoadVpsConnectionsAsync();
            VpsConnections.Clear();
            foreach (var connection in connections)
            {
                VpsConnections.Add(connection);
            }
        }

        public void AddVps()
        {
            var name = _dialogService.ShowInputDialog("Add VPS", "Enter VPS name:", _owner);
            if (string.IsNullOrWhiteSpace(name)) return;

            if (VpsConnections.Any(v => v.Name == name))
            {
                System.Windows.MessageBox.Show("A VPS with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var newVps = new VpsConnection { Name = name };
            VpsConnections.Add(newVps);
            SelectedVpsConnection = newVps;
        }

        public async void DeleteVps()
        {
            if (SelectedVpsConnection == null) return;

            var result = System.Windows.MessageBox.Show($"Delete VPS '{SelectedVpsConnection.Name}'?", "Confirm Delete", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var vpsName = SelectedVpsConnection.Name;
                await _dataService.DeleteVpsConnectionAsync(vpsName);
                VpsConnections.Remove(SelectedVpsConnection);
                
                // Update selection after deletion
                if (VpsConnections.Count > 0)
                {
                    // Select the first remaining VPS
                    SelectedVpsConnection = VpsConnections.First();
                }
                else
                {
                    // No VPS connections remaining
                    SelectedVpsConnection = null;
                }
            }
        }
    }
}
