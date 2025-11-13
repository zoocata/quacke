using System.Windows;
using System.Windows.Controls;
using QuakeServerManager.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.ComponentModel;
using System;

namespace QuakeServerManager
{
    public partial class MainWindow : Window
    {
        private bool _userScrolledUp = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new ViewModels.MainViewModel(this);
            
            // Set up password binding
            PasswordBox.PasswordChanged += PasswordBox_PasswordChanged;
            
            // Set up terminal auto-scrolling
            SetupTerminalAutoScroll();
            SetupTerminalScrollTracking();
            
            // Set up window closing event
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel && viewModel.IsDeploying)
            {
                var result = System.Windows.MessageBox.Show(
                    "⚠️ DANGER: Deployment in Progress!\n\n" +
                    "You are currently deploying a server. Closing the application now will:\n\n" +
                    "• Abort the deployment process\n" +
                    "• Leave partial files on the VPS\n" +
                    "• Potentially corrupt the server installation\n" +
                    "• Require manual cleanup of /tmp files\n\n" +
                    "Are you sure you want to abandon the deployment?",
                    "Abandon Deployment?",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.No)
                {
                    e.Cancel = true; // Prevent window from closing
                }
                else
                {
                    // User confirmed - show additional warning
                    var finalResult = System.Windows.MessageBox.Show(
                        "🚨 FINAL WARNING!\n\n" +
                        "This will leave your VPS in an inconsistent state.\n" +
                        "You may need to manually clean up files and restart the deployment.\n\n" +
                        "Are you absolutely sure?",
                        "Final Confirmation",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Stop);

                    if (finalResult == System.Windows.MessageBoxResult.No)
                    {
                        e.Cancel = true; // Prevent window from closing
                    }
                }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Ensure terminal auto-scrolling is set up
            SetupTerminalAutoScroll();
        }

