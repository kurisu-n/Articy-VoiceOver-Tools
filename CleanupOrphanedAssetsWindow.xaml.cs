using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Kurisu.VoiceOverTools
{
    public partial class CleanupOrphanedAssetsWindow : Window
    {
        public enum ActionChoice
        {
            DryRun,
            DeleteArticyOnly,
            DeleteArticyAndDisk
        }

        public ActionChoice SelectedAction { get; private set; } = ActionChoice.DryRun;
        public bool Confirmed { get; private set; }

        private Action<int> _onNavigate;

        public CleanupOrphanedAssetsWindow()
        {
            InitializeComponent();

            // Articy's modal ShowDialog sometimes opens without keyboard/mouse focus; activate on
            // Loaded so the first click on an interactive element always registers.
            Loaded += (_, _) =>
            {
                Activate();
                MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            };
        }

        public void Populate(IReadOnlyList<string> rows, int orphanCount, long totalBytes, Action<int> onNavigate)
        {
            _onNavigate = onNavigate;

            if (orphanCount == 0)
            {
                SummaryTextBlock.Text = "No orphaned audio assets found. All audio assets are referenced by at least one DialogueFragment.";
                ExecuteButton.IsEnabled = false;
            }
            else
            {
                SummaryTextBlock.Text =
                    $"Found {orphanCount} audio asset(s) not referenced by any DialogueFragment.  " +
                    $"Total on-disk size: {FormatSize(totalBytes)}.";
                ExecuteButton.IsEnabled = true;
            }

            OrphansListBox.ItemsSource = rows;
        }

        private void OrphansListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowAssetButton.IsEnabled = OrphansListBox.SelectedIndex >= 0 && _onNavigate != null;
        }

        private void ShowAssetButton_Click(object sender, RoutedEventArgs e)
        {
            var idx = OrphansListBox.SelectedIndex;
            if (idx < 0 || _onNavigate == null) return;
            _onNavigate(idx);
            // Intentionally stay open so the user can navigate multiple entries without re-scanning.
        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeleteBothRadio.IsChecked == true)
                SelectedAction = ActionChoice.DeleteArticyAndDisk;
            else if (DeleteArticyRadio.IsChecked == true)
                SelectedAction = ActionChoice.DeleteArticyOnly;
            else
                SelectedAction = ActionChoice.DryRun;

            Confirmed = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int u = 0;
            while (size >= 1024 && u < units.Length - 1)
            {
                size /= 1024;
                u++;
            }
            return $"{size:0.##} {units[u]}";
        }
    }
}
