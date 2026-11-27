using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Articy.Api;
using Articy.Api.Data;
using Kurisu.VoiceOverTools.Core;

namespace Kurisu.VoiceOverTools
{
    /// <summary>
    /// Match Language Voice-Overs window: pick where the voice-overs are, the asset folder with the new
    /// language's audio and the target language, preview, then bind the ticked rows. Nothing changes
    /// before Bind is clicked.
    /// </summary>
    public partial class LanguageMatchWindow : Window
    {
        private Plugin mPlugin;
        private ObjectProxy mRoot;
        private ObjectProxy mFolder;
        private List<TransferRow> mRows;
        private string mPlannedCulture;

        public LanguageMatchWindow()
        {
            InitializeComponent();

            // Articy's modal ShowDialog sometimes opens without focus; activate on Loaded so the
            // first click registers (same workaround as the other windows).
            Loaded += (_, _) =>
            {
                Activate();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            };
        }

        internal void Initialize(Plugin aPlugin, ObjectProxy aRoot, ObjectProxy aFolder, List<ApiLanguageInfo> aLanguages)
        {
            mPlugin = aPlugin;
            foreach (var language in aLanguages)
                LanguageComboBox.Items.Add(language.CultureName);
            // Default to the first voice-over language that is not the main one: that is usually the new one.
            var primary = aLanguages.FirstOrDefault(l => l.IsPrimary)?.CultureName;
            LanguageComboBox.SelectedItem = aLanguages.Select(l => l.CultureName).FirstOrDefault(c => c != primary) ?? primary;

            SetRoot(aRoot);
            SetFolder(aFolder);
        }

        private void SetRoot(ObjectProxy aRoot)
        {
            mRoot = aRoot;
            RootTextBlock.Text = Plugin.DescribeObject(aRoot);
            ClearPlan();
        }

        private void SetFolder(ObjectProxy aFolder)
        {
            mFolder = aFolder;
            FolderTextBlock.Text = aFolder == null ? "(pick an asset folder)" : Plugin.DescribeObject(aFolder);
            ClearPlan();
        }

        private void ClearPlan()
        {
            mRows = null;
            mPlannedCulture = null;
            if (RowsListBox != null) RowsListBox.ItemsSource = null;
            if (SummaryTextBlock != null) SummaryTextBlock.Text = "";
            if (BindButton != null) BindButton.IsEnabled = false;
        }

        private void PickRootButton_Click(object aSender, RoutedEventArgs aArgs) => SetRoot(mPlugin.PickObject(mRoot));

        private void PickFolderButton_Click(object aSender, RoutedEventArgs aArgs) => SetFolder(mPlugin.PickAssetFolder(mFolder));

        private void Option_Changed(object aSender, RoutedEventArgs aArgs) => ClearPlan();

        private void PreviewButton_Click(object aSender, RoutedEventArgs aArgs)
        {
            var culture = LanguageComboBox.SelectedItem as string;
            if (mRoot == null) { SummaryTextBlock.Text = "Pick where the voice-overs are."; return; }
            if (mFolder == null) { SummaryTextBlock.Text = "Pick the asset folder with the new voice-overs."; return; }
            if (string.IsNullOrEmpty(culture)) { SummaryTextBlock.Text = "Pick the target language."; return; }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                mRows = mPlugin.PlanLanguageMatch(mRoot, mFolder, SubfoldersCheckBox.IsChecked == true, culture);
                mPlannedCulture = culture;
            }
            catch (Exception ex)
            {
                SummaryTextBlock.Text = "Preview failed: " + ex.Message;
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            RowsListBox.ItemsSource = mRows;
            SummaryTextBlock.Text = Summarize(mRows, culture);
            BindButton.IsEnabled = mRows.Any(r => r.Selectable || (r.HasChoices && r.Status == TransferStatus.Conflict));
        }

        private static string Summarize(List<TransferRow> aRows, string aCulture)
        {
            if (aRows.Count == 0) return $"Nothing to match for {aCulture}: no slot shares a name with an asset in the folder.";
            return $"{aCulture}: " + string.Join(" · ", aRows.GroupBy(r => r.StatusText).Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}"));
        }

        private void BindButton_Click(object aSender, RoutedEventArgs aArgs)
        {
            if (mRows == null || mPlannedCulture == null) return;
            var count = VoiceOverTransferCore.BindingsToApply(mRows).Count();
            if (count == 0)
            {
                FooterTextBlock.Text = "Nothing is ticked or picked.";
                return;
            }
            if (MessageBox.Show(this, $"Bind {count} {mPlannedCulture} voice-over(s)? Work on a copy or have a backup.", Title,
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            var (bound, failed) = mPlugin.ApplyLanguageMatch(mRows, mPlannedCulture);
            FooterTextBlock.Text = $"Bound {bound}" + (failed > 0 ? $", {failed} failed (see the rows)." : ".");
            RowsListBox.Items.Refresh();
            SummaryTextBlock.Text = Summarize(mRows, mPlannedCulture);
            BindButton.IsEnabled = mRows.Any(r => r.Selectable || (r.HasChoices && r.Status == TransferStatus.Conflict));
        }

        private void CloseButton_Click(object aSender, RoutedEventArgs aArgs) => Close();
    }
}