        private void SetupTerminalAutoScroll()
        {
            if (DataContext is MainViewModel viewModel)
            {
                // Remove any existing event handler to avoid duplicates
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                
                // Add the event handler
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.TerminalOutputText))
            {
                // Auto-scroll to the bottom of the terminal only if user hasn't scrolled up
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_userScrolledUp)
                    {
                        ScrollTerminalToBottom();
                    }
                });
            }
        }

        private void ScrollTerminalToBottom()
        {
            try
            {
                // Scroll the TextBox to the end
                TerminalOutput.ScrollToEnd();
                
                // Also ensure the ScrollViewer is at the bottom
                TerminalScrollViewer.ScrollToBottom();
            }
            catch (System.Exception ex)
            {
                // Silently handle any scrolling errors
                System.Diagnostics.Debug.WriteLine($"Error scrolling terminal: {ex.Message}");
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel viewModel &&
                viewModel.VpsManager.SelectedVpsConnection != null)
            {
                viewModel.VpsManager.SelectedVpsConnection.Password = PasswordBox.Password;
            }
        }

        public void UpdatePasswordBox(string password)
        {
            if (PasswordBox.Password != password)
            {
                PasswordBox.Password = password;
            }
        }

        private void RconPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel viewModel &&
                viewModel.ServerManager.SelectedServerInstance != null)
            {
                viewModel.ServerManager.SelectedServerInstance.RconPassword = RconPasswordBox.Password;
            }
        }

        public void UpdateRconPasswordBox(string rconPassword)
        {
            if (RconPasswordBox.Password != rconPassword)
            {
                RconPasswordBox.Password = rconPassword;
            }
        }

        private void ToggleTerminal_Click(object sender, RoutedEventArgs e)
        {
            // Terminal is now resizable, no longer collapsible
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow((ViewModels.MainViewModel)DataContext);
            settingsWindow.Owner = this;
            
            if (settingsWindow.ShowDialog() == true)
            {
                // Settings were saved (OK was clicked)
                // The Q3Path is already updated in the ViewModel
            }
        }
        
        private void CycleServersButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel viewModel)
            {
                _ = viewModel.CycleAllServersAsync("Cycle Servers");
            }
        }
        
                 private void CycleServersDropdown_Click(object sender, RoutedEventArgs e)
         {
             var contextMenu = new ContextMenu();
             
             var cycleItem = new MenuItem { Header = "Cycle Servers" };
             cycleItem.Click += (s, args) => 
             {
                 if (DataContext is ViewModels.MainViewModel viewModel)
                 {
                     _ = viewModel.CycleAllServersAsync("Cycle Servers");
                 }
             };
             
             var stopItem = new MenuItem { Header = "Stop Servers" };
             stopItem.Click += (s, args) => 
             {
                 if (DataContext is ViewModels.MainViewModel viewModel)
                 {
                     _ = viewModel.CycleAllServersAsync("Stop Servers");
                 }
             };
             
             var startItem = new MenuItem { Header = "Start Servers" };
             startItem.Click += (s, args) => 
             {
                 if (DataContext is ViewModels.MainViewModel viewModel)
                 {
                     _ = viewModel.CycleAllServersAsync("Start Servers");
                 }
             };
             
             contextMenu.Items.Add(cycleItem);
             contextMenu.Items.Add(new Separator());
             contextMenu.Items.Add(stopItem);
             contextMenu.Items.Add(startItem);
             
             // Position the context menu at the dropdown button
             if (sender is System.Windows.Controls.Button button)
             {
                 contextMenu.PlacementTarget = button;
                 contextMenu.Placement = PlacementMode.Bottom;
             }
             
             contextMenu.IsOpen = true;
         }

         private void VpsConnectionsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
         {
             if (e.OriginalSource is FrameworkElement element)
             {
                 // Find the ListBoxItem that was double-clicked
                 var listBoxItem = FindAncestor<ListBoxItem>(element);
                 if (listBoxItem != null)
                 {
                     // Get the data context (VpsConnection)
                     var vpsConnection = listBoxItem.DataContext as Models.VpsConnection;
                     if (vpsConnection != null)
                     {
                         // Find the Grid container
                         var grid = FindVisualChild<Grid>(listBoxItem);
                         if (grid != null)
                         {
                             // Find the TextBox and StackPanel in the template
                             var displayPanel = FindVisualChild<StackPanel>(grid, "DisplayPanel");
                             var editTextBox = FindVisualChild<System.Windows.Controls.TextBox>(grid, "EditTextBox");
                             
                             if (displayPanel != null && editTextBox != null)
                             {
                                 // Switch to edit mode
                                 displayPanel.Visibility = Visibility.Collapsed;
                                 editTextBox.Visibility = Visibility.Visible;
                                 editTextBox.Focus();
                                 editTextBox.SelectAll();
                             }
                         }
                     }
                 }
             }
         }

         private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
         {
             var textBox = sender as System.Windows.Controls.TextBox;
             if (textBox != null)
             {
                 EndEdit(textBox);
             }
         }

         private void EditTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
         {
             if (e.Key == System.Windows.Input.Key.Enter)
             {
                 var textBox = sender as System.Windows.Controls.TextBox;
                 if (textBox != null)
                 {
                     EndEdit(textBox);
                 }
                 e.Handled = true;
             }
             else if (e.Key == System.Windows.Input.Key.Escape)
             {
                 var textBox = sender as System.Windows.Controls.TextBox;
                 if (textBox != null)
                 {
                     CancelEdit(textBox);
                 }
                 e.Handled = true;
             }
         }

         private void EndEdit(System.Windows.Controls.TextBox textBox)
         {
             if (textBox != null)
             {
                 var listBoxItem = FindAncestor<ListBoxItem>(textBox);
                 if (listBoxItem != null)
                 {
                     var grid = FindVisualChild<Grid>(listBoxItem);
                     if (grid != null)
                     {
                         var displayPanel = FindVisualChild<StackPanel>(grid, "DisplayPanel");
                         
                         if (displayPanel != null)
                         {
                             // Switch back to display mode
                             textBox.Visibility = Visibility.Collapsed;
                             displayPanel.Visibility = Visibility.Visible;
                         }
                     }
                 }
             }
         }

         private void CancelEdit(System.Windows.Controls.TextBox textBox)
         {
             if (textBox != null)
             {
                 var listBoxItem = FindAncestor<ListBoxItem>(textBox);
                 if (listBoxItem != null)
                 {
                     var vpsConnection = listBoxItem.DataContext as Models.VpsConnection;
                     var grid = FindVisualChild<Grid>(listBoxItem);
                     
                     if (vpsConnection != null && grid != null)
                     {
                         var displayPanel = FindVisualChild<StackPanel>(grid, "DisplayPanel");
                         
                         if (displayPanel != null)
                         {
                             // Reset the text to the original value
                             textBox.Text = vpsConnection.Name;
                             
                             // Switch back to display mode
                             textBox.Visibility = Visibility.Collapsed;
                             displayPanel.Visibility = Visibility.Visible;
                         }
                     }
                 }
             }
         }

         private void SetupTerminalScrollTracking()
        {
            // Track when user manually scrolls
            TerminalScrollViewer.ScrollChanged += (sender, e) =>
            {
                // Check if user scrolled up (not at bottom)
                var scrollViewer = (ScrollViewer)sender;
                var isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1;
                _userScrolledUp = !isAtBottom;
            };
        }

        private void ScrollToBottomAndResumeAutoScroll()
        {
            ScrollTerminalToBottom();
            _userScrolledUp = false;
        }

        private void TerminalHeader_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ScrollToBottomAndResumeAutoScroll();
        }

        private void CancelDeployment_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                var result = System.Windows.MessageBox.Show(
                    "⚠️ Cancel Deployment?\n\n" +
                    "This will stop the current deployment process.\n" +
                    "Partial files may remain on the VPS.\n\n" +
                    "Are you sure you want to cancel?",
                    "Cancel Deployment",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // TODO: Implement cancellation logic in ViewModel
                    // For now, just log the cancellation
                    viewModel.LogToTerminal("Deployment cancelled by user.");
                }
            }
        }

         private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
         {
             while (current != null)
             {
                 if (current is T result)
                     return result;
                 current = VisualTreeHelper.GetParent(current);
             }
             return null;
         }
        
        private static T? FindVisualChild<T>(DependencyObject parent, string? name = null) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T result)
                {
                    if (name == null || (child is FrameworkElement fe && fe.Name == name))
                    {
                        return result;
                    }
                }
                
                var descendant = FindVisualChild<T>(child, name);
                if (descendant != null)
                {
                    return descendant;
                }
            }
            return null;
        }
        


    }
}
