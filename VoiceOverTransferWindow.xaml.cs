using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Articy.Api;
using Articy.Api.Data;
using Kurisu.VoiceOverTools.Core;
using Microsoft.Win32;

namespace Kurisu.VoiceOverTools
{
    /// <summary>
    /// Export / Import Voice-Overs window. Both modes share the object, folder, languages and main
    /// language; Preview builds the plan, and the run button applies the ticked rows. Nothing is
    /// written before the run button is clicked.
    /// </summary>
    public partial class VoiceOverTransferWindow : Window
    {
        private const string NoMainLanguage = "(none)";

        private Plugin mPlugin;
        private ObjectProxy mRoot;
        private bool mImportMode;
        private List<TransferRow> mRows;
        private TransferSettings mPlannedSettings;
        private List<string> mPlannedCultures;

        public VoiceOverTransferWindow()
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

        internal void Initialize(Plugin aPlugin, ObjectProxy aRoot, List<ApiLanguageInfo> aLanguages, string aPrimaryCulture)
        {
            mPlugin = aPlugin;
            SetRoot(aRoot);

            foreach (var language in aLanguages)
            {
                LanguagesPanel.Children.Add(new CheckBox
                {
                    Content = $"{language.DisplayName} ({language.CultureName})",
                    Tag = language.CultureName,
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 14, 0),
                });
            }

            MainLanguageComboBox.Items.Add(NoMainLanguage);
            foreach (var language in aLanguages)
                MainLanguageComboBox.Items.Add(language.CultureName);
            MainLanguageComboBox.SelectedItem = aLanguages.Any(l => l.CultureName == aPrimaryCulture) ? aPrimaryCulture : NoMainLanguage;

            for (int i = 0; i <= 6; i++)
                LevelsComboBox.Items.Add(i);
            LevelsComboBox.SelectedIndex = 0;

            SpeakerComboBox.Items.Add("None");
            SpeakerComboBox.Items.Add("Before the hierarchy");
            SpeakerComboBox.Items.Add("After the hierarchy");
            SpeakerComboBox.SelectedIndex = 0;

            ExistingComboBox.Items.Add("Skip");
            ExistingComboBox.Items.Add("Overwrite");
            ExistingComboBox.SelectedIndex = 0;

            SetMode(false);
        }

        private void SetRoot(ObjectProxy aRoot)
        {
            mRoot = aRoot;
            ObjectTextBlock.Text = Plugin.DescribeObject(aRoot);
            ClearPlan();
        }

        private void SetMode(bool aImport)
        {
            mImportMode = aImport;
            ExportModeToggle.IsChecked = !aImport;
            ImportModeToggle.IsChecked = aImport;

            var exportOnly = aImport ? Visibility.Collapsed : Visibility.Visible;
            SplitLabel.Visibility = SplitPanel.Visibility = exportOnly;
            NamesLabel.Visibility = NamesPanel.Visibility = exportOnly;

            ModeHintTextBlock.Text = aImport
                ? "Pick the folder holding the edited files; subfolders are searched too. Files replace the voice-overs with matching names."
                : "Copies every voice-over under the object to the folder.";
            MainLanguageHint.Text = aImport
                ? "files without a language suffix are matched to this language"
                : "its files lose their _language suffix";
            RunButton.Content = aImport ? "Replace ticked" : "Export ticked";
            ClearPlan();
        }

        private void ClearPlan()
        {
            mRows = null;
            mPlannedSettings = null;
            mPlannedCultures = null;
            if (RowsListBox != null) RowsListBox.ItemsSource = null;
            if (SummaryTextBlock != null) SummaryTextBlock.Text = "";
            if (FooterTextBlock != null) FooterTextBlock.Text = "";
            if (RunButton != null) RunButton.IsEnabled = false;
        }

        private List<string> ReadCultures()
        {
            return LanguagesPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
        }

