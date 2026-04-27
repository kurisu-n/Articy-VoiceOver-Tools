using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Articy.Api;
using Articy.Api.Plugins;
using Articy.ModelFramework;

namespace Kurisu.VoiceOverTools
{
    public partial class Plugin
    {
        private enum AuditCategory
        {
            Missing,
            Corrupted,
            Overlapping
        }

        private sealed class AuditEntry
        {
            public ObjectProxy Fragment;
            public ObjectProxy Asset;     // null for Missing
            public string LanguageCode;
            public string LocalIdHex;
            public string SourcePath;
            public string Detail;
            public AuditCategory Category;
        }

        private void AuditVoiceOvers(MacroCommandDescriptor aDescriptor, List<ObjectProxy> aSelectedobjects)
        {
            var fragments = CollectAllDialogueFragments();
            if (fragments == null || fragments.Count == 0)
            {
                var empty = new MessageWindow { Title = "Voice-Over Audit" };
                empty.MessageTextBlock.Text = "No DialogueFragments found in the project.";
                Session.ShowDialog(empty);
                return;
            }

            var (entries, totalReferences) = BuildAudit(fragments);

            int missing     = entries.Count(e => e.Category == AuditCategory.Missing);
            int corrupted   = entries.Count(e => e.Category == AuditCategory.Corrupted);
            int overlapping = entries.Count(e => e.Category == AuditCategory.Overlapping);

            var rows = entries.Select(BuildAuditRow).ToList();

            var window = new VoiceOverAuditWindow();
            var scopeSummary =
                $"{fragments.Count} DialogueFragment(s) scanned  |  " +
                $"{totalReferences} voice-over reference(s) checked  |  " +
                $"{entries.Count} issue(s) found";

            window.Populate(
                scopeSummary,
                rows,
                missing, corrupted, overlapping,
                onNavigateFragment: idx => NavigateToObject(entries[idx].Fragment),
                onNavigateAsset:    idx => NavigateToObject(entries[idx].Asset));

            Session.ShowDialog(window);
        }

