using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using QuakeServerManager.Models;
using QuakeServerManager.Services;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace QuakeServerManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DataService _dataService;
        private readonly SshService _sshService;
        private VpsConnection? _selectedVpsConnection;
        private ServerInstance? _selectedServerInstance;
        private string _q3Path = string.Empty;
        private string _statusMessage = "Ready";
        private string _terminalOutput = string.Empty;
        private bool _isPasswordAuth = true;
        private bool _isQ3PathValid = false;
        private string _q3ValidationMessage = string.Empty;
        private bool _retainInitialValues = false;
        private DeploymentState? _currentDeploymentState;
        private bool _isDeploying = false;
        private double _deploymentProgress = 0.0;
        private string _deploymentStatus = string.Empty;

        public MainViewModel()
        {
            _dataService = new DataService();
            _sshService = new SshService();
            
            // Connect SSH service logging to terminal output
            _sshService.LogMessage += (message) => LogToTerminal(message);
            
            VpsConnections = new ObservableCollection<VpsConnection>();
            ServerInstances = new ObservableCollection<ServerInstance>();
            AvailableClasses = new ObservableCollection<Class>();

            InitializeCommands();
            InitializeClasses();
            LoadDataAsync();
        }

        public ObservableCollection<VpsConnection> VpsConnections { get; }
        public ObservableCollection<ServerInstance> ServerInstances { get; }
        public ObservableCollection<Class> AvailableClasses { get; }

        public VpsConnection? SelectedVpsConnection
        {
            get => _selectedVpsConnection;
            set
            {
                SetProperty(ref _selectedVpsConnection, value);
                
                if (value == null)
                {
                    // Clear all imported state when no VPS is selected
                    ServerInstances.Clear();
                    SelectedServerInstance = null;
                    CurrentDeploymentState = null;
                    RetainInitialValues = false;
                }
                else
                {
                    // Load server instances for the selected VPS
                    LoadServerInstancesAsync();
                }
                
                OnPropertyChanged(nameof(CanRetainInitialValues));
                
                // Update password box when VPS selection changes
                if (value != null && App.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.UpdatePasswordBox(value.Password);
                }
                
                // Save the selected connection to settings
                SaveSelectedConnectionToSettings();
            }
        }

        public ServerInstance? SelectedServerInstance
        {
            get => _selectedServerInstance;
            set
            {
                // Unsubscribe from previous instance's PropertyChanged event
                if (_selectedServerInstance != null)
                {
                    _selectedServerInstance.PropertyChanged -= OnSelectedServerInstancePropertyChanged;
                }

                if (SetProperty(ref _selectedServerInstance, value))
                {
                    // Subscribe to new instance's PropertyChanged event
                    if (_selectedServerInstance != null)
                    {
                        _selectedServerInstance.PropertyChanged += OnSelectedServerInstancePropertyChanged;
                    }
                    
                    // Notify that CanRetainInitialValues may have changed
                    OnPropertyChanged(nameof(CanRetainInitialValues));
                    
                    // Raise CanExecuteChanged for server control commands when selection changes
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string Q3Path
        {
            get => _q3Path;
            set
            {
                if (SetProperty(ref _q3Path, value))
                {
                    ValidateQ3Path();
                    // Save to settings when Q3Path is changed (but not during initial load)
                    if (!string.IsNullOrEmpty(value))
                    {
                        _ = SaveQ3PathToSettings();
                    }
                }
            }
        }

        public bool IsQ3PathValid
        {
            get => _isQ3PathValid;
            set => SetProperty(ref _isQ3PathValid, value);
        }

        public string Q3ValidationMessage
        {
            get => _q3ValidationMessage;
            set => SetProperty(ref _q3ValidationMessage, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string TerminalOutput
        {
            get => _terminalOutput;
            set => SetProperty(ref _terminalOutput, value);
        }

        public bool IsPasswordAuth
        {
            get => _isPasswordAuth;
            set
            {
                SetProperty(ref _isPasswordAuth, value);
                OnPropertyChanged(nameof(PasswordVisibility));
                OnPropertyChanged(nameof(KeyVisibility));
            }
        }

        public bool IsKeyAuth
        {
            get => !_isPasswordAuth;
            set
            {
                IsPasswordAuth = !value;
            }
        }

        public Visibility PasswordVisibility => IsPasswordAuth ? Visibility.Visible : Visibility.Collapsed;
        public Visibility KeyVisibility => IsKeyAuth ? Visibility.Visible : Visibility.Collapsed;

        public bool RetainInitialValues
        {
            get => _retainInitialValues;
            set
            {
                if (SetProperty(ref _retainInitialValues, value))
                {
                    OnPropertyChanged(nameof(CanRetainInitialValues));
                }
            }
        }

        public bool CanRetainInitialValues => SelectedServerInstance != null && 
                                            ServerInstances.Count > 0 && 
                                            ServerInstances.IndexOf(SelectedServerInstance) == 0;

        public DeploymentState? CurrentDeploymentState
        {
            get => _currentDeploymentState;
            set => SetProperty(ref _currentDeploymentState, value);
        }

        public bool IsDeploying
        {
            get => _isDeploying;
            set => SetProperty(ref _isDeploying, value);
        }

        public double DeploymentProgress
        {
            get => _deploymentProgress;
            set => SetProperty(ref _deploymentProgress, value);
        }

        public string DeploymentStatus
        {
            get => _deploymentStatus;
            set => SetProperty(ref _deploymentStatus, value);
        }

        // Commands
        public ICommand AddVpsCommand { get; private set; } = null!;
        public ICommand DeleteVpsCommand { get; private set; } = null!;
        public ICommand TestConnectionCommand { get; private set; } = null!;
        public ICommand BrowseKeyCommand { get; private set; } = null!;
        public ICommand AddInstanceCommand { get; private set; } = null!;
        public ICommand DeleteInstanceCommand { get; private set; } = null!;
        public ICommand BrowseQ3PathCommand { get; private set; } = null!;
        public ICommand DeployServerCommand { get; private set; } = null!;
        public ICommand StartServerCommand { get; private set; } = null!;
        public ICommand StopServerCommand { get; private set; } = null!;
        public ICommand RestartServerCommand { get; private set; } = null!;
        public ICommand SyncMapsCommand { get; private set; } = null!;
        public ICommand ImportExistingConfigurationsCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            AddVpsCommand = new RelayCommand(AddVps);
            DeleteVpsCommand = new RelayCommand(DeleteVps, CanDeleteVps);
            TestConnectionCommand = new RelayCommand(TestConnection, CanTestConnection);
            BrowseKeyCommand = new RelayCommand(BrowseKey);
            AddInstanceCommand = new RelayCommand(AddInstance, CanAddInstance);
            DeleteInstanceCommand = new RelayCommand(DeleteInstance, CanDeleteInstance);
            BrowseQ3PathCommand = new RelayCommand(BrowseQ3Path);
            DeployServerCommand = new RelayCommand(DeployServer, CanDeployServer);
            StartServerCommand = new RelayCommand(StartServer, CanControlServer);
            StopServerCommand = new RelayCommand(StopServer, CanControlServer);
            RestartServerCommand = new RelayCommand(RestartServer, CanControlServer);
            SyncMapsCommand = new RelayCommand(SyncMaps, CanSyncMaps);
            ImportExistingConfigurationsCommand = new RelayCommand(ImportExistingConfigurations, CanImportExistingConfigurations);
        }

        private void InitializeClasses()
        {
            AvailableClasses.Clear();
            
            // Scout class (based on scout.cfg)
            AvailableClasses.Add(new Class 
            { 
                Name = "Scout",
                Model = "slash",
                BaseSpeed = 320,
                SpawnHealth = 100,
                MaxArmour = 50,
                ArmourClass = 0,
                DoubleJump = true,
                RampJump = true,
                Weapon3 = "15,25,5,10", // Shotgun
                Weapon4 = "3,6,1,3",    // Machine Gun
                StartingWeapon = 3,
                FileName = "scout.cfg"
            });
            
            // Fighter class (based on fighter.cfg)
            AvailableClasses.Add(new Class 
            { 
                Name = "Fighter",
                Model = "sarge",
                BaseSpeed = 280,
                SpawnHealth = 100,
                MaxArmour = 100,
                ArmourClass = 1,
                DoubleJump = true,
                RampJump = false,
                Weapon3 = "10,25,5,10", // Shotgun
                Weapon5 = "5,25,5,10",  // Grenade Launcher
                Weapon8 = "50,100,25,50", // Plasma Gun
                StartingWeapon = 3,

                FileName = "fighter.cfg"
            });
            
            // Sniper class (based on sniper.cfg)
            AvailableClasses.Add(new Class 
            { 
                Name = "Sniper",
                Model = "visor",
                BaseSpeed = 280,
                SpawnHealth = 100,
                MaxArmour = 50,
                ArmourClass = 1,
                DoubleJump = true,
                RampJump = false,
                Weapon2 = "100,100,25,50", // Railgun
                Weapon7 = "10,25,5,10",    // Lightning Gun
                StartingWeapon = 3,
                FileName = "sniper.cfg"
            });
            
            // Tank class (based on tank.cfg)
            AvailableClasses.Add(new Class 
            { 
                Name = "Tank",
                Model = "keel",
                BaseSpeed = 240,
                SpawnHealth = 100,
                MaxArmour = 150,
                ArmourClass = 2,
                DoubleJump = false,
                RampJump = false,
                Weapon3 = "10,25,5,10",   // Shotgun
                Weapon5 = "10,25,5,10",   // Grenade Launcher
                Weapon6 = "50,150,25,50", // Rocket Launcher
                StartingWeapon = 3,
                FileName = "tank.cfg"
            });
        }

        public void LogToTerminal(string message)
        {
            // Ensure we're on the UI thread when updating the terminal
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                TerminalOutput += $"[{timestamp}] {message}\n";
            });
        }

        private async void LoadDataAsync()
        {
            try
            {
                var connections = await _dataService.LoadVpsConnectionsAsync();
                VpsConnections.Clear();
                foreach (var connection in connections)
                {
                    VpsConnections.Add(connection);
                }

                var settings = await _dataService.LoadSettingsAsync();
                Q3Path = settings.LastQ3Path;
                
                // Validate Q3 path on startup
                ValidateQ3Path();
                
                if (!IsQ3PathValid && !string.IsNullOrWhiteSpace(Q3Path))
                {
                    LogToTerminal("Warning: Invalid Q3 installation path detected. Please set a valid path in Settings.");
                }
                
                // Restore last selected connection and load its server instances
                if (!string.IsNullOrWhiteSpace(settings.LastSelectedConnection))
                {
                    var lastConnection = VpsConnections.FirstOrDefault(c => c.Name == settings.LastSelectedConnection);
                    if (lastConnection != null)
                    {
                        SelectedVpsConnection = lastConnection;
                        LogToTerminal($"Restored last selected connection: {lastConnection.Name}");
                    }
                }
                
                LogToTerminal("Application started successfully.");
            }
            catch (Exception ex)
            {
                LogToTerminal($"Error loading data: {ex.Message}");
                System.Windows.MessageBox.Show($"Error loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadServerInstancesAsync()
        {
            if (SelectedVpsConnection == null) return;

            try
            {
                var instances = await _dataService.LoadServerInstancesAsync(SelectedVpsConnection.Name);
                ServerInstances.Clear();
                foreach (var instance in instances)
                {
                    ServerInstances.Add(instance);
                }
                OnPropertyChanged(nameof(CanRetainInitialValues));
                
                // Load deployment state
                CurrentDeploymentState = await _dataService.LoadDeploymentStateAsync(SelectedVpsConnection.Name);
                
                // Update IsDeployed status for each instance based on local state
                foreach (var instance in ServerInstances)
                {
                    instance.IsDeployed = CurrentDeploymentState?.DeployedInstances?.Contains(instance.Name) ?? false;
                }
                
                LogToTerminal($"Loaded {instances.Count} server instances for {SelectedVpsConnection.Name}");
                LogToTerminal($"Deployment state: {CurrentDeploymentState?.DeployedInstances?.Count ?? 0} deployed instances");
                
                // Force UI refresh
                OnPropertyChanged(nameof(ServerInstances));
                CommandManager.InvalidateRequerySuggested();
                
                // Automatically sync deployment state with server to ensure accuracy
                // Run this in the background to avoid blocking the UI
                _ = Task.Run(async () =>
                {
                    // Small delay to ensure UI is fully loaded first
                    await Task.Delay(500);
                    await SyncDeploymentStateWithServerAsync();
                });
            }
            catch (Exception ex)
            {
                LogToTerminal($"Error loading instances: {ex.Message}");
                System.Windows.MessageBox.Show($"Error loading instances: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddVps()
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox("Enter VPS name:", "Add VPS", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            if (VpsConnections.Any(v => v.Name == name))
            {
                System.Windows.MessageBox.Show("A VPS with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var newVps = new VpsConnection { Name = name };
            VpsConnections.Add(newVps);
            SelectedVpsConnection = newVps;
            
            LogToTerminal($"Added new VPS: {name}");
        }

        private async void DeleteVps()
        {
            if (SelectedVpsConnection == null) return;

            var result = System.Windows.MessageBox.Show($"Delete VPS '{SelectedVpsConnection.Name}'?", "Confirm Delete", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Store the VPS name for logging before deletion
                var vpsName = SelectedVpsConnection.Name;
                
                // Clear all imported state before deleting the VPS
                ServerInstances.Clear();
                SelectedServerInstance = null;
                CurrentDeploymentState = null;
                RetainInitialValues = false;
                
                // Reset class configurations to default
                InitializeClasses();
                
                // Delete the VPS connection from data service
                await _dataService.DeleteVpsConnectionAsync(vpsName);
                
                // Remove from UI collection
                VpsConnections.Remove(SelectedVpsConnection);
                SelectedVpsConnection = null;
                
                LogToTerminal($"VPS '{vpsName}' deleted successfully. All imported server configurations, deployment state, and class configurations have been cleared.");
            }
        }

                private bool CanDeleteVps() => SelectedVpsConnection != null;

        private async void TestConnection()
        {
            if (SelectedVpsConnection == null) return;

            StatusMessage = "Testing connection...";
            LogToTerminal($"Testing SSH connection to {SelectedVpsConnection.Ip}:{SelectedVpsConnection.Port}...");
            
            var success = await _sshService.TestConnectionAsync(SelectedVpsConnection);
            
            if (success)
            {
                // If connection test succeeds, automatically save the connection
                try
                {
                    await _dataService.SaveVpsConnectionAsync(SelectedVpsConnection);
                    StatusMessage = "Connection successful and saved!";
                    LogToTerminal("SSH connection test successful and connection saved!");
                    
                    // Check for existing CPMA installation and import configurations
                    await CheckAndImportExistingConfigurationsAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "Connection successful but failed to save!";
                    LogToTerminal($"SSH connection test successful but failed to save: {ex.Message}");
                    System.Windows.MessageBox.Show($"SSH connection test successful but failed to save: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                StatusMessage = "Connection failed!";
                LogToTerminal("SSH connection test failed!");
                System.Windows.MessageBox.Show("SSH connection test failed. Please check your settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CheckAndImportExistingConfigurationsAsync()
        {
            if (SelectedVpsConnection == null) return;

            try
            {
                LogToTerminal("Checking for existing CPMA installation...");
                StatusMessage = "Checking for existing CPMA installation...";
                
                var hasExistingCpma = await _sshService.CheckForExistingCpmaInstallationAsync(SelectedVpsConnection);
                
                if (hasExistingCpma)
                {
                    LogToTerminal("Existing CPMA installation detected! Checking for server configurations...");
                    StatusMessage = "Importing existing server configurations...";
                    
                    var result = System.Windows.MessageBox.Show(
                        "An existing CPMA installation was detected on this server. Would you like to import the existing server configurations?",
                        "Import Existing Configurations",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        LogToTerminal("User chose to import existing configurations. Starting import process...");
                        
                                                    var importedInstances = await _sshService.ImportExistingServerConfigurationsAsync(SelectedVpsConnection);
                        
                        if (importedInstances.Count > 0)
                        {
                            // Clear existing instances and add imported ones
                            ServerInstances.Clear();
                            
                            foreach (var instance in importedInstances)
                            {
                                ServerInstances.Add(instance);
                                // Save each imported instance
                                await _dataService.SaveServerInstanceAsync(SelectedVpsConnection.Name, instance);
                            }
                            
                            // Set focus on the first imported instance
                            if (importedInstances.Count > 0)
                            {
                                SelectedServerInstance = importedInstances[0];
                            }
                            
                            // Update deployment state
                            var deploymentState = new DeploymentState
                            {
                                VpsConnectionName = SelectedVpsConnection.Name,
                                DeployedInstances = importedInstances.Select(i => i.Name).ToList(),
                                LastDeploymentSync = DateTime.Now
                            };
                            await _dataService.SaveDeploymentStateAsync(SelectedVpsConnection.Name, deploymentState);
                            CurrentDeploymentState = deploymentState;
                            
                            LogToTerminal($"Successfully imported {importedInstances.Count} server configurations!");
                            
                            // Also import class configurations
                            try
                            {
                                LogToTerminal("Importing class configurations...");
                                var importedClasses = await _sshService.ImportClassConfigurationsAsync(SelectedVpsConnection);
                                
                                if (importedClasses.Count > 0)
                                {
                                    AvailableClasses.Clear();
                                    foreach (var cls in importedClasses)
                                    {
                                        AvailableClasses.Add(cls);
                                    }
                                    LogToTerminal($"Successfully imported {importedClasses.Count} class configurations!");
                                    StatusMessage = $"Imported {importedInstances.Count} server configurations and {importedClasses.Count} class configurations!";
                                    
                                    System.Windows.MessageBox.Show(
                                        $"Successfully imported {importedInstances.Count} server configurations and {importedClasses.Count} class configurations from the existing CPMA installation.\n\n" +
                                        "You can now edit these configurations locally and redeploy them to update the server settings.",
                                        "Import Successful",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                                }
                                else
                                {
                                    LogToTerminal("No class configurations found to import.");
                                    StatusMessage = $"Imported {importedInstances.Count} server configurations!";
                                    
                                    System.Windows.MessageBox.Show(
                                        $"Successfully imported {importedInstances.Count} server configurations from the existing CPMA installation.\n\n" +
                                        "No class configurations were found to import.",
                                        "Import Successful",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                                }
                            }
                            catch (Exception classEx)
                            {
                                LogToTerminal($"Warning: Server configurations imported successfully, but class import failed: {classEx.Message}");
                                StatusMessage = $"Imported {importedInstances.Count} server configurations!";
                                
                                System.Windows.MessageBox.Show(
                                    $"Successfully imported {importedInstances.Count} server configurations from the existing CPMA installation.\n\n" +
                                    $"Class configuration import failed: {classEx.Message}",
                                    "Import Partially Successful",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            LogToTerminal("No server configurations found to import.");
                            StatusMessage = "No existing configurations found.";
                            
                            System.Windows.MessageBox.Show(
                                "No existing server configurations were found to import.",
                                "No Configurations Found",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        LogToTerminal("User chose not to import existing configurations.");
                        StatusMessage = "Connection successful - no import performed.";
                    }
                }
                else
                {
                    LogToTerminal("No existing CPMA installation found. Ready for fresh deployment.");
                    StatusMessage = "Connection successful - ready for deployment.";
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"Error during configuration import check: {ex.Message}");
                StatusMessage = "Connection successful but import check failed.";
                
                System.Windows.MessageBox.Show(
                    $"Connection was successful, but there was an error checking for existing configurations: {ex.Message}",
                    "Import Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private bool CanTestConnection() => SelectedVpsConnection != null && 
            !string.IsNullOrWhiteSpace(SelectedVpsConnection.Ip);

        private void BrowseKey()
        {
            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select Private Key File",
                Filter = "Private Key Files (*.pem;*.key)|*.pem;*.key|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (SelectedVpsConnection != null)
                {
                    SelectedVpsConnection.PrivateKeyPath = dialog.FileName;
                    LogToTerminal($"Selected private key: {dialog.FileName}");
                }
            }
        }

        private async void AddInstance()
        {
            if (SelectedVpsConnection == null) return;

            var name = Microsoft.VisualBasic.Interaction.InputBox("Enter instance name:", "Add Instance", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            if (ServerInstances.Any(i => i.Name == name))
            {
                System.Windows.MessageBox.Show("An instance with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get location from existing instances, or use default
            var existingLocation = ServerInstances.FirstOrDefault()?.Location ?? "Unknown";

            var newInstance = new ServerInstance 
            { 
                Name = name,
                ServerName = name, // Set default server name to instance name
                Admin = "admin",
                Location = existingLocation, // Carry over location from existing instances
                MaxClients = 24,
                Port = 27960
            };

            // If RetainInitialValues is enabled and there's a first instance, copy its values (except ServerName)
            if (RetainInitialValues && ServerInstances.Count > 0)
            {
                var firstInstance = ServerInstances[0];
                newInstance.Admin = firstInstance.Admin;
                newInstance.Location = firstInstance.Location;
                newInstance.MaxClients = firstInstance.MaxClients;
                newInstance.Port = firstInstance.Port;
                newInstance.RconPassword = firstInstance.RconPassword;
                newInstance.SelectedClass = firstInstance.SelectedClass;
            }

                    ServerInstances.Add(newInstance);
        SelectedServerInstance = newInstance;
        OnPropertyChanged(nameof(CanRetainInitialValues));
            
            // Save the new instance immediately
            await _dataService.SaveServerInstanceAsync(SelectedVpsConnection.Name, newInstance);
            
            LogToTerminal($"Added new server instance: {name}");
        }

        private bool CanAddInstance() => SelectedVpsConnection != null;

        private async void DeleteInstance()
        {
            if (SelectedServerInstance == null || SelectedVpsConnection == null) return;

            var result = System.Windows.MessageBox.Show($"Delete instance '{SelectedServerInstance.Name}'?", "Confirm Delete", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var instanceName = SelectedServerInstance.Name;
                await _dataService.DeleteServerInstanceAsync(SelectedVpsConnection.Name, instanceName);
                ServerInstances.Remove(SelectedServerInstance);
                SelectedServerInstance = null;
                OnPropertyChanged(nameof(CanRetainInitialValues));
                
                // Update deployment state to remove the deleted instance
                if (CurrentDeploymentState != null)
                {
                    CurrentDeploymentState.DeployedInstances.RemoveAll(i => i == instanceName);
                    await _dataService.SaveDeploymentStateAsync(SelectedVpsConnection.Name, CurrentDeploymentState);
                }
                
                LogToTerminal("Server instance deleted successfully.");
            }
        }

        private bool CanDeleteInstance() => SelectedServerInstance != null && SelectedVpsConnection != null;

        private async void BrowseQ3Path()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Quake III Installation Folder"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedPath = dialog.SelectedPath;
                
                // Validate the selected folder
                if (Q3ValidationService.IsValidQ3Folder(selectedPath))
                {
                    Q3Path = selectedPath;
                    LogToTerminal($"Selected valid Q3 path: {selectedPath}");
                    System.Windows.MessageBox.Show("Valid Quake III installation folder selected!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Save the Q3 path to settings
                    await SaveQ3PathToSettings();
                }
                else
                {
                    var errorMessage = Q3ValidationService.GetValidationErrorMessage(selectedPath);
                    LogToTerminal($"Invalid Q3 folder selected: {selectedPath}");
                    System.Windows.MessageBox.Show(errorMessage, "Invalid Q3 Installation", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private async void DeployServer()
        {
            if (SelectedVpsConnection == null || ServerInstances.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select a VPS connection with server instances.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!IsQ3PathValid)
            {
                System.Windows.MessageBox.Show("Please set a valid Q3 installation path in Settings first.", "Invalid Q3 Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Initialize progress tracking
            IsDeploying = true;
            DeploymentProgress = 0.0;
            DeploymentStatus = "Initializing deployment...";
            StatusMessage = "Deploying servers...";
            LogToTerminal($"Starting deployment of all server instances to {SelectedVpsConnection.Ip}...");
            
            try
            {
                // Run deployment on background thread to prevent UI freezing
                var success = await Task.Run(async () =>
                {
                    try
                    {
                        // First, cleanup any orphaned instances
                        DeploymentProgress = 5.0;
                        DeploymentStatus = "Checking for orphaned instances...";
                        LogToTerminal("Checking for orphaned instances...");
                        var currentInstanceNames = ServerInstances.Select(i => i.Name).ToList();
                        var cleanupSuccess = await _sshService.CleanupOrphanedInstancesAsync(SelectedVpsConnection, currentInstanceNames);
                        if (cleanupSuccess)
                        {
                            LogToTerminal("Orphaned instances cleanup completed.");
                        }
                        else
                        {
                            LogToTerminal("Warning: Orphaned instances cleanup failed, but continuing with deployment.");
                        }

                        // Update deployment state
                        if (CurrentDeploymentState != null)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                CurrentDeploymentState.DeployedInstances.Clear();
                            });
                        }

                        // Setup VPS environment (once per VPS)
                        DeploymentProgress = 10.0;
                        DeploymentStatus = "Setting up VPS environment...";
                        LogToTerminal("Setting up VPS environment (base directories, packages, Q3 files)...");
                        
                        var vpsSetupSuccess = await _sshService.SetupVpsEnvironmentAsync(SelectedVpsConnection, Q3Path, 
                            (progress, status) =>
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    var scaledProgress = 10.0 + (30.0 * progress / 100.0);
                                    DeploymentProgress = scaledProgress;
                                    DeploymentStatus = $"[VPS Setup] {status}";
                                });
                            });

                        if (!vpsSetupSuccess)
                        {
                            LogToTerminal("VPS environment setup failed. Aborting deployment.");
                            return false;
                        }

                        LogToTerminal("VPS environment setup completed successfully.");

                        // Deploy all instances (instance-specific operations only)
                        var totalInstances = ServerInstances.Count;
                        var deployedCount = 0;
                        var allSuccess = true;

                        foreach (var instance in ServerInstances)
                        {
                            var instanceStartProgress = 40.0 + (60.0 * deployedCount / totalInstances);
                            var instanceEndProgress = 40.0 + (60.0 * (deployedCount + 1) / totalInstances);

                            LogToTerminal($"Deploying instance {deployedCount + 1}/{totalInstances}: {instance.Name}");
                            DeploymentStatus = $"Deploying {instance.Name}...";
                            
                            var instanceSuccess = await _sshService.DeployServerAsync(SelectedVpsConnection, instance, Q3Path, 
                                (progress, status) =>
                                {
                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        var scaledProgress = instanceStartProgress + (instanceEndProgress - instanceStartProgress) * progress / 100.0;
                                        DeploymentProgress = scaledProgress;
                                        DeploymentStatus = $"[{instance.Name}] {status}";
                                    });
                                });

                            if (instanceSuccess)
                            {
                                LogToTerminal($"Successfully deployed {instance.Name}");
                                
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    instance.IsDeployed = true;
                                    
                                    // Update deployment state
                                    if (CurrentDeploymentState != null)
                                    {
                                        if (!CurrentDeploymentState.DeployedInstances.Contains(instance.Name))
                                        {
                                            CurrentDeploymentState.DeployedInstances.Add(instance.Name);
                                        }
                                    }
                                    
                                    // Force UI refresh for the ComboBox
                                    OnPropertyChanged(nameof(ServerInstances));
                                });
                            }
                            else
                            {
                                LogToTerminal($"Failed to deploy {instance.Name}");
                                allSuccess = false;
                            }

                            deployedCount++;
                        }

                        if (CurrentDeploymentState != null)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                CurrentDeploymentState.LastDeploymentSync = DateTime.Now;
                            });
                        }

                        return allSuccess;
                    }
                    catch (Exception ex)
                    {
                        LogToTerminal($"Deployment error: {ex.Message}");
                        return false;
                    }
                });
                
                if (success)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        DeploymentProgress = 100.0;
                        DeploymentStatus = "All deployments completed successfully!";
                        StatusMessage = "All servers deployed successfully!";
                    });
                    
                    LogToTerminal("All server deployments completed successfully!");
                    
                    // Force UI refresh
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerInstances));
                        CommandManager.InvalidateRequerySuggested();
                    });
                    
                    // Add a small delay and then force refresh again to ensure UI updates
                    await Task.Delay(100);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerInstances));
                    });
                    
                    // Save deployment state
                    await _dataService.SaveDeploymentStateAsync(SelectedVpsConnection.Name, CurrentDeploymentState);
                    
                    // Show success message and bring window to front
                    var result = System.Windows.MessageBox.Show($"All server instances deployed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Bring the main window to front after showing the message
                    if (App.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.Activate();
                        mainWindow.Focus();
                    }
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        DeploymentProgress = 0.0;
                        DeploymentStatus = "Deployment failed!";
                        StatusMessage = "Server deployment failed!";
                    });
                    
                    LogToTerminal("Server deployment failed!");
                    
                    // Force UI refresh
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerInstances));
                        CommandManager.InvalidateRequerySuggested();
                    });
                    
                    // Add a small delay and then force refresh again to ensure UI updates
                    await Task.Delay(100);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerInstances));
                    });
                    
                    // Save deployment state even if failed
                    await _dataService.SaveDeploymentStateAsync(SelectedVpsConnection.Name, CurrentDeploymentState);
                    
                    System.Windows.MessageBox.Show("Server deployment failed. Please check your settings and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                // Reset progress after a short delay to show completion
                await Task.Delay(2000);
                IsDeploying = false;
                DeploymentProgress = 0.0;
                DeploymentStatus = string.Empty;
            }
        }

        private bool CanDeployServer() => SelectedVpsConnection != null && 
            SelectedServerInstance != null && 
            IsQ3PathValid;

        private void ValidateQ3Path()
        {
            if (string.IsNullOrWhiteSpace(Q3Path))
            {
                IsQ3PathValid = false;
                Q3ValidationMessage = "No Q3 installation path selected.";
                return;
            }

            IsQ3PathValid = Q3ValidationService.IsValidQ3Folder(Q3Path);
            Q3ValidationMessage = IsQ3PathValid ? "Valid Q3 installation found." : Q3ValidationService.GetValidationErrorMessage(Q3Path);
        }

        private async void StartServer()
        {
            if (SelectedVpsConnection == null || SelectedServerInstance == null) return;

            StatusMessage = "Starting server...";
            LogToTerminal($"Starting server '{SelectedServerInstance.Name}'...");
            
            var success = await _sshService.StartServerAsync(SelectedVpsConnection, SelectedServerInstance.Name);
            
            if (success)
            {
                StatusMessage = "Server started successfully!";
                LogToTerminal("Server started successfully!");
                SelectedServerInstance.IsRunning = true;
            }
            else
            {
                StatusMessage = "Failed to start server!";
                LogToTerminal("Failed to start server!");
                System.Windows.MessageBox.Show("Failed to start server. Please check the connection and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopServer()
        {
            if (SelectedVpsConnection == null || SelectedServerInstance == null) return;

            StatusMessage = "Stopping server...";
            LogToTerminal($"Stopping server '{SelectedServerInstance.Name}'...");
            
            var success = await _sshService.StopServerAsync(SelectedVpsConnection, SelectedServerInstance.Name);
            
            if (success)
            {
                StatusMessage = "Server stopped successfully!";
                LogToTerminal("Server stopped successfully!");
                SelectedServerInstance.IsRunning = false;
            }
            else
            {
                StatusMessage = "Failed to stop server!";
                LogToTerminal("Failed to stop server!");
                System.Windows.MessageBox.Show("Failed to stop server. Please check the connection and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RestartServer()
        {
            if (SelectedVpsConnection == null || SelectedServerInstance == null) return;

            StatusMessage = "Restarting server...";
            LogToTerminal($"Restarting server '{SelectedServerInstance.Name}'...");
            
            var success = await _sshService.RestartServerAsync(SelectedVpsConnection, SelectedServerInstance.Name);
            
            if (success)
            {
                StatusMessage = "Server restarted successfully!";
                LogToTerminal("Server restarted successfully!");
            }
            else
            {
                StatusMessage = "Failed to restart server!";
                LogToTerminal("Failed to restart server!");
                System.Windows.MessageBox.Show("Failed to restart server. Please check the connection and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanControlServer() => SelectedVpsConnection != null && SelectedServerInstance != null && SelectedServerInstance.IsDeployed;



        private async void ImportExistingConfigurations()
        {
            if (SelectedVpsConnection == null) return;

            var result = System.Windows.MessageBox.Show(
                "This will import existing server configurations from the VPS server. Any current local configurations will be replaced. Continue?",
                "Import Existing Configurations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                await CheckAndImportExistingConfigurationsAsync();
            }
        }

        private bool CanImportExistingConfigurations() => SelectedVpsConnection != null;

        private async Task SyncDeploymentStateWithServerAsync()
        {
            if (SelectedVpsConnection == null || CurrentDeploymentState == null) return;

            try
            {
                LogToTerminal("Syncing deployment state with server...");
                
                // Get current instances from server
                var serverDeployedInstances = await _sshService.GetDeployedInstancesAsync(SelectedVpsConnection);
                
                // Update local deployment state to match server
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentDeploymentState.DeployedInstances.Clear();
                    CurrentDeploymentState.DeployedInstances.AddRange(serverDeployedInstances);
                    CurrentDeploymentState.LastDeploymentSync = DateTime.Now;
                });
                
                // Save updated deployment state
                await _dataService.SaveDeploymentStateAsync(SelectedVpsConnection.Name, CurrentDeploymentState);
                
                // Update UI on the main thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var instance in ServerInstances)
                    {
                        instance.IsDeployed = CurrentDeploymentState.DeployedInstances.Contains(instance.Name);
                    }
                    
                    // Force UI refresh
                    OnPropertyChanged(nameof(ServerInstances));
                    CommandManager.InvalidateRequerySuggested();
                });
                
                LogToTerminal($"Synced deployment state: {serverDeployedInstances.Count} instances found on server");
            }
            catch (Exception ex)
            {
                LogToTerminal($"Error syncing deployment state: {ex.Message}");
                LogToTerminal("Continuing with local deployment state only.");
                // Don't throw - this is a background operation that shouldn't break the UI
            }
        }

        private async void OnSelectedServerInstancePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // When IsDeployed property changes, update command states
            if (e.PropertyName == nameof(ServerInstance.IsDeployed))
            {
                CommandManager.InvalidateRequerySuggested();
            }
            
            // Save the server instance when any property changes
            if (SelectedServerInstance != null && SelectedVpsConnection != null)
            {
                await _dataService.SaveServerInstanceAsync(SelectedVpsConnection.Name, SelectedServerInstance);
            }
        }
        
        private async void SaveSelectedConnectionToSettings()
        {
            try
            {
                var settings = await _dataService.LoadSettingsAsync();
                settings.LastSelectedConnection = SelectedVpsConnection?.Name ?? string.Empty;
                await _dataService.SaveSettingsAsync(settings);
            }
            catch (Exception ex)
            {
                LogToTerminal($"Error saving selected connection to settings: {ex.Message}");
            }
        }
        
        private async Task SaveQ3PathToSettings()
        {
            try
            {
                var settings = await _dataService.LoadSettingsAsync();
                settings.LastQ3Path = Q3Path;
                await _dataService.SaveSettingsAsync(settings);
                LogToTerminal($"Q3 path saved to settings: {Q3Path}");
            }
            catch (Exception ex)
            {
                LogToTerminal($"Error saving Q3 path to settings: {ex.Message}");
            }
        }
        
        // Master Control Methods
        private async void SyncMaps()
        {
            if (VpsConnections.Count == 0)
            {
                System.Windows.MessageBox.Show("No VPS connections available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            StatusMessage = "Syncing maps to all VPS servers...";
            LogToTerminal("Starting map sync to all VPS servers...");
            
            var successCount = 0;
            var totalCount = VpsConnections.Count;
            
                            foreach (var connection in VpsConnections)
                {
                    try
                    {
                        LogToTerminal($"Syncing maps to {connection.Name} ({connection.Ip})...");
                        var success = await _sshService.SyncMapsAsync(connection, Q3Path);
                        if (success)
                        {
                            successCount++;
                            LogToTerminal($"Successfully synced maps to {connection.Name}");
                        }
                        else
                        {
                            LogToTerminal($"Failed to sync maps to {connection.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToTerminal($"Error syncing maps to {connection.Name}: {ex.Message}");
                    }
                }
            
            StatusMessage = $"Map sync completed: {successCount}/{totalCount} servers updated";
            LogToTerminal($"Map sync completed: {successCount}/{totalCount} servers updated");
            
            if (successCount == totalCount)
            {
                System.Windows.MessageBox.Show($"Successfully synced maps to all {totalCount} servers!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show($"Map sync completed with errors. {successCount}/{totalCount} servers updated.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private bool CanSyncMaps() => VpsConnections.Count > 0;
        
        public async Task CycleAllServersAsync(string operation)
        {
            if (VpsConnections.Count == 0)
            {
                System.Windows.MessageBox.Show("No VPS connections available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            StatusMessage = $"Executing {operation} on all VPS servers...";
            LogToTerminal($"Starting {operation} on all VPS servers...");
            
            var successCount = 0;
            var totalCount = VpsConnections.Count;
            
            foreach (var connection in VpsConnections)
            {
                try
                {
                    LogToTerminal($"Executing {operation} on {connection.Name} ({connection.Ip})...");
                    
                    // Load server instances for this connection
                    var instances = await _dataService.LoadServerInstancesAsync(connection.Name);
                    var deployedInstances = instances.Where(i => i.IsDeployed).Select(i => i.Name).ToList();
                    
                    if (deployedInstances.Count == 0)
                    {
                        LogToTerminal($"No deployed server instances found for {connection.Name}");
                        continue;
                    }
                    
                    LogToTerminal($"Found {deployedInstances.Count} deployed instances: {string.Join(", ", deployedInstances)}");
                    bool success = false;
                    
                    switch (operation.ToLower())
                    {
                        case "cycle servers":
                            success = await _sshService.RestartAllServersAsync(connection, deployedInstances);
                            break;
                        case "stop servers":
                            success = await _sshService.StopAllServersAsync(connection, deployedInstances);
                            break;
                        case "start servers":
                            success = await _sshService.StartAllServersAsync(connection, deployedInstances);
                            break;
                    }
                    
                    if (success)
                    {
                        successCount++;
                        LogToTerminal($"Successfully executed {operation} on {connection.Name}");
                    }
                    else
                    {
                        LogToTerminal($"Failed to execute {operation} on {connection.Name}");
                    }
                }
                catch (Exception ex)
                {
                    LogToTerminal($"Error executing {operation} on {connection.Name}: {ex.Message}");
                }
            }
            
            StatusMessage = $"{operation} completed: {successCount}/{totalCount} servers updated";
            LogToTerminal($"{operation} completed: {successCount}/{totalCount} servers updated");
            
            if (successCount == totalCount)
            {
                System.Windows.MessageBox.Show($"Successfully executed {operation} on all {totalCount} servers!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show($"{operation} completed with errors. {successCount}/{totalCount} servers updated.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
    }
} 