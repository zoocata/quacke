using QuakeServerManager.Models;
using QuakeServerManager.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;

namespace QuakeServerManager.ViewModels
{
    public class ServerManagerViewModel : ViewModelBase
    {
        private readonly DataService _dataService;
        private ServerInstance? _selectedServerInstance;

        public ObservableCollection<ServerInstance> ServerInstances { get; } = new();

        public ServerInstance? SelectedServerInstance
        {
            get => _selectedServerInstance;
            set
            {
                SetProperty(ref _selectedServerInstance, value);

                // Update RCON password box when selection changes
                if (_owner is MainWindow mainWindow && value != null)
                {
                    mainWindow.UpdateRconPasswordBox(value.RconPassword ?? string.Empty);
                }
            }
        }

        private readonly IDialogService _dialogService;
        private readonly Window? _owner;
        private readonly MainViewModel? _mainViewModel;

        public ServerManagerViewModel(DataService dataService, IDialogService dialogService, Window? owner = null, MainViewModel? mainViewModel = null)
        {
            _dataService = dataService;
            _dialogService = dialogService;
            _owner = owner;
            _mainViewModel = mainViewModel;
            
            // Subscribe to collection changes to notify the main view model
            ServerInstances.CollectionChanged += (s, e) =>
            {
                _mainViewModel?.OnPropertyChanged(nameof(MainViewModel.CanRetainInitialValues));
            };
        }

        public async void LoadServerInstances(string vpsName)
        {
            var instances = await _dataService.LoadServerInstancesAsync(vpsName);
            ServerInstances.Clear();
            foreach (var instance in instances)
            {
                ServerInstances.Add(instance);
            }
            
            // Auto-select the first instance if any are loaded
            if (ServerInstances.Count > 0)
            {
                SelectedServerInstance = ServerInstances.First();
            }
            else
            {
                SelectedServerInstance = null;
            }
        }

        public async void AddInstance(string vpsName)
        {
            var name = _dialogService.ShowInputDialog("Add Instance", "Enter instance name:", _owner);
            if (string.IsNullOrWhiteSpace(name)) return;

            if (ServerInstances.Any(i => i.Name == name))
            {
                System.Windows.MessageBox.Show("An instance with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var newInstance = new ServerInstance { Name = name, ServerName = name };
            
            // If retain initial values is enabled and there are existing instances, copy values from the first instance
            if (_mainViewModel?.RetainInitialValues == true && ServerInstances.Count > 0)
            {
                var firstInstance = ServerInstances.First();
                newInstance.Admin = firstInstance.Admin;
                newInstance.Location = firstInstance.Location;
                newInstance.Port = firstInstance.Port;
                newInstance.MaxClients = firstInstance.MaxClients;
                newInstance.RconPassword = firstInstance.RconPassword;
                newInstance.Map = firstInstance.Map;
                newInstance.GameType = firstInstance.GameType;
                // Note: ServerName and class selection are not copied as per the design
            }
            
            ServerInstances.Add(newInstance);
            SelectedServerInstance = newInstance;
            await _dataService.SaveServerInstanceAsync(vpsName, newInstance);
        }

        public async void DeleteInstance(string vpsName)
        {
            if (SelectedServerInstance == null) return;

            var result = System.Windows.MessageBox.Show($"Delete instance '{SelectedServerInstance.Name}'?", "Confirm Delete", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var instanceName = SelectedServerInstance.Name;
                await _dataService.DeleteServerInstanceAsync(vpsName, instanceName);
                ServerInstances.Remove(SelectedServerInstance);
                
                // Update selection after deletion
                if (ServerInstances.Count > 0)
                {
                    // Select the first remaining instance
                    SelectedServerInstance = ServerInstances.First();
                }
                else
                {
                    // No instances remaining
                    SelectedServerInstance = null;
                }
            }
        }
    }
}
