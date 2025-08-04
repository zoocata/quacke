using System.Windows;
using System;

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