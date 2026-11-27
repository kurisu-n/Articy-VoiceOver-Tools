using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Articy.Api;
using Articy.Api.Plugins;
using Articy.ModelFramework;
using Kurisu.VoiceOverTools.Core;

namespace Kurisu.VoiceOverTools
{
    // Export / Import Voice-Overs: the articy side. It turns the voice-overs under the picked object
    // into VoRecords, asks VoiceOverTransferCore for a plan (all naming and matching decisions live
    // there, testable without articy), and carries the plan out with articy calls.
    public partial class Plugin
    {
        private void OpenVoiceOverTransfer(MacroCommandDescriptor aDescriptor, List<ObjectProxy> aSelectedObjects)
        {
            var preselected = aSelectedObjects?.FirstOrDefault(o => o != null && o.IsValid)
                ?? Session.GetSystemFolder(SystemFolderNames.Flow);

            var window = new VoiceOverTransferWindow();
            window.Initialize(this, preselected, Session.GetVoiceOverLanguages() ?? new List<Articy.Api.Data.ApiLanguageInfo>(), Session.GetPrimaryLanguage()?.CultureName);
            Session.ShowDialog(window);
        }

        /// <summary>Opens articy's own object picker, starting at the current choice.</summary>
        internal ObjectProxy PickObject(ObjectProxy aCurrent)
        {
            var picker = new Articy.Api.UI.ObjectPickerDialog
            {
                RootObject = Session.GetSystemFolder(SystemFolderNames.Flow),
                SelectedObject = aCurrent,
                ShowHierarchy = true,
                AllowEmptySelection = false,
                ObjectFilter = o => o != null && (o.CanHaveChildren || o.ObjectType == ObjectType.DialogueFragment),
                TitleLid = "Pick the object to export or import voice-overs under",
                DescriptionLid = "Every voice-over below the picked object is included.",
            };
            return picker.ShowDialog() && picker.SelectedObject != null ? picker.SelectedObject : aCurrent;
        }

        internal static string DescribeObject(ObjectProxy aObject)
        {
            if (aObject == null) return "(nothing picked)";
            var path = TryGetFragmentPath(aObject);
            return string.IsNullOrEmpty(path) ? aObject.GetDisplayName() : path;
        }

