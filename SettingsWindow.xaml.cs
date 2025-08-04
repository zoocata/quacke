using System.Windows;
using Microsoft.Win32;
using System.Windows.Forms;

namespace QuakeServerManager
{
    public partial class SettingsWindow : Window
    {
        private ViewModels.MainViewModel _mainViewModel;

        public SettingsWindow(ViewModels.MainViewModel mainViewModel)
        {
            InitializeComponent();
            _mainViewModel = mainViewModel;
            DataContext = mainViewModel;
        }

        private void BrowseQ3Path_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Quake III Installation Folder",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedPath = dialog.SelectedPath;
                
                // Validate the selected folder
                if (Services.Q3ValidationService.IsValidQ3Folder(selectedPath))
                {
                    _mainViewModel.Q3Path = selectedPath;
                    System.Windows.MessageBox.Show("Valid Quake III installation folder selected!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var errorMessage = Services.Q3ValidationService.GetValidationErrorMessage(selectedPath);
                    System.Windows.MessageBox.Show(errorMessage, "Invalid Q3 Installation", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 