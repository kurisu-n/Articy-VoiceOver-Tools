using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Articy.Api;
using Articy.Api.Plugins;
using Articy.ModelFramework;
using Kurisu.VoiceOverTools.Core;

namespace Kurisu.VoiceOverTools
{
    // Match Language Voice-Overs: binds audio assets already imported into articy (one asset folder,
    // one target language) to the fragments whose voice-overs in other languages carry the same name.
    // All matching decisions live in VoiceOverTransferCore.PlanLanguageMatch; this file only gathers
    // articy objects and sets the bindings.
    public partial class Plugin
    {
        private void OpenLanguageMatch(MacroCommandDescriptor aDescriptor, List<ObjectProxy> aSelectedObjects)
        {
            var root = aSelectedObjects?.FirstOrDefault(o => o != null && o.IsValid && o.ObjectType != ObjectType.Asset)
                ?? Session.GetSystemFolder(SystemFolderNames.Flow);
            var assetFolder = aSelectedObjects?.FirstOrDefault(o => o != null && o.IsValid && IsAssetFolder(o));

            var window = new LanguageMatchWindow();
            window.Initialize(this, root, assetFolder, Session.GetVoiceOverLanguages() ?? new List<Articy.Api.Data.ApiLanguageInfo>());
            Session.ShowDialog(window);
        }

        private bool IsAssetFolder(ObjectProxy aObject)
        {
            if (aObject == null || !aObject.IsValid || !aObject.IsFolder) return false;
            var assets = Session.GetSystemFolder(SystemFolderNames.Assets);
            for (var node = aObject; node != null && node.IsValid; node = node.GetParent())
                if (node.Id == assets.Id) return true;
            return false;
        }

        /// <summary>articy's picker on the Assets tree, folders only.</summary>
        internal ObjectProxy PickAssetFolder(ObjectProxy aCurrent)
        {
            var picker = new Articy.Api.UI.ObjectPickerDialog
            {
                RootObject = Session.GetSystemFolder(SystemFolderNames.Assets),
                SelectedObject = aCurrent,
                ShowHierarchy = true,
                AllowEmptySelection = false,
                ObjectFilter = o => o != null && o.IsFolder,
                TitleLid = "Pick the asset folder holding the new language's voice-overs",
                DescriptionLid = "Audio assets in this folder are matched by name to the existing voice-overs.",
            };
            return picker.ShowDialog() && picker.SelectedObject != null ? picker.SelectedObject : aCurrent;
        }

        private static string SafeDisplayName(ObjectProxy aObject)
        {
            try { return aObject.GetDisplayName(); } catch { return null; }
        }

        /// <summary>Every voice-over slot under the root, with what it holds in each voice-over language.</summary>
        internal List<LanguageSlot> CollectLanguageSlots(ObjectProxy aRoot, IReadOnlyCollection<string> aCultures)
        {
            var slots = new List<LanguageSlot>();
            if (aRoot == null) return slots;

            foreach (var fragment in TraverseDialogueFragments(aRoot))
            {
                var path = TryGetFragmentPath(fragment);
                foreach (var (property, text) in EnumerateVoiceOverTextProxies(fragment))
                {
                    var slot = new LanguageSlot { FragmentId = fragment.Id, Property = property, FragmentPath = path };
                    foreach (var culture in aCultures)
                    {
                        var asset = text.VoiceOverReferences[culture];
                        if (asset == null || !asset.IsValid) continue;
                        var file = asset[ObjectPropertyNames.AbsoluteFilePath] as string;
                        if (string.IsNullOrEmpty(file)) continue;
                        slot.Existing[culture] = Path.GetFileNameWithoutExtension(file);
                        slot.ExistingExtension[culture] = Path.GetExtension(file);
                        slot.ExistingAssets[culture] = asset.Id;
                        slot.ExistingDisplay[culture] = SafeDisplayName(asset);
                    }
                    if (slot.Existing.Count > 0)
                        slots.Add(slot);
                }
            }
            return slots;
        }