        private (List<AuditEntry> entries, int totalReferences) BuildAudit(IReadOnlyList<ObjectProxy> fragments)
        {
            var entries = new List<AuditEntry>();
            int totalReferences = 0;

            var voLanguages = Session.GetVoiceOverLanguages();
            if (voLanguages == null || voLanguages.Count == 0) return (entries, 0);

            // Pass 1: per-fragment, per-language scan for Missing + Corrupted.
            // Also build the asset-usage map for overlap detection in pass 2.
            var assetUsage = new Dictionary<ulong, List<(ObjectProxy fragment, string culture, string srcPath)>>();

            foreach (var fragment in fragments)
            {
                if (fragment == null || fragment.ObjectType != ObjectType.DialogueFragment) continue;

                var localId = DecimalToHex(fragment.Id.ToString());
                var properties = fragment.GetAvailableProperties();
                if (properties == null) continue;

                foreach (var prop in properties)
                {
                    if (prop == "BBCodeText") continue;
                    var info = fragment.GetPropertyInfo(prop);
                    if (info is not { HasVoiceOver: true }) continue;
                    if (fragment[prop] is not TextProxy text) continue;

                    foreach (var lang in voLanguages)
                    {
                        var culture = lang.CultureName;
                        if (string.IsNullOrEmpty(culture)) continue;

                        totalReferences++;

                        var asset = text.VoiceOverReferences[culture];

                        if (asset == null || !asset.IsValid || asset.ObjectType != ObjectType.Asset)
                        {
                            entries.Add(new AuditEntry
                            {
                                Fragment = fragment,
                                Asset = null,
                                LanguageCode = culture,
                                LocalIdHex = localId,
                                Category = AuditCategory.Missing,
                                Detail = "no VO asset assigned for this language"
                            });
                            continue;
                        }

                        var srcPath = asset[ObjectPropertyNames.AbsoluteFilePath] as string;
                        var corruptionReason = DetectCorruption(srcPath);
                        if (corruptionReason != null)
                        {
                            entries.Add(new AuditEntry
                            {
                                Fragment = fragment,
                                Asset = asset,
                                LanguageCode = culture,
                                LocalIdHex = localId,
                                SourcePath = srcPath,
                                Category = AuditCategory.Corrupted,
                                Detail = corruptionReason
                            });
                            continue;
                        }

                        // Healthy — track for overlap detection.
                        if (!assetUsage.TryGetValue(asset.Id, out var list))
                            assetUsage[asset.Id] = list = new List<(ObjectProxy, string, string)>();
                        list.Add((fragment, culture, srcPath));
                    }
                }
            }

            // Pass 2: overlap detection. An asset shared by >1 distinct fragments is flagged.
            foreach (var kvp in assetUsage)
            {
                var refs = kvp.Value;
                var distinctFragmentIds = refs.Select(r => r.fragment.Id).Distinct().Count();
                if (distinctFragmentIds < 2) continue;

                foreach (var (fragment, culture, srcPath) in refs)
                {
                    // Re-look-up the asset from the first ref (any will do; same id).
                    ObjectProxy asset = null;
                    var anyRef = refs.FirstOrDefault();
                    // Walk back to fragment's ref to get a valid proxy:
                    var properties = fragment.GetAvailableProperties();
                    if (properties != null)
                    {
                        foreach (var prop in properties)
                        {
                            var info = fragment.GetPropertyInfo(prop);
                            if (info is not { HasVoiceOver: true }) continue;
                            if (fragment[prop] is not TextProxy text) continue;
                            var candidate = text.VoiceOverReferences[culture];
                            if (candidate != null && candidate.IsValid && candidate.Id == kvp.Key)
                            {
                                asset = candidate;
                                break;
                            }
                        }
                    }

                    var fileName = string.IsNullOrEmpty(srcPath) ? "(unknown)" : Path.GetFileName(srcPath);
                    entries.Add(new AuditEntry
                    {
                        Fragment = fragment,
                        Asset = asset,
                        LanguageCode = culture,
                        LocalIdHex = DecimalToHex(fragment.Id.ToString()),
                        SourcePath = srcPath,
                        Category = AuditCategory.Overlapping,
                        Detail = $"shared with {distinctFragmentIds - 1} other fragment(s) — file: {fileName}"
                    });
                }
            }

            return (entries, totalReferences);
        }

        private static string DetectCorruption(string srcPath)
        {
            if (string.IsNullOrEmpty(srcPath))
                return "asset has no AbsoluteFilePath";

            if (!File.Exists(srcPath))
                return $"file missing on disk: {Path.GetFileName(srcPath)}";

            try
            {
                var info = new FileInfo(srcPath);
                if (info.Length == 0)
                    return $"file is 0 bytes: {Path.GetFileName(srcPath)}";
            }
            catch (Exception ex)
            {
                return $"file inaccessible: {ex.Message}";
            }

            return null; // healthy
        }

        private static VoiceOverAuditWindow.Row BuildAuditRow(AuditEntry entry)
        {
            string icon;
            Brush brush;
            string categoryLabel;
            switch (entry.Category)
            {
                case AuditCategory.Missing:     icon = "⌀"; brush = IconWarnBrush;   categoryLabel = "Missing VO";    break;
                case AuditCategory.Corrupted:   icon = "✗"; brush = IconDangerBrush; categoryLabel = "Corrupted VO";  break;
                case AuditCategory.Overlapping: icon = "⇄"; brush = IconAccentBrush; categoryLabel = "Overlapping VO"; break;
                default:                        icon = "?";      brush = Brushes.Gray;   categoryLabel = "Unknown";        break;
            }

            var header = $"{entry.LocalIdHex}  [{entry.LanguageCode}]  —  {categoryLabel}";

            return new VoiceOverAuditWindow.Row
            {
                Icon = icon,
                IconBrush = brush,
                Header = header,
                Detail = entry.Detail ?? "",
                HasAsset = entry.Asset != null,
                CategoryKey = (int)entry.Category
            };
        }
    }
}
