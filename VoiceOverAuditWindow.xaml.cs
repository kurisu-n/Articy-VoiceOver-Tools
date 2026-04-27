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
            public string PropertyName { get; set; }
            public string PropertyLabel { get; set; }
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
        private bool _includeEmpty;
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
            int empty,
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
            EmptyCount.Text = empty.ToString();

            // Build the character filter list with "(all characters)" first.
            var characters = new List<string> { AllCharactersOption };
            characters.AddRange(speakers);
            CharacterFilterComboBox.ItemsSource = characters;
            CharacterFilterComboBox.SelectedIndex = 0;
            _selectedSpeaker = AllCharactersOption;

            // Build the hierarchical path picker (ContextMenu attached to the dropdown button).
            BuildPathContextMenu(pathPrefixes);
            _selectedPath = AllPathsOption;
            PathDropdownLabel.Text = AllPathsOption;

            BuildPropertyToggles(propertyNames);

            ApplyFilter();
        }

        // ─── Empty toggle (4th category-style pill) ─────────────────────────────────────────────

        private void EmptyFilter_Click(object sender, RoutedEventArgs e)
        {
            _includeEmpty = EmptyToggle.IsChecked == true;
            ApplyFilter();
        }

        // ─── Property toggles (dynamic per audit) ───────────────────────────────────────────────

        private void BuildPropertyToggles(IReadOnlyList<string> propertyNames)
        {
            PropertyTogglesPanel.Children.Clear();
            _visibleProperties.Clear();

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

        private void PropertyFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb) return;
            var name = tb.Tag as string;
            if (string.IsNullOrEmpty(name)) return;

            if (tb.IsChecked == true) _visibleProperties.Add(name);
            else _visibleProperties.Remove(name);

            ApplyFilter();
        }

        // ─── Category toggles ───────────────────────────────────────────────────────────────────

        private void CategoryFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb) return;
            var tag = tb.Tag as string;
            if (!int.TryParse(tag, out var key)) return;

            if (tb.IsChecked == true) _visibleCategories.Add(key);
            else _visibleCategories.Remove(key);

            ApplyFilter();
        }

        // ─── Character filter ───────────────────────────────────────────────────────────────────

        private void CharacterFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CharacterFilterComboBox.SelectedItem is string s) _selectedSpeaker = s;
            ApplyFilter();
        }

        // ─── Path filter (hierarchical ContextMenu) ─────────────────────────────────────────────

        private sealed class PathNode
        {
            public string Segment;
            public string FullPath;
            public List<PathNode> Children = new();
        }

        private void BuildPathContextMenu(IReadOnlyList<string> pathPrefixes)
        {
            var menu = new ContextMenu();

            // "(all paths)" reset entry
            var allItem = new MenuItem { Header = AllPathsOption };
            allItem.Click += (s, e) => { SelectPath(AllPathsOption); e.Handled = true; };
            menu.Items.Add(allItem);
            menu.Items.Add(new Separator());

            // Build the tree from the flat prefix list, then realize it as nested MenuItems.
            var root = new PathNode();
            if (pathPrefixes != null)
            {
                foreach (var prefix in pathPrefixes)
                {
                    if (string.IsNullOrEmpty(prefix)) continue;
                    var segments = prefix.Split(new[] { " / " }, StringSplitOptions.None);
                    var current = root;
                    var built = "";
                    for (int i = 0; i < segments.Length; i++)
                    {
                        built = i == 0 ? segments[0] : built + " / " + segments[i];
                        var existing = current.Children.FirstOrDefault(c => c.Segment == segments[i]);
                        if (existing == null)
                        {
                            existing = new PathNode { Segment = segments[i], FullPath = built };
                            current.Children.Add(existing);
                        }
                        current = existing;
                    }
                }
            }
            SortTree(root);

            foreach (var node in root.Children)
                menu.Items.Add(BuildMenuItem(node));

            PathDropdownButton.ContextMenu = menu;
        }

        private static void SortTree(PathNode node)
        {
            node.Children.Sort((a, b) => StringComparer.Ordinal.Compare(a.Segment, b.Segment));
            foreach (var c in node.Children) SortTree(c);
        }

        /// <summary>
        /// Builds a MenuItem for a path node. Leaves get a direct Click that selects the path.
        /// Nodes with children get a synthetic "(this folder)" leaf at the top of their submenu
        /// so the user can pick the prefix at any intermediate level.
        /// </summary>
        private MenuItem BuildMenuItem(PathNode node)
        {
            var item = new MenuItem { Header = node.Segment };

            if (node.Children.Count > 0)
            {
                var selfItem = new MenuItem { Header = "↩ " + node.Segment + " (this folder)" };
                selfItem.Click += (s, e) => { SelectPath(node.FullPath); e.Handled = true; };
                item.Items.Add(selfItem);
                item.Items.Add(new Separator());

                foreach (var child in node.Children)
                    item.Items.Add(BuildMenuItem(child));
            }
            else
            {
                item.Click += (s, e) => { SelectPath(node.FullPath); e.Handled = true; };
            }

            return item;
        }

        private void SelectPath(string path)
        {
            _selectedPath = path;
            PathDropdownLabel.Text = string.IsNullOrEmpty(path) ? AllPathsOption : path;
            ApplyFilter();
        }

        private void PathDropdownButton_Click(object sender, RoutedEventArgs e)
        {
            if (PathDropdownButton.ContextMenu == null) return;
            PathDropdownButton.ContextMenu.PlacementTarget = PathDropdownButton;
            PathDropdownButton.ContextMenu.Placement = PlacementMode.Bottom;
            PathDropdownButton.ContextMenu.MinWidth = PathDropdownButton.ActualWidth;
            PathDropdownButton.ContextMenu.IsOpen = true;
        }

        // ─── Filter pipeline ────────────────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            IEnumerable<Row> filtered = _allRows.Where(r => _visibleCategories.Contains(r.CategoryKey));

            if (_selectedSpeaker != AllCharactersOption)
                filtered = filtered.Where(r => string.Equals(r.SpeakerName, _selectedSpeaker, StringComparison.Ordinal));

            if (_selectedPath != AllPathsOption)
            {
                var prefix = _selectedPath;
                var prefixWithSep = prefix + " / ";
                filtered = filtered.Where(r =>
                    !string.IsNullOrEmpty(r.FragmentPath) &&
                    (r.FragmentPath == prefix || r.FragmentPath.StartsWith(prefixWithSep, StringComparison.Ordinal)));
            }

            if (PropertyAreaPanel.Visibility == Visibility.Visible)
                filtered = filtered.Where(r => string.IsNullOrEmpty(r.PropertyName) || _visibleProperties.Contains(r.PropertyName));

            // Empty-line filter — opt-in via the EmptyToggle (4th category pill, default unchecked).
            if (!_includeEmpty)
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

        // ─── Selection / navigation ─────────────────────────────────────────────────────────────

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