        /// <summary>Audio assets in the folder (and its subfolders if asked), with their place below it.</summary>
        internal List<CandidateAsset> CollectCandidateAssets(ObjectProxy aFolder, bool aIncludeSubfolders)
        {
            var result = new List<CandidateAsset>();
            if (aFolder == null) return result;

            void Walk(ObjectProxy aNode, string aLocation)
            {
                foreach (var child in aNode.GetChildren() ?? new List<ObjectProxy>())
                {
                    if (child == null || !child.IsValid) continue;
                    if (child.ObjectType == ObjectType.Asset)
                    {
                        var file = child[ObjectPropertyNames.AbsoluteFilePath] as string;
                        if (string.IsNullOrEmpty(file) || !VoiceOverTransferCore.AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                            continue;
                        result.Add(new CandidateAsset
                        {
                            AssetId = child.Id,
                            StoredName = Path.GetFileNameWithoutExtension(file),
                            Extension = Path.GetExtension(file),
                            Location = aLocation,
                            DisplayName = SafeDisplayName(child),
                        });
                    }
                    else if (aIncludeSubfolders && child.IsFolder)
                    {
                        Walk(child, string.IsNullOrEmpty(aLocation) ? child.GetDisplayName() : aLocation + "/" + child.GetDisplayName());
                    }
                }
            }

            Walk(aFolder, aFolder.GetDisplayName());
            return result;
        }

        internal List<TransferRow> PlanLanguageMatch(ObjectProxy aRoot, ObjectProxy aAssetFolder, bool aIncludeSubfolders, string aTargetCulture)
        {
            var cultures = (Session.GetVoiceOverLanguages() ?? new List<Articy.Api.Data.ApiLanguageInfo>()).Select(l => l.CultureName).ToList();
            var allCultures = cultures.Concat((Session.GetTextLanguages() ?? new List<Articy.Api.Data.ApiLanguageInfo>()).Select(l => l.CultureName))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return VoiceOverTransferCore.PlanLanguageMatch(CollectLanguageSlots(aRoot, cultures), CollectCandidateAssets(aAssetFolder, aIncludeSubfolders),
                aTargetCulture, allCultures);
        }

        /// <summary>Sets each chosen asset as the target-language voice-over of its slot and reads it back.</summary>
        internal (int Bound, int Failed) ApplyLanguageMatch(List<TransferRow> aRows, string aTargetCulture)
        {
            int bound = 0, failed = 0;
            foreach (var row in VoiceOverTransferCore.BindingsToApply(aRows).ToList())
            {
                try
                {
                    var fragment = Session.GetObjectById(row.Slot.FragmentId);
                    var asset = Session.GetObjectById(row.ChoiceAssets[row.Choice]);
                    if (fragment == null || !fragment.IsValid || asset == null || !asset.IsValid)
                        throw new InvalidOperationException("The fragment or the asset no longer exists.");
                    if (fragment[row.Slot.Property] is not TextProxy text)
                        throw new InvalidOperationException($"{row.Slot.Property} is no longer a voice-over text.");

                    Session.SuspendLocaStateHandling();
                    try
                    {
                        text.VoiceOverReferences[aTargetCulture] = asset;
                    }
                    finally
                    {
                        Session.ResumeLocaStateHandling();
                    }

                    var check = text.VoiceOverReferences[aTargetCulture];
                    if (check == null || check.Id != asset.Id)
                        throw new InvalidOperationException("articy did not keep the binding.");

                    row.Status = TransferStatus.Done;
                    row.Selectable = row.Selected = false;
                    row.Detail = "bound " + row.Choice;
                    row.Choices = null;
                    bound++;
                }
                catch (Exception ex)
                {
                    row.Status = TransferStatus.Failed;
                    row.Detail = ex.Message;
                    failed++;
                }
            }
            return (bound, failed);
        }
    }
}
