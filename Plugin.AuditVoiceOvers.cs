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
            public string SpeakerName;
            public string FragmentPath;   // navigator path to the fragment (root segment trimmed)
            public string LineText;       // the text the character would say in this language
            public string PropertyName;   // which text property: "Text", "PreviewText", etc.
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

            var (entries, totalScanned, totalReferences) = BuildAudit(fragments);

            int missing     = entries.Count(e => e.Category == AuditCategory.Missing);
            int corrupted   = entries.Count(e => e.Category == AuditCategory.Corrupted);
            int overlapping = entries.Count(e => e.Category == AuditCategory.Overlapping);
            int emptyCount  = entries.Count(e => string.IsNullOrWhiteSpace(e.LineText));

            var rows = entries.Select(BuildAuditRow).ToList();

            // Build the unique-speaker list (sorted alphabetically) for the character filter dropdown.
            var speakers = entries
                .Select(e => e.SpeakerName ?? "(no speaker)")
                .Distinct()
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Build the unique property-name list for the property toggle filter.
            // Articy's per-property HasVoiceOver flag (set in template editor) is what determines
            // whether a property is scanned at all; this list is whatever survived that filter.
            var propertyNames = entries
                .Select(e => e.PropertyName ?? "")
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            // Build the unique path-prefix list. Each fragment's path produces one prefix per
            // depth level (e.g. "Episode 01" / "Episode 01 / Scene A" / ...), letting the user
            // filter at any level. Alphabetical sort naturally groups children under their parent.
            var pathPrefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var path = entry.FragmentPath;
                if (string.IsNullOrEmpty(path)) continue;
                var segments = path.Split(new[] { " / " }, StringSplitOptions.None);
                var prefix = "";
                for (int i = 0; i < segments.Length; i++)
                {
                    prefix = i == 0 ? segments[0] : prefix + " / " + segments[i];
                    pathPrefixes.Add(prefix);
                }
            }
            var sortedPaths = pathPrefixes.OrderBy(p => p, StringComparer.Ordinal).ToList();

            var window = new VoiceOverAuditWindow();
            var skipped = fragments.Count - totalScanned;
            var scopeSummary =
                $"{totalScanned} of {fragments.Count} DialogueFragment(s) scanned " +
                $"({skipped} skipped: no Speaker assigned)  |  " +
                $"{totalReferences} voice-over reference(s) checked  |  " +
                $"{entries.Count} issue(s) found";

            window.Populate(
                scopeSummary,
                rows,
                missing, corrupted, overlapping, emptyCount,
                speakers,
                propertyNames,
                sortedPaths,
                onNavigateFragment: idx => NavigateToObject(entries[idx].Fragment),
                onNavigateAsset:    idx => NavigateToObject(entries[idx].Asset));

            Session.ShowDialog(window);
        }

        private (List<AuditEntry> entries, int totalScanned, int totalReferences) BuildAudit(IReadOnlyList<ObjectProxy> fragments)
        {
            var entries = new List<AuditEntry>();
            int totalReferences = 0;
            int totalScanned = 0;

            var voLanguages = Session.GetVoiceOverLanguages();
            if (voLanguages == null || voLanguages.Count == 0) return (entries, 0, 0);

            // Pass 1: per-fragment, per-language scan for Missing + Corrupted.
            // Also build the asset-usage map for overlap detection in pass 2.
            // assetUsage tuple caches everything we'd need to render an Overlap entry
            // without re-walking the fragment.
            var assetUsage = new Dictionary<ulong, List<(ObjectProxy fragment, string culture, string srcPath, string speaker, string path, string line, string propName)>>();

            foreach (var fragment in fragments)
            {
                if (fragment == null || fragment.ObjectType != ObjectType.DialogueFragment) continue;

                // HARD FILTER: only audit fragments that have a Speaker assigned.
                // Speaker-less fragments (narration, stage directions, internal monologue) are
                // assumed not to need recorded VO and are excluded entirely from the audit.
                var speakerName = TryGetSpeakerName(fragment);
                if (speakerName == null) continue;

                totalScanned++;

                var localId = DecimalToHex(fragment.Id.ToString());
                var fragmentPath = TryGetFragmentPath(fragment);
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

                        var lineText = text.Texts[culture] ?? "";
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
                                Detail = "no VO asset assigned for this language",
                                SpeakerName = speakerName,
                                FragmentPath = fragmentPath,
                                LineText = lineText,
                                PropertyName = prop
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
                                Detail = corruptionReason,
                                SpeakerName = speakerName,
                                FragmentPath = fragmentPath,
                                LineText = lineText,
                                PropertyName = prop
                            });
                            continue;
                        }

                        // Healthy — track for overlap detection.
                        if (!assetUsage.TryGetValue(asset.Id, out var list))
                            assetUsage[asset.Id] = list = new List<(ObjectProxy, string, string, string, string, string, string)>();
                        list.Add((fragment, culture, srcPath, speakerName, fragmentPath, lineText, prop));
                    }
                }
            }

            // Pass 2: overlap detection. An asset shared by >1 distinct fragments is flagged.
            foreach (var kvp in assetUsage)
            {
                var refs = kvp.Value;
                var distinctFragmentIds = refs.Select(r => r.fragment.Id).Distinct().Count();
                if (distinctFragmentIds < 2) continue;

                foreach (var (fragment, culture, srcPath, speakerName, fragmentPath, lineText, propName) in refs)
                {
                    // Walk back to the fragment's ref to recover a valid asset proxy for navigation.
                    ObjectProxy asset = null;
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
                        Detail = $"shared with {distinctFragmentIds - 1} other fragment(s) — file: {fileName}",
                        SpeakerName = speakerName,
                        FragmentPath = fragmentPath,
                        LineText = lineText,
                        PropertyName = propName
                    });
                }
            }

            return (entries, totalScanned, totalReferences);
        }

        /// <summary>
        /// Returns the Speaker entity's display name for a DialogueFragment, or null if the fragment
        /// has no Speaker assigned (or the Speaker proxy is invalid).
        /// </summary>
        private static string TryGetSpeakerName(ObjectProxy fragment)
        {
            if (!fragment.HasProperty(ObjectPropertyNames.Speaker)) return null;
            var speaker = fragment[ObjectPropertyNames.Speaker] as ObjectProxy;
            if (speaker == null || !speaker.IsValid) return null;
            var name = speaker.GetDisplayName();
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Returns the navigator path of a fragment with the root segment trimmed
        /// (e.g. "Episode 01 / Scene A / Dialog B / Fragment 3").
        /// </summary>
        private static string TryGetFragmentPath(ObjectProxy fragment)
        {
            try
            {
                var raw = fragment.GetObjectPath();
                if (string.IsNullOrEmpty(raw)) return "";
                var segments = raw.Split(new[] { " / " }, StringSplitOptions.None);
                return segments.Length <= 1 ? raw : string.Join(" / ", segments[1..]);
            }
            catch
            {
                return "";
            }
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
                case AuditCategory.Missing:     icon = "⌀"; brush = IconWarnBrush;   categoryLabel = "Missing";    break;
                case AuditCategory.Corrupted:   icon = "✗"; brush = IconDangerBrush; categoryLabel = "Corrupted";  break;
                case AuditCategory.Overlapping: icon = "⇄"; brush = IconAccentBrush; categoryLabel = "Overlapping"; break;
                default:                        icon = "?"; brush = Brushes.Gray;    categoryLabel = "Unknown";    break;
            }

            return new VoiceOverAuditWindow.Row
            {
                Icon = icon,
                IconBrush = brush,
                CategoryLabel = categoryLabel,
                IdAndLang = $"{entry.LocalIdHex}  {entry.LanguageCode}",
                SpeakerName = entry.SpeakerName ?? "(no speaker)",
                FragmentPath = entry.FragmentPath ?? "",
                LineText = entry.LineText ?? "",
                Detail = entry.Detail ?? "",
                PropertyName = entry.PropertyName ?? "",
                PropertyLabel = string.IsNullOrEmpty(entry.PropertyName) ? "" : "." + entry.PropertyName,
                HasAsset = entry.Asset != null,
                CategoryKey = (int)entry.Category
            };
        }
    }
}
