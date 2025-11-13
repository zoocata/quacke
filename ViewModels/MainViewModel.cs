using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using QuakeServerManager.Models;
using QuakeServerManager.Services;
using QuakeServerManager.Services.Ssh;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace QuakeServerManager.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly DataService _dataService;
        private readonly SshService _sshService;
        public VpsManagerViewModel VpsManager { get; }

        public ServerManagerViewModel ServerManager { get; }

        private string _q3Path = string.Empty;
        private string _statusMessage = "Ready";
        public ObservableCollection<LogEntry> TerminalOutput { get; } = new();
        private string _terminalOutputText = string.Empty;
        public string TerminalOutputText
        {
            get => _terminalOutputText;
            private set => SetProperty(ref _terminalOutputText, value);
        }
        private bool _isPasswordAuth = true;
        private bool _isQ3PathValid = false;
        private string _q3ValidationMessage = string.Empty;
        private bool _retainInitialValues = false;
        private DeploymentState? _currentDeploymentState;
        private bool _isDeploying = false;
        private double _deploymentProgress = 0.0;
        private string _deploymentStatus = string.Empty;

        private readonly IDialogService _dialogService;

        public MainViewModel(Window? owner = null)
        {
            _dataService = new DataService();
            _sshService = new SshService();
            _dialogService = new DialogService();
            VpsManager = new VpsManagerViewModel(_dataService, _dialogService, owner);
            ServerManager = new ServerManagerViewModel(_dataService, _dialogService, owner, this);
            
            // Propagate property changes from nested view models
            VpsManager.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VpsManager.SelectedVpsConnection))
                {
                    OnPropertyChanged(nameof(SelectedVpsConnection));
                    // Load server instances when VPS connection changes
                    LoadServerInstancesAsync();
                }
            };
            ServerManager.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ServerManager.SelectedServerInstance))
                    OnPropertyChanged(nameof(SelectedServerInstance));
            };
            
            // Connect SSH service logging to terminal output
            _sshService.LogMessage += (message, level) => LogToTerminal(message, level);
            _sshService.HostKeyReceived += OnHostKeyReceived;

            
            AvailableClasses = new ObservableCollection<Class>();

            InitializeCommands();
            InitializeClasses();
            LoadDataAsync();
        }



        public ObservableCollection<Class> AvailableClasses { get; }

        // Pass-through properties for nested view models to simplify XAML bindings
        public System.Collections.ObjectModel.ObservableCollection<QuakeServerManager.Models.VpsConnection> VpsConnections => VpsManager.VpsConnections;

        public QuakeServerManager.Models.VpsConnection? SelectedVpsConnection
        {
            get => VpsManager.SelectedVpsConnection;
            set
            {
                if (VpsManager.SelectedVpsConnection != value)
                {
                    VpsManager.SelectedVpsConnection = value;
                    OnPropertyChanged();
                }
            }
        }

        public System.Collections.ObjectModel.ObservableCollection<QuakeServerManager.Models.ServerInstance> ServerInstances => ServerManager.ServerInstances;

        public QuakeServerManager.Models.ServerInstance? SelectedServerInstance
        {
            get => ServerManager.SelectedServerInstance;
            set
            {
                if (ServerManager.SelectedServerInstance != value)
                {
                    ServerManager.SelectedServerInstance = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanRetainInitialValues));
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

        public bool CanRetainInitialValues => ServerManager.SelectedServerInstance != null && 
                                            ServerManager.ServerInstances.Count > 0;

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
        public ICommand ImportExistingConfigurationsCommand { get; private set; } = null!;
        public ICommand UploadCustomMapsCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            AddVpsCommand = new RelayCommand(VpsManager.AddVps);
            DeleteVpsCommand = new RelayCommand(VpsManager.DeleteVps, () => VpsManager.SelectedVpsConnection != null);
            TestConnectionCommand = new RelayCommand(TestConnection, CanTestConnection);
            BrowseKeyCommand = new RelayCommand(BrowseKey);
            AddInstanceCommand = new RelayCommand(() => ServerManager.AddInstance(VpsManager.SelectedVpsConnection.Name), () => VpsManager.SelectedVpsConnection != null);
            DeleteInstanceCommand = new RelayCommand(() => ServerManager.DeleteInstance(VpsManager.SelectedVpsConnection.Name), () => ServerManager.SelectedServerInstance != null && VpsManager.SelectedVpsConnection != null);
            BrowseQ3PathCommand = new RelayCommand(BrowseQ3Path);
            DeployServerCommand = new RelayCommand(DeployServer, CanDeployServer);
            StartServerCommand = new RelayCommand(StartServer, CanControlServer);
            StopServerCommand = new RelayCommand(StopServer, CanControlServer);
            RestartServerCommand = new RelayCommand(RestartServer, CanControlServer);
            ImportExistingConfigurationsCommand = new RelayCommand(ImportExistingConfigurations, CanImportExistingConfigurations);
            UploadCustomMapsCommand = new RelayCommand(UploadCustomMaps, () => ServerManager.SelectedServerInstance != null);
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

        public void LogToTerminal(string message, LogLevel level = LogLevel.Info)
        {
            // Ensure we're on the UI thread when updating the terminal
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var entry = new LogEntry(message, level);
                TerminalOutput.Add(entry);
                TerminalOutputText += $"[{entry.Timestamp:HH:mm:ss}] {entry.Message}{System.Environment.NewLine}";
                OnPropertyChanged(nameof(TerminalOutputText));
            });
        }

        private async void LoadDataAsync()
        {
            try
            {
                VpsManager.LoadVpsConnections();

                var settings = await _dataService.LoadSettingsAsync();
                Q3Path = settings.LastQ3Path;
                
                // Validate Q3 path on startup
                ValidateQ3Path();
                
                if (!IsQ3PathValid && !string.IsNullOrWhiteSpace(Q3Path))
                {
                    LogToTerminal("Warning: Invalid Q3 installation path detected. Please set a valid path in Settings.", LogLevel.Warning);
                }
                
                // Restore last selected connection and load its server instances
                if (!string.IsNullOrWhiteSpace(settings.LastSelectedConnection))
                {
                    var lastConnection = VpsManager.VpsConnections.FirstOrDefault(c => c.Name == settings.LastSelectedConnection);
                    if (lastConnection != null)
                    {
                        VpsManager.SelectedVpsConnection = lastConnection;
                        LogToTerminal($"Restored last selected connection: {lastConnection.Name}");
                    }
                }
                
                LogToTerminal("Application started successfully.", LogLevel.Success);
            }
            catch (Exception ex)
            {
                LogToTerminal($"Error loading data: {ex.Message}", LogLevel.Error);
                System.Windows.MessageBox.Show($"Error loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadServerInstancesAsync()
        {
            if (VpsManager.SelectedVpsConnection == null) return;
            ServerManager.LoadServerInstances(VpsManager.SelectedVpsConnection.Name);
        }



        private async void TestConnection()
        {
            if (VpsManager.SelectedVpsConnection == null) return;

            StatusMessage = "Testing connection...";
            LogToTerminal($"Testing SSH connection to {VpsManager.SelectedVpsConnection.Ip}:{VpsManager.SelectedVpsConnection.Port}...");
            
            var success = await _sshService.TestConnectionAsync(VpsManager.SelectedVpsConnection);
            
            if (success)
            {
                // If connection test succeeds, automatically save the connection
                try
                {
                    await _dataService.SaveVpsConnectionAsync(VpsManager.SelectedVpsConnection);
                    StatusMessage = "Connection successful and saved!";
                    LogToTerminal("SSH connection test successful and connection saved!", LogLevel.Success);
                    
                    // Check for existing CPMA installation and import configurations
                    await CheckAndImportExistingConfigurationsAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = "Connection successful but failed to save!";
                    LogToTerminal($"SSH connection test successful but failed to save: {ex.Message}", LogLevel.Warning);
                    System.Windows.MessageBox.Show($"SSH connection test successful but failed to save: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                StatusMessage = "Connection failed!";
                LogToTerminal("SSH connection test failed!", LogLevel.Error);
                System.Windows.MessageBox.Show("SSH connection test failed. Please check your settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CheckAndImportExistingConfigurationsAsync()
        {
            if (VpsManager.SelectedVpsConnection == null) return;

            try
            {
                LogToTerminal("Checking for existing CPMA installation...");
                StatusMessage = "Checking for existing CPMA installation...";
                
                var hasExistingCpma = await _sshService.CheckForExistingCpmaInstallationAsync(VpsManager.SelectedVpsConnection);
                
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
                        
                                                    var importedInstances = await _sshService.ImportExistingServerConfigurationsAsync(VpsManager.SelectedVpsConnection);
                        
                        if (importedInstances.Count > 0)
                        {
                            // Clear existing instances and add imported ones
                            ServerManager.ServerInstances.Clear();
                            
                            foreach (var instance in importedInstances)
                            {
                                ServerManager.ServerInstances.Add(instance);
                                // Save each imported instance
                                await _dataService.SaveServerInstanceAsync(VpsManager.SelectedVpsConnection.Name, instance);
                            }
                            
                            // Set focus on the first imported instance
                            if (importedInstances.Count > 0)
                            {
                                ServerManager.SelectedServerInstance = importedInstances[0];
                            }
                            
                            // Update deployment state
                            var deploymentState = new DeploymentState
                            {
                                VpsConnectionName = VpsManager.SelectedVpsConnection.Name,
                                DeployedInstances = importedInstances.Select(i => i.Name).ToList(),
                                LastDeploymentSync = DateTime.Now
                            };
                            await _dataService.SaveDeploymentStateAsync(VpsManager.SelectedVpsConnection.Name, deploymentState);
                            CurrentDeploymentState = deploymentState;
                            
                            LogToTerminal($"Successfully imported {importedInstances.Count} server configurations!", LogLevel.Success);
                            
                            // Also import class configurations
                            try
                            {
                                LogToTerminal("Importing class configurations...");
                                var importedClasses = await _sshService.ImportClassConfigurationsAsync(VpsManager.SelectedVpsConnection);
                                
                                if (importedClasses.Count > 0)
                                {
                                    AvailableClasses.Clear();
                                    foreach (var cls in importedClasses)
                                    {
                                        AvailableClasses.Add(cls);
                                    }
                                    LogToTerminal($"Successfully imported {importedClasses.Count} class configurations!", LogLevel.Success);
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
                                    LogToTerminal("No class configurations found to import.", LogLevel.Warning);
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
                                LogToTerminal($"Warning: Server configurations imported successfully, but class import failed: {classEx.Message}", LogLevel.Warning);
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
                            LogToTerminal("No server configurations found to import.", LogLevel.Warning);
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
                LogToTerminal($"Error during configuration import check: {ex.Message}", LogLevel.Error);
                StatusMessage = "Connection successful but import check failed.";
                
                System.Windows.MessageBox.Show(
                    $"Connection was successful, but there was an error checking for existing configurations: {ex.Message}",
                    "Import Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private bool CanTestConnection() => VpsManager.SelectedVpsConnection != null && 
            !string.IsNullOrWhiteSpace(VpsManager.SelectedVpsConnection.Ip);

        private void BrowseKey()
        {
            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select Private Key File",
                Filter = "Private Key Files (*.pem;*.key)|*.pem;*.key|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (VpsManager.SelectedVpsConnection != null)
                {
                    VpsManager.SelectedVpsConnection.PrivateKeyPath = dialog.FileName;
                    LogToTerminal($"Selected private key: {dialog.FileName}");
                }
            }
        }



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
            if (VpsManager.SelectedVpsConnection == null || ServerManager.ServerInstances.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select a VPS connection with server instances.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check if all required paths are configured
            var settings = await _dataService.LoadSettingsAsync();
            var missingPaths = new List<string>();

            if (string.IsNullOrEmpty(settings.CpmaPath) || !Directory.Exists(settings.CpmaPath))
                missingPaths.Add("CPMA folder");
            if (string.IsNullOrEmpty(settings.Pak0Path) || !File.Exists(settings.Pak0Path))
                missingPaths.Add("pak0.pk3");
            if (string.IsNullOrEmpty(settings.ServerExecutablePath) || !File.Exists(settings.ServerExecutablePath))
                missingPaths.Add("Server executable");

            if (missingPaths.Any())
            {
                System.Windows.MessageBox.Show(
                    $"The following required files/folders are not configured:\n\n" +
                    string.Join("\n", missingPaths.Select(p => $"• {p}")) +
                    "\n\nPlease configure these paths in Settings before deploying.",
                    "Missing Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Initialize progress tracking
            IsDeploying = true;
            DeploymentProgress = 0.0;
            DeploymentStatus = "Initializing Docker deployment...";
            StatusMessage = "Deploying servers via Docker...";
            LogToTerminal($"Starting Docker deployment of all server instances to {VpsManager.SelectedVpsConnection.Ip}...");
            
            try
            {
                // Run deployment on background thread to prevent UI freezing
                var success = await Task.Run(async () =>
                {
                    try
                    {
                        // Clear deployment state
                        if (CurrentDeploymentState != null)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                CurrentDeploymentState.DeployedInstances.Clear();
                            });
                        }

                        // Collect custom maps from all instances
                        var allCustomMaps = ServerManager.ServerInstances
                            .SelectMany(i => i.CustomMaps)
                            .Distinct()
                            .ToList();

                        // Use Docker deployment
                        LogToTerminal("Using Docker-based deployment...");
                        var dockerSuccess = await _sshService.DeployDockerServerAsync(
                            VpsManager.SelectedVpsConnection,
                            ServerManager.ServerInstances.ToList(),
                            settings.Pak0Path,
                            settings.CpmaPath,
                            settings.ServerExecutablePath,
                            settings.MapsPath,
                            allCustomMaps.Any() ? allCustomMaps : null,
                            (progress, status) =>
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    DeploymentProgress = progress;
                                    DeploymentStatus = status;
                                });
                            });

                        if (dockerSuccess)
                        {
                            LogToTerminal("Docker deployment completed successfully!", LogLevel.Success);

                            // Mark all instances as deployed
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                foreach (var instance in ServerManager.ServerInstances)
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
                                }

                                // Update deployment timestamp
                                if (CurrentDeploymentState != null)
                                {
                                    CurrentDeploymentState.LastDeploymentSync = DateTime.Now;
                                }

                                // Force UI refresh
                                OnPropertyChanged(nameof(ServerManager.ServerInstances));
                            });

                            return true;
                        }
                        else
                        {
                            LogToTerminal("Docker deployment failed.", LogLevel.Error);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToTerminal($"Deployment error: {ex.Message}", LogLevel.Error);
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
                    
                    LogToTerminal("All server deployments completed successfully!", LogLevel.Success);
                    
                    // Force UI refresh
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerManager.ServerInstances));
                        CommandManager.InvalidateRequerySuggested();
                    });
                    
                    // Add a small delay and then force refresh again to ensure UI updates
                    await Task.Delay(100);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerManager.ServerInstances));
                    });
                    
                    // Save deployment state
                    await _dataService.SaveDeploymentStateAsync(VpsManager.SelectedVpsConnection.Name, CurrentDeploymentState);
                    
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
                    
                    LogToTerminal("Server deployment failed!", LogLevel.Error);
                    
                    // Force UI refresh
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerManager.ServerInstances));
                        CommandManager.InvalidateRequerySuggested();
                    });
                    
                    // Add a small delay and then force refresh again to ensure UI updates
                    await Task.Delay(100);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(ServerManager.ServerInstances));
                    });
                    
                    // Save deployment state even if failed
                    await _dataService.SaveDeploymentStateAsync(VpsManager.SelectedVpsConnection.Name, CurrentDeploymentState);
                    
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

        private bool CanDeployServer() => VpsManager.SelectedVpsConnection != null && 
            ServerManager.SelectedServerInstance != null && 
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
            if (VpsManager.SelectedVpsConnection == null || ServerManager.SelectedServerInstance == null) return;

            StatusMessage = "Starting server...";
            LogToTerminal($"Starting server '{ServerManager.SelectedServerInstance.Name}'...");
            
            var success = await _sshService.StartServerAsync(VpsManager.SelectedVpsConnection, ServerManager.SelectedServerInstance.Name);
            
            if (success)
            {
                StatusMessage = "Server started successfully!";
                LogToTerminal("Server started successfully!", LogLevel.Success);
                ServerManager.SelectedServerInstance.IsRunning = true;
            }
            else
            {
                StatusMessage = "Failed to start server!";
                LogToTerminal("Failed to start server!", LogLevel.Error);
                System.Windows.MessageBox.Show("Failed to start server. Please check the connection and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopServer()
        {
            if (VpsManager.SelectedVpsConnection == null || ServerManager.SelectedServerInstance == null) return;

            StatusMessage = "Stopping server...";
            LogToTerminal($"Stopping server '{ServerManager.SelectedServerInstance.Name}'...");
            
            var success = await _sshService.StopServerAsync(VpsManager.SelectedVpsConnection, ServerManager.SelectedServerInstance.Name);
            
            if (success)
            {
                StatusMessage = "Server stopped successfully!";
                LogToTerminal("Server stopped successfully!", LogLevel.Success);
                ServerManager.SelectedServerInstance.IsRunning = false;
            }
            else
            {
                StatusMessage = "Failed to stop server!";
                LogToTerminal("Failed to stop server!", LogLevel.Error);
                System.Windows.MessageBox.Show("Failed to stop server. Please check the connection and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RestartServer()
        {
            if (VpsManager.SelectedVpsConnection == null || ServerManager.SelectedServerInstance == null) return;

            StatusMessage = "Restarting server...";
            LogToTerminal($"Restarting server '{ServerManager.SelectedServerInstance.Name}'...");
            
            var success = await _sshService.RestartServerAsync(VpsManager.SelectedVpsConnection, ServerManager.SelectedServerInstance.Name);
            
            if (success)
            {
                StatusMessage = "Server restarted successfully!";
                LogToTerminal("Server restarted successfully!", LogLevel.Success);
            }
            else
            {
                StatusMessage = "Failed to restart server!";
                LogToTerminal("Failed to restart server!", LogLevel.Error);
                System.Windows.MessageBox.Show("Failed to restart server. Please check the connection and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanControlServer() => VpsManager.SelectedVpsConnection != null && ServerManager.SelectedServerInstance != null && ServerManager.SelectedServerInstance.IsDeployed;



        private async void ImportExistingConfigurations()
        {
            if (VpsManager.SelectedVpsConnection == null) return;

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

        private bool CanImportExistingConfigurations() => VpsManager.SelectedVpsConnection != null;

        private void UploadCustomMaps()
        {
            if (ServerManager.SelectedServerInstance == null)
                return;

            var dialog = new QuakeServerManager.Views.MapUploadDialog
            {
                Owner = App.Current.MainWindow
            };

            if (dialog.ShowDialog() == true && dialog.DialogResult)
            {
                var mapPaths = dialog.SelectedMaps.Select(m => m.FilePath).ToList();

                if (dialog.ApplyToAllInstances)
                {
                    // Add maps to all instances
                    foreach (var instance in ServerManager.ServerInstances)
                    {
                        foreach (var mapPath in mapPaths)
                        {
                            if (!instance.CustomMaps.Contains(mapPath))
                            {
                                instance.CustomMaps.Add(mapPath);
                            }
                        }
                    }

                    LogToTerminal($"Added {mapPaths.Count} custom map(s) to all {ServerManager.ServerInstances.Count} instances.", LogLevel.Success);
                    System.Windows.MessageBox.Show(
                        $"Added {mapPaths.Count} custom map(s) to all instances.\n\nRedeploy servers to apply changes.",
                        "Maps Added",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    // Add maps to selected instance only
                    foreach (var mapPath in mapPaths)
                    {
                        if (!ServerManager.SelectedServerInstance.CustomMaps.Contains(mapPath))
                        {
                            ServerManager.SelectedServerInstance.CustomMaps.Add(mapPath);
                        }
                    }

                    LogToTerminal($"Added {mapPaths.Count} custom map(s) to instance '{ServerManager.SelectedServerInstance.Name}'.", LogLevel.Success);
                    System.Windows.MessageBox.Show(
                        $"Added {mapPaths.Count} custom map(s) to '{ServerManager.SelectedServerInstance.Name}'.\n\nRedeploy servers to apply changes.",
                        "Maps Added",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                // Save instance configurations
                if (VpsManager.SelectedVpsConnection != null)
                {
                    foreach (var instance in ServerManager.ServerInstances)
                    {
                        _dataService.SaveServerInstanceAsync(VpsManager.SelectedVpsConnection.Name, instance).Wait();
                    }
                }
            }
        }

        private async Task SyncDeploymentStateWithServerAsync()
        {
            if (VpsManager.SelectedVpsConnection == null || CurrentDeploymentState == null) return;

            try
            {
                LogToTerminal("Syncing deployment state with server...");
                
                // Get current instances from server
                var serverDeployedInstances = await _sshService.GetDeployedInstancesAsync(VpsManager.SelectedVpsConnection);
                
                // Update local deployment state to match server
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentDeploymentState.DeployedInstances.Clear();
                    CurrentDeploymentState.DeployedInstances.AddRange(serverDeployedInstances);
                    CurrentDeploymentState.LastDeploymentSync = DateTime.Now;
                });
                
                // Save updated deployment state
                await _dataService.SaveDeploymentStateAsync(VpsManager.SelectedVpsConnection.Name, CurrentDeploymentState);
                
                // Update UI on the main thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var instance in ServerManager.ServerInstances)
                    {
                        instance.IsDeployed = CurrentDeploymentState.DeployedInstances.Contains(instance.Name);
                    }
                    
                    // Force UI refresh
                    OnPropertyChanged(nameof(ServerManager.ServerInstances));
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


        
        
        private bool OnHostKeyReceived(string fingerprint)
        {
            var result = System.Windows.MessageBox.Show($"The server's host key fingerprint is:\n{fingerprint}\n\nDo you want to trust this host?", "Host Key Verification", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                VpsManager.SelectedVpsConnection.HostKeyFingerprint = fingerprint;
                return true;
            }
            return false;
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
        public async Task CycleAllServersAsync(string operation)
        {
            if (VpsManager.VpsConnections.Count == 0)
            {
                System.Windows.MessageBox.Show("No VPS connections available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            StatusMessage = $"Executing {operation} on all VPS servers...";
            LogToTerminal($"Starting {operation} on all VPS servers...");
            
            var successCount = 0;
            var totalCount = VpsManager.VpsConnections.Count;
            
            foreach (var connection in VpsManager.VpsConnections)
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
                        LogToTerminal($"Successfully executed {operation} on {connection.Name}", LogLevel.Success);
                    }
                    else
                    {
                        LogToTerminal($"Failed to execute {operation} on {connection.Name}", LogLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    LogToTerminal($"Error executing {operation} on {connection.Name}: {ex.Message}", LogLevel.Error);
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


    }


}