        /// <summary>Every voice-over binding under the root, for the given cultures.</summary>
        internal List<VoRecord> CollectVoiceOvers(ObjectProxy aRoot, IReadOnlyCollection<string> aCultures)
        {
            var result = new List<VoRecord>();
            if (aRoot == null) return result;

            foreach (var fragment in TraverseDialogueFragments(aRoot))
            {
                var speaker = TryGetSpeakerName(fragment);
                var hierarchy = HierarchyBetween(aRoot, fragment);
                var path = TryGetFragmentPath(fragment);

                foreach (var (_, text) in EnumerateVoiceOverTextProxies(fragment))
                {
                    foreach (var culture in aCultures)
                    {
                        var asset = text.VoiceOverReferences[culture];
                        if (asset == null || !asset.IsValid) continue;

                        result.Add(new VoRecord
                        {
                            AssetId = asset.Id,
                            FragmentId = fragment.Id,
                            Culture = culture,
                            SourcePath = asset[ObjectPropertyNames.AbsoluteFilePath] as string,
                            Speaker = speaker,
                            FragmentPath = path,
                            Hierarchy = hierarchy,
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>Display names of the objects strictly between the root and the fragment, top first.</summary>
        private static List<string> HierarchyBetween(ObjectProxy aRoot, ObjectProxy aFragment)
        {
            var names = new List<string>();
            var node = aFragment.GetParent();
            while (node != null && node.IsValid && node.Id != aRoot.Id)
            {
                names.Insert(0, node.GetDisplayName() ?? "");
                node = node.GetParent();
            }
            return names;
        }

        internal List<TransferRow> PlanExport(ObjectProxy aRoot, IReadOnlyCollection<string> aCultures, TransferSettings aSettings)
        {
            return VoiceOverTransferCore.PlanExport(CollectVoiceOvers(aRoot, aCultures), aSettings);
        }

        internal List<TransferRow> PlanImport(ObjectProxy aRoot, IReadOnlyCollection<string> aCultures, TransferSettings aSettings)
        {
            var files = Directory.EnumerateFiles(aSettings.Folder, "*", SearchOption.AllDirectories);
            return VoiceOverTransferCore.PlanImport(CollectVoiceOvers(aRoot, aCultures), aSettings, files, SameContent);
        }

        private static bool SameContent(string aA, string aB)
        {
            try
            {
                if (aB == null || !File.Exists(aB)) return false;
                if (new FileInfo(aA).Length != new FileInfo(aB).Length) return false;
                using var sha = SHA256.Create();
                using var streamA = File.OpenRead(aA);
                using var streamB = File.OpenRead(aB);
                return sha.ComputeHash(streamA).SequenceEqual(sha.ComputeHash(streamB));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How long the import waits for articy to finish copying a replacement in the background.</summary>
        private const double ReplaceLandTimeoutSeconds = 10;

        /// <summary>
        /// Replaces each chosen asset's file. The new file is first copied to a temporary file named like
        /// the stored file without its tag (articy appends its own), so an exported name such as one with
        /// the main-language suffix stripped never becomes the asset's file name.
        ///
        /// Two articy behaviours, both measured by the self-test on 2026-11-27, shape the rest. articy
        /// silently skips a replacement whose file name and date match the last file it imported for that
        /// asset, so the staged copy always gets the current date. And articy finishes the copy in the
        /// background, up to about a second after WaitForAssetProcessing returns, so each row waits for the
        /// stored file to hold the new audio before it counts as replaced, and the staged copies are kept
        /// until the end. The asset's display name, description and external id are put back afterwards in
        /// case the replacement resets them.
        /// </summary>
        internal (int Replaced, int Failed) RunImport(TransferSettings aSettings, List<TransferRow> aRows)
        {
            int replaced = 0, failed = 0;
            var temp = Path.Combine(Path.GetTempPath(), "Kurisu.VoiceOverImport", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                foreach (var row in VoiceOverTransferCore.RowsToApply(aRows).ToList())
                {
                    try
                    {
                        var asset = Session.GetObjectById(row.Record.AssetId);
                        if (asset == null || !asset.IsValid)
                            throw new InvalidOperationException("The voice-over asset no longer exists.");

                        var source = Path.Combine(aSettings.Folder, row.Choice);

                        // One folder per row, so two rows can never share a staged path.
                        var rowFolder = Path.Combine(temp, (replaced + failed).ToString());
                        Directory.CreateDirectory(rowFolder);
                        var staged = Path.Combine(rowFolder, VoiceOverTransferCore.StagedFileName(row.Record, source));
                        File.Copy(source, staged, true);
                        File.SetLastWriteTimeUtc(staged, DateTime.UtcNow);

                        var displayName = asset.HasProperty(ObjectPropertyNames.DisplayName) ? asset[ObjectPropertyNames.DisplayName] : null;
                        var text = asset.HasProperty(ObjectPropertyNames.Text) ? asset[ObjectPropertyNames.Text] : null;
                        var externalId = asset.HasProperty(ObjectPropertyNames.ExternalId) ? asset[ObjectPropertyNames.ExternalId] : null;

                        Session.ChangeAsset(asset, staged);
                        Session.WaitForAssetProcessing(new WaitForAssetProcessingArgs());

                        // ChangeAsset can return without error and still leave the old file in place, so the
                        // stored file itself is the only proof that the replacement happened.
                        if (!WaitForStoredContent(asset, source, ReplaceLandTimeoutSeconds))
                            throw new InvalidOperationException("articy kept the old file: the stored voice-over did not change.");

                        if (displayName != null) asset[ObjectPropertyNames.DisplayName] = displayName;
                        if (text != null) asset[ObjectPropertyNames.Text] = text;
                        if (externalId != null) asset[ObjectPropertyNames.ExternalId] = externalId;

                        row.Status = TransferStatus.Done;
                        row.Selectable = row.Selected = false;
                        row.Choices = null;
                        row.Detail = "replaced from " + row.Choice + " · now " + Path.GetFileName(asset[ObjectPropertyNames.AbsoluteFilePath] as string ?? "");
                        replaced++;
                    }
                    catch (Exception ex)
                    {
                        row.Status = TransferStatus.Failed;
                        row.Detail = ex.Message;
                        failed++;
                    }
                }

                Session.WaitForAssetProcessing(new WaitForAssetProcessingArgs());
                DrainDispatcher();
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { /* articy may still hold a staged file; the temp folder is harmless */ }
            }

            return (replaced, failed);
        }

        /// <summary>
        /// Waits until the asset's stored file holds the expected audio, letting articy's dispatcher run
        /// between checks so its background copy can finish. Returns false if it has not arrived in time.
        /// </summary>
        private bool WaitForStoredContent(ObjectProxy aAsset, string aExpected, double aTimeoutSeconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                if (aAsset[ObjectPropertyNames.AbsoluteFilePath] is string stored && SameContent(stored, aExpected))
                    return true;
                if (clock.Elapsed.TotalSeconds >= aTimeoutSeconds)
                    return false;
                DrainDispatcher();
                System.Threading.Thread.Sleep(200);
            }
        }
    }
}
