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
            public string CategoryLabel { get; set; }
            public string IdAndLang { get; set; }
            public string SpeakerName { get; set; }
            public string FragmentPath { get; set; }
            public string LineText { get; set; }
            public string Detail { get; set; }
            public string PropertyName { get; set; }      // raw, e.g. "Text"
            public string PropertyLabel { get; set; }     // display, e.g. ".Text"
            public bool HasAsset { get; set; }
            public int CategoryKey { get; set; }
        }

        private const string AllCharactersOption = "(all characters)";
        private const string AllPathsOption = "(all paths)";

        private Action<int> _onNavigateFragment;
        private Action<int> _onNavigateAsset;

        private IReadOnlyList<Row> _allRows = new List<Row>();
        private readonly HashSet<int> _visibleCategories = new() { 0, 1, 2 };
        private readonly HashSet<string> _visibleProperties = new(StringComparer.Ordinal);
        private string _selectedSpeaker = AllCharactersOption;
        private string _selectedPath = AllPathsOption;
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
            IReadOnlyList<string> speakers,
            IReadOnlyList<string> propertyNames,
            IReadOnlyList<string> pathPrefixes,
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

            // Build the character filter list with "(all characters)" first.
            var characters = new List<string> { AllCharactersOption };
            characters.AddRange(speakers);
            CharacterFilterComboBox.ItemsSource = characters;
            CharacterFilterComboBox.SelectedIndex = 0;
            _selectedSpeaker = AllCharactersOption;

            // Build the path filter list with "(all paths)" first.
            var paths = new List<string> { AllPathsOption };
            paths.AddRange(pathPrefixes);
            PathFilterComboBox.ItemsSource = paths;
            PathFilterComboBox.SelectedIndex = 0;
            _selectedPath = AllPathsOption;

            BuildPropertyToggles(propertyNames);

            ApplyFilter();
        }

        /// <summary>
        /// Builds one toggle per property name found in the audit. The property toggle area
        /// is hidden when only one property type is present (no point cluttering the UI),
        /// but the surrounding toolbar row stays visible because the empty-lines checkbox
        /// always belongs there.
        /// </summary>
        private void BuildPropertyToggles(IReadOnlyList<string> propertyNames)
        {
            PropertyTogglesPanel.Children.Clear();
            _visibleProperties.Clear();

            // Always populate the visible-set: even when only one property type is present
            // we want it to pass the filter.
            if (propertyNames != null)
                foreach (var p in propertyNames)
                    _visibleProperties.Add(p);

            if (propertyNames == null || propertyNames.Count <= 1)
            {
                PropertyAreaPanel.Visibility = Visibility.Collapsed;
                return;
            }

            PropertyAreaPanel.Visibility = Visibility.Visible;

            var style = (Style)FindResource("CategoryToggleStyle");
            foreach (var name in propertyNames)
            {
                var label = new TextBlock
                {
                    Text = "." + name,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Margin = new Thickness(4, 1, 4, 1)
                };

                var tb = new ToggleButton
                {
                    Style = style,
                    IsChecked = true,
                    Tag = name,
                    Content = label,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                tb.Click += PropertyFilter_Click;
                PropertyTogglesPanel.Children.Add(tb);
            }
        }

        private void IncludeEmptyCheckbox_Click(object sender, RoutedEventArgs e)
        {
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

        private void PropertyFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb) return;
            var name = tb.Tag as string;
            if (string.IsNullOrEmpty(name)) return;

            if (tb.IsChecked == true) _visibleProperties.Add(name);
            else _visibleProperties.Remove(name);

            ApplyFilter();
        }

        private void CharacterFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CharacterFilterComboBox.SelectedItem is string s) _selectedSpeaker = s;
            ApplyFilter();
        }

        private void PathFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PathFilterComboBox.SelectedItem is string s) _selectedPath = s;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<Row> filtered = _allRows.Where(r => _visibleCategories.Contains(r.CategoryKey));

            if (_selectedSpeaker != AllCharactersOption)
                filtered = filtered.Where(r => string.Equals(r.SpeakerName, _selectedSpeaker, StringComparison.Ordinal));

            // Path filter — picking a prefix matches the row's path either exactly (selected leaf)
            // or as an ancestor (selected an intermediate folder).
            if (_selectedPath != AllPathsOption)
            {
                var prefix = _selectedPath;
                var prefixWithSep = prefix + " / ";
                filtered = filtered.Where(r =>
                    !string.IsNullOrEmpty(r.FragmentPath) &&
                    (r.FragmentPath == prefix || r.FragmentPath.StartsWith(prefixWithSep, StringComparison.Ordinal)));
            }

            // Property filter (only effective when the property toggle area is visible — i.e. >1
            // property type was found in the audit).
            if (PropertyAreaPanel.Visibility == Visibility.Visible)
                filtered = filtered.Where(r => string.IsNullOrEmpty(r.PropertyName) || _visibleProperties.Contains(r.PropertyName));

            // Empty-line filter — opt-in via the "Include empty lines" checkbox, default off.
            // Hides rows where the dialogue line is blank, which usually means the fragment
            // is just a placeholder / not yet written and would be noise in the audit.
            if (IncludeEmptyCheckbox.IsChecked != true)
                filtered = filtered.Where(r => !string.IsNullOrWhiteSpace(r.LineText));

            var list = filtered.ToList();
            AuditListBox.ItemsSource = list;

            if (_totalCount == 0)
                FilterStatusTextBlock.Text = "No issues found. All voice-over references are healthy.";
            else if (list.Count == _totalCount)
                FilterStatusTextBlock.Text = "Click a category, property, character, or path to filter.";
            else
                FilterStatusTextBlock.Text = $"Showing {list.Count} of {_totalCount}.  Click a category, property, character, or path.";
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