        private TransferSettings ReadSettings()
        {
            var main = MainLanguageComboBox.SelectedItem as string;
            return new TransferSettings
            {
                Folder = (FolderTextBox.Text ?? "").Trim(),
                MainCulture = main == NoMainLanguage ? null : main,
                StripTag = StripTagCheckBox.IsChecked == true,
                HierarchyLevels = LevelsComboBox.SelectedItem is int levels ? levels : 0,
                Speaker = (SpeakerSplit)Math.Max(0, SpeakerComboBox.SelectedIndex),
                FolderPerLanguage = FolderPerLanguageCheckBox.IsChecked == true,
                Overwrite = ExistingComboBox.SelectedIndex == 1,
            };
        }

        private string Validate(TransferSettings aSettings, List<string> aCultures)
        {
            if (mRoot == null) return "Pick an object first.";
            if (string.IsNullOrEmpty(aSettings.Folder) || !Path.IsPathRooted(aSettings.Folder)) return "Choose a folder.";
            if (mImportMode && !Directory.Exists(aSettings.Folder)) return "The folder does not exist.";
            if (aCultures.Count == 0) return "Tick at least one language.";
            return null;
        }

        private void PickButton_Click(object aSender, RoutedEventArgs aArgs)
        {
            SetRoot(mPlugin.PickObject(mRoot));
        }

        private void ModeToggle_Click(object aSender, RoutedEventArgs aArgs)
        {
            SetMode(aSender == ImportModeToggle);
        }

        private void BrowseButton_Click(object aSender, RoutedEventArgs aArgs)
        {
            var dialog = new OpenFolderDialog { Title = mImportMode ? "Folder with the edited voice-overs" : "Export voice-overs to" };
            if (Directory.Exists(FolderTextBox.Text))
                dialog.InitialDirectory = FolderTextBox.Text;
            if (dialog.ShowDialog(this) == true)
            {
                FolderTextBox.Text = dialog.FolderName;
                ClearPlan();
            }
        }

        private void PreviewButton_Click(object aSender, RoutedEventArgs aArgs)
        {
            var settings = ReadSettings();
            var cultures = ReadCultures();
            var problem = Validate(settings, cultures);
            if (problem != null)
            {
                SummaryTextBlock.Text = problem;
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                mRows = mImportMode ? mPlugin.PlanImport(mRoot, cultures, settings) : mPlugin.PlanExport(mRoot, cultures, settings);
                mPlannedSettings = settings;
                mPlannedCultures = cultures;
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
            SummaryTextBlock.Text = Summarize(mRows);
            FooterTextBlock.Text = mImportMode
                ? "Conflicts are never resolved for you: a conflict is applied only if you pick a file in its row."
                : "Rows that would overwrite an existing file are only ticked when Existing files is set to Overwrite.";
            RunButton.IsEnabled = mRows.Any(r => r.Selectable || r.HasChoices);
        }

        private static string Summarize(List<TransferRow> aRows)
        {
            if (aRows.Count == 0) return "Nothing found under the picked object for the ticked languages.";
            return string.Join(" · ", aRows.GroupBy(r => r.StatusText).Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}"));
        }

        private void RunButton_Click(object aSender, RoutedEventArgs aArgs)
        {
            if (mRows == null || mPlannedSettings == null) return;

            var count = mImportMode
                ? VoiceOverTransferCore.RowsToApply(mRows).Count()
                : mRows.Count(r => r.Selectable && r.Selected);
            if (count == 0)
            {
                FooterTextBlock.Text = "No rows are ticked.";
                return;
            }

            var question = mImportMode
                ? $"Replace {count} voice-over file(s) in this project? Work on a copy or have a backup."
                : $"Write {count} file(s) to {mPlannedSettings.Folder}?";
            if (MessageBox.Show(this, question, Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var (done, failed) = mImportMode ? mPlugin.RunImport(mPlannedSettings, mRows) : VoiceOverTransferCore.RunExport(mRows);
                FooterTextBlock.Text = (mImportMode ? $"Replaced {done}" : $"Wrote {done}") + (failed > 0 ? $", {failed} failed (see the rows)." : ".");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            RowsListBox.Items.Refresh();
            SummaryTextBlock.Text = Summarize(mRows);
            RunButton.IsEnabled = mRows.Any(r => r.Selectable || (r.HasChoices && r.Status == TransferStatus.Conflict));
        }

        private void CloseButton_Click(object aSender, RoutedEventArgs aArgs)
        {
            Close();
        }
    }
}
