using System.Windows;
using System;
using QuakeServerManager.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows.Forms;

namespace QuakeServerManager
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                // Initialize logging and other services here
                InitializeServices();

                // Check for first run and pak0.pk3 selection
                CheckFirstRun();

                // Create and show the main window
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Application startup failed: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void InitializeServices()
        {
            // Initialize any application-wide services
            // This could include logging, configuration, etc.
        }

        private async void CheckFirstRun()
        {
            var dataService = new DataService();
            var settings = await dataService.LoadSettingsAsync();

            // Check if this is first run or required files are missing
            if (!settings.FirstRunCompleted ||
                string.IsNullOrEmpty(settings.CpmaPath) || !Directory.Exists(settings.CpmaPath) ||
                string.IsNullOrEmpty(settings.Pak0Path) || !File.Exists(settings.Pak0Path) ||
                string.IsNullOrEmpty(settings.ServerExecutablePath) || !File.Exists(settings.ServerExecutablePath))
            {
                var result = System.Windows.MessageBox.Show(
                    "Welcome to Quacke Manager!\n\n" +
                    "To deploy servers, you'll need to provide:\n\n" +
                    "1. CPMA installation folder\n" +
                    "2. pak0.pk3 (legally-obtained)\n" +
                    "3. Server executable (cnq3-server-x64)\n\n" +
                    "Would you like to configure these now?",
                    "Initial Setup",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.No)
                {
                    System.Windows.MessageBox.Show(
                        "You can configure these paths later in Settings.\n\n" +
                        "Note: Deployment will not work until all required files are configured.",
                        "Setup Skipped",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Step 1: Select CPMA folder
                if (string.IsNullOrEmpty(settings.CpmaPath) || !Directory.Exists(settings.CpmaPath))
                {
                    System.Windows.MessageBox.Show(
                        "Step 1 of 3: Select your CPMA installation folder.\n\n" +
                        "This folder contains your CPMA mod files.",
                        "Select CPMA Folder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    using (var folderDialog = new FolderBrowserDialog())
                    {
                        folderDialog.Description = "Select CPMA installation folder";
                        folderDialog.ShowNewFolderButton = false;

                        if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            settings.CpmaPath = folderDialog.SelectedPath;
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                "Setup cancelled. You can configure paths later in Settings.",
                                "Setup Cancelled",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return;
                        }
                    }
                }

                // Step 2: Select pak0.pk3
                if (string.IsNullOrEmpty(settings.Pak0Path) || !File.Exists(settings.Pak0Path))
                {
                    System.Windows.MessageBox.Show(
                        "Step 2 of 3: Select your pak0.pk3 file.\n\n" +
                        "This file must be legally obtained from your Quake 3 Arena installation.",
                        "Select pak0.pk3",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    var openFileDialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select pak0.pk3",
                        Filter = "pak0.pk3 file|pak0.pk3|PK3 files (*.pk3)|*.pk3|All files (*.*)|*.*",
                        FileName = "pak0.pk3"
                    };

                    if (openFileDialog.ShowDialog() == true)
                    {
                        var selectedFile = openFileDialog.FileName;
                        if (Path.GetFileName(selectedFile).Equals("pak0.pk3", StringComparison.OrdinalIgnoreCase))
                        {
                            var fileInfo = new FileInfo(selectedFile);
                            if (fileInfo.Length > 1_000_000) // At least 1 MB
                            {
                                settings.Pak0Path = selectedFile;
                            }
                            else
                            {
                                System.Windows.MessageBox.Show(
                                    "The selected file appears to be too small to be a valid pak0.pk3.\n\n" +
                                    "Setup cancelled. Please try again from Settings.",
                                    "Invalid File",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                                return;
                            }
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                "Please select a file named 'pak0.pk3'.\n\n" +
                                "Setup cancelled. Please try again from Settings.",
                                "Invalid File",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            "Setup cancelled. You can configure paths later in Settings.",
                            "Setup Cancelled",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }
                }

                // Step 3: Select server executable
                if (string.IsNullOrEmpty(settings.ServerExecutablePath) || !File.Exists(settings.ServerExecutablePath))
                {
                    System.Windows.MessageBox.Show(
                        "Step 3 of 3: Select your Linux server executable.\n\n" +
                        "This must be a Linux binary (NOT a Windows .exe file).\n" +
                        "Typically named 'cnq3-server-x64' without any extension.\n\n" +
                        "Docker containers run on Linux and cannot execute Windows .exe files.",
                        "Select Linux Server Executable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    var openFileDialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select Linux Server Executable",
                        Filter = "Linux Server Executable|cnq3-server-x64;cnq3-server*|All files (*.*)|*.*"
                    };

                    if (openFileDialog.ShowDialog() == true)
                    {
                        var selectedFile = openFileDialog.FileName;

                        // Validate that it's not a Windows .exe file
                        if (Path.GetExtension(selectedFile).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Windows.MessageBox.Show(
                                "Windows .exe files cannot be used.\n\n" +
                                "Docker containers run on Linux. Please select a Linux server executable.\n" +
                                "This is typically named 'cnq3-server-x64' without any file extension.",
                                "Invalid Executable",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        settings.ServerExecutablePath = selectedFile;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            "Setup cancelled. You can configure paths later in Settings.",
                            "Setup Cancelled",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }
                }

                // Save settings
                settings.FirstRunCompleted = true;
                await dataService.SaveSettingsAsync(settings);

                System.Windows.MessageBox.Show(
                    "Initial setup complete!\n\n" +
                    "You can now create VPS connections and deploy servers.",
                    "Setup Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                base.OnExit(e);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Application exit error: {ex.Message}", 
                    "Exit Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 