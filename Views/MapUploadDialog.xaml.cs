using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace QuakeServerManager.Views
{
    public partial class MapUploadDialog : Window
    {
        public ObservableCollection<MapFileInfo> SelectedMaps { get; private set; }
        public bool ApplyToAllInstances => AllInstancesRadio.IsChecked ?? true;
        public bool DialogResult { get; private set; }

        public MapUploadDialog()
        {
            InitializeComponent();
            SelectedMaps = new ObservableCollection<MapFileInfo>();
            MapListBox.ItemsSource = SelectedMaps;
        }

        private void AddMaps_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Map Files",
                Filter = "Quake 3 Maps (*.bsp)|*.bsp|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var filePath in openFileDialog.FileNames)
                {
                    // Check if map is already in the list
                    var fileName = Path.GetFileName(filePath);
                    if (SelectedMaps.Any(m => m.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        System.Windows.MessageBox.Show(
                            $"Map '{fileName}' is already in the list.",
                            "Duplicate Map",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        continue;
                    }

                    // Add map to list
                    var fileInfo = new FileInfo(filePath);
                    SelectedMaps.Add(new MapFileInfo
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        SizeBytes = fileInfo.Length,
                        SizeFormatted = FormatFileSize(fileInfo.Length)
                    });
                }
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = MapListBox.SelectedItems.Cast<MapFileInfo>().ToList();

            if (selectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "Please select maps to remove.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            foreach (var item in selectedItems)
            {
                SelectedMaps.Remove(item);
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMaps.Count == 0)
                return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to remove all {SelectedMaps.Count} map(s)?",
                "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SelectedMaps.Clear();
            }
        }

        private void Upload_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMaps.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "Please add at least one map file.",
                    "No Maps Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class MapFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; } = string.Empty;
    }
}
