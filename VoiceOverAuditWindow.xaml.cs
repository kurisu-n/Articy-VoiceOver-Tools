using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Kurisu.VoiceOverTools
{
    public partial class VoiceOverAuditWindow : Window
    {
        public sealed class Row
        {
            public string Icon { get; set; }
            public Brush IconBrush { get; set; }
            public string Header { get; set; }
            public string Detail { get; set; }
            public bool HasAsset { get; set; }
            public int CategoryKey { get; set; }
        }

        private Action<int> _onNavigateFragment;
        private Action<int> _onNavigateAsset;

        private IReadOnlyList<Row> _allRows = new List<Row>();
        private readonly HashSet<int> _visibleCategories = new() { 0, 1, 2 };
        private int _totalCount;

        public VoiceOverAuditWindow()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                Activate();
                MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            };
        }

        public void Populate(
            string scopeSummary,
            IReadOnlyList<Row> rows,
            int missing,
            int corrupted,
            int overlapping,
            Action<int> onNavigateFragment,
            Action<int> onNavigateAsset)
        {
            _onNavigateFragment = onNavigateFragment;
            _onNavigateAsset = onNavigateAsset;

            _allRows = rows;
            _totalCount = rows.Count;

            ScopeTextBlock.Text = scopeSummary;
            MissingCount.Text = missing.ToString();
            CorruptedCount.Text = corrupted.ToString();
            OverlappingCount.Text = overlapping.ToString();

            ApplyFilter();
        }

        private void CategoryFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb) return;
            var tag = tb.Tag as string;
            if (!int.TryParse(tag, out var key)) return;

            if (tb.IsChecked == true) _visibleCategories.Add(key);
            else _visibleCategories.Remove(key);

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filtered = _allRows.Where(r => _visibleCategories.Contains(r.CategoryKey)).ToList();
            AuditListBox.ItemsSource = filtered;

            if (_totalCount == 0)
                FilterStatusTextBlock.Text = "No issues found. All voice-over references are healthy.";
            else if (filtered.Count == _totalCount)
                FilterStatusTextBlock.Text = "Click a category to toggle it on/off.";
            else
                FilterStatusTextBlock.Text = $"Showing {filtered.Count} of {_totalCount}.  Click a category to toggle.";
        }

        private void AuditListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = AuditListBox.SelectedItem as Row;
            ShowFragmentButton.IsEnabled = selected != null && _onNavigateFragment != null;
            ShowAssetButton.IsEnabled = selected != null && selected.HasAsset && _onNavigateAsset != null;
        }

        private void ShowFragmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (AuditListBox.SelectedItem is not Row row || _onNavigateFragment == null) return;
            var idx = _allRows.ToList().IndexOf(row);
            if (idx < 0) return;
            _onNavigateFragment(idx);
        }

        private void ShowAssetButton_Click(object sender, RoutedEventArgs e)
        {
            if (AuditListBox.SelectedItem is not Row row || !row.HasAsset || _onNavigateAsset == null) return;
            var idx = _allRows.ToList().IndexOf(row);
            if (idx < 0) return;
            _onNavigateAsset(idx);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
