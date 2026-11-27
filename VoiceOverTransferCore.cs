using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// Everything the Export / Import Voice-Overs window decides, with no articy types, so it can be
// tested outside articy (see Tests/). The plugin turns articy objects into VoRecords, asks this
// file for a plan, and carries out the plan with articy calls.
//
// Stored voice-over files are named like "0x0100000000001B4D_el(1C49).mp3": the fragment id, an
// underscore and the culture, then a tag of hex digits articy appends to every stored asset file.
// Every stored file seen so far has a 4-digit upper-case tag directly after the name; a tag is read
// as 4 or more hex digits with no space before the bracket, so a Windows duplicate such as
// "line (2).mp3" is never mistaken for one.
namespace Kurisu.VoiceOverTools.Core
{
    /// <summary>Where the speaker folder goes relative to the hierarchy folders.</summary>
    public enum SpeakerSplit
    {
        None,
        BeforeHierarchy,
        AfterHierarchy,
    }

    /// <summary>One voice-over binding: a fragment's text property in one language, and its asset.</summary>
    public sealed class VoRecord
    {
        public ulong AssetId;
        public ulong FragmentId;
        public string Culture;
        public string SourcePath;
        public string Speaker;
        public string FragmentPath;

        /// <summary>Display names from the picked object's child down to the fragment's parent.</summary>
        public IReadOnlyList<string> Hierarchy = Array.Empty<string>();

        public string StoredName => string.IsNullOrEmpty(SourcePath) ? null : Path.GetFileNameWithoutExtension(SourcePath);
        public string Extension => string.IsNullOrEmpty(SourcePath) ? "" : Path.GetExtension(SourcePath);
    }

    public sealed class TransferSettings
    {
        public string Folder;

        /// <summary>Culture whose "_culture" suffix is dropped on export and may be missing on import; null for none.</summary>
        public string MainCulture;
        public bool StripTag;
        public int HierarchyLevels;
        public SpeakerSplit Speaker;
        public bool FolderPerLanguage;
        public bool Overwrite;
    }

    public enum TransferStatus
    {
        Ready,
        Exists,
        Overwrite,
        Conflict,
        Missing,
        Unchanged,
        NoMatch,
        Done,
        Failed,
    }

    /// <summary>One row of either preview. Choices is only used by import rows.</summary>
    public sealed class TransferRow
    {
        public TransferStatus Status { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public bool Selected { get; set; }
        public bool Selectable { get; set; }
        public List<string> Choices { get; set; }
        public string Choice { get; set; }
        public bool HasChoices => Choices != null && Choices.Count > 0;

        public string StatusText => Status switch
        {
            TransferStatus.Ready => "Ready",
            TransferStatus.Exists => "Exists, skip",
            TransferStatus.Overwrite => "Overwrite",
            TransferStatus.Conflict => "Conflict",
            TransferStatus.Missing => "Missing",
            TransferStatus.Unchanged => "Unchanged",
            TransferStatus.NoMatch => "No match",
            TransferStatus.Done => "Done",
            _ => "Failed",
        };

        // Export
        public string SourcePath;
        public string TargetPath;

        // Import
        public VoRecord Record;
        public int SharedBy;

        // Language matching: the fragment slot to bind, and the asset id behind each choice.
        public LanguageSlot Slot;
        public Dictionary<string, ulong> ChoiceAssets;
    }

    /// <summary>One voice-over slot: a fragment's voice-over text property, with what it already holds per language.</summary>
    public sealed class LanguageSlot
    {
        public ulong FragmentId;
        public string Property;
        public string FragmentPath;

        /// <summary>Stored file name (without extension) of the voice-over bound in each language.</summary>
        public Dictionary<string, string> Existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Asset ids bound in each language.</summary>
        public Dictionary<string, ulong> ExistingAssets = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Extension of the voice-over file bound in each language, with the dot.</summary>
        public Dictionary<string, string> ExistingExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The asset's name as articy shows it, per language. Shown for checking only, never matched on.</summary>
        public Dictionary<string, string> ExistingDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An audio asset in the picked asset folder, offered for the target language.</summary>
    public sealed class CandidateAsset
    {
        public ulong AssetId;
        public string StoredName;
        public string Extension;
        public string Location;

        /// <summary>The asset's name as articy shows it. Shown for checking only, never matched on.</summary>
        public string DisplayName;
    }

    public static class VoiceOverTransferCore
    {
        public const string SkipChoice = "(skip)";

        /// <summary>Longest full path written without a warning. Windows' classic limit is 260 including the terminator.</summary>
        public const int MaxPathLength = 259;

        public static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg", ".flac", ".aif", ".aiff", ".m4a" };

        // A tag needs a non-space character before it, so "line (2)" is not split and a name that is
        // nothing but a tag keeps it rather than leaving an empty name.
        private static readonly Regex sTagPattern = new Regex(@"(?<=\S)\(([0-9A-Fa-f]{4,})\)$", RegexOptions.Compiled);

        private static readonly HashSet<string> sReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        #region Names

        /// <summary>Splits "name(1C49)" into ("name", "1C49"); the tag is null when there is none.</summary>
        public static (string Base, string Tag) SplitTag(string aName)
        {
            aName ??= "";
            var match = sTagPattern.Match(aName);
            return match.Success ? (aName.Substring(0, match.Index), match.Groups[1].Value) : (aName, null);
        }

        /// <summary>Drops a trailing "_culture" (case-insensitive); returns null when the name has none.</summary>
        public static string StripCulture(string aBase, string aCulture)
        {
            if (string.IsNullOrEmpty(aCulture) || string.IsNullOrEmpty(aBase)) return null;
            var suffix = "_" + aCulture;
            if (!aBase.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || aBase.Length == suffix.Length) return null;
            return aBase.Substring(0, aBase.Length - suffix.Length);
        }

        private static bool IsMain(VoRecord aRecord, TransferSettings aSettings)
        {
            return aSettings.MainCulture != null && string.Equals(aRecord.Culture, aSettings.MainCulture, StringComparison.OrdinalIgnoreCase);
        }

        public static string ExportName(VoRecord aRecord, TransferSettings aSettings)
        {
            var (baseName, tag) = SplitTag(aRecord.StoredName);
            if (IsMain(aRecord, aSettings))
                baseName = StripCulture(baseName, aRecord.Culture) ?? baseName;
            var name = aSettings.StripTag || tag == null ? baseName : $"{baseName}({tag})";
            return name + aRecord.Extension;
        }

        public static string ExportFolder(VoRecord aRecord, TransferSettings aSettings)
        {
            var parts = new List<string>();
            if (aSettings.FolderPerLanguage)
                parts.Add(SafeFolderName(aRecord.Culture));

            var speaker = SafeFolderName(string.IsNullOrWhiteSpace(aRecord.Speaker) ? "(no speaker)" : aRecord.Speaker);
            if (aSettings.Speaker == SpeakerSplit.BeforeHierarchy)
                parts.Add(speaker);
            parts.AddRange((aRecord.Hierarchy ?? Array.Empty<string>()).Take(Math.Max(0, aSettings.HierarchyLevels)).Select(SafeFolderName));
            if (aSettings.Speaker == SpeakerSplit.AfterHierarchy)
                parts.Add(speaker);

            return parts.Count == 0 ? aSettings.Folder : Path.Combine(new[] { aSettings.Folder }.Concat(parts).ToArray());
        }

        /// <summary>
        /// Makes a display name usable as one Windows folder name: invalid characters become "_",
        /// trailing dots and spaces go (Windows drops them silently), reserved device names such as
        /// "CON" get a trailing "_", and an empty result becomes "_".
        /// </summary>
        public static string SafeFolderName(string aName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((aName ?? "").Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            if (string.IsNullOrEmpty(cleaned))
                return "_";
            // Windows reads any name whose part before the first dot is a device name as that device,
            // whatever follows ("aux.wav_" is still AUX), so the mark goes on the device part itself.
            var dot = cleaned.IndexOf('.');
            var stem = (dot < 0 ? cleaned : cleaned.Substring(0, dot)).TrimEnd(' ');
            return sReservedNames.Contains(stem) ? cleaned.Insert(stem.Length, "_") : cleaned;
        }

        #endregion

        #region Export

        public static List<TransferRow> PlanExport(IEnumerable<VoRecord> aRecords, TransferSettings aSettings, Func<string, bool> aFileExists = null)
        {
            aFileExists ??= File.Exists;
            var rows = new List<TransferRow>();
            var byTarget = new Dictionary<string, List<VoRecord>>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in aRecords)
            {
                if (string.IsNullOrEmpty(record.SourcePath) || !aFileExists(record.SourcePath))
                {
                    rows.Add(new TransferRow
                    {
                        Status = TransferStatus.Missing,
                        Title = $"{record.FragmentPath} [{record.Culture}]",
                        Detail = "The stored file is missing on disk: " + (string.IsNullOrEmpty(record.SourcePath) ? "(no path)" : record.SourcePath),
                    });
                    continue;
                }

                var target = Path.Combine(ExportFolder(record, aSettings), ExportName(record, aSettings));
                if (!byTarget.TryGetValue(target, out var list))
                    byTarget[target] = list = new List<VoRecord>();
                list.Add(record);
            }

            foreach (var pair in byTarget.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var target = pair.Key;
                var first = pair.Value[0];
                var sources = pair.Value.Select(r => r.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var relative = Path.GetRelativePath(aSettings.Folder, target);

                if (sources.Count > 1)
                {
                    rows.Add(new TransferRow
                    {
                        Status = TransferStatus.Conflict,
                        Title = relative,
                        Detail = $"{sources.Count} different files would be written here, so none is: " + string.Join(", ", sources.Select(Path.GetFileName)),
                    });
                    continue;
                }

                if (target.Length > MaxPathLength)
                {
                    rows.Add(new TransferRow
                    {
                        Status = TransferStatus.Conflict,
                        Title = relative,
                        Detail = $"The full path is {target.Length} characters, over Windows' {MaxPathLength + 1}-character limit: use fewer hierarchy levels or a shorter folder.",
                        SourcePath = first.SourcePath,
                        TargetPath = target,
                    });
                    continue;
                }

                var fragments = pair.Value.Select(r => r.FragmentId).Distinct().Count();
                var exists = aFileExists(target);
                rows.Add(new TransferRow
                {
                    Status = exists ? (aSettings.Overwrite ? TransferStatus.Overwrite : TransferStatus.Exists) : TransferStatus.Ready,
                    Title = relative,
                    Detail = $"From {Path.GetFileName(first.SourcePath)} · {first.Speaker ?? "(no speaker)"} · {first.FragmentPath}"
                        + (fragments > 1 ? $" · shared by {fragments} fragments" : ""),
                    Selectable = !exists || aSettings.Overwrite,
                    Selected = !exists || aSettings.Overwrite,
                    SourcePath = first.SourcePath,
                    TargetPath = target,
                });
            }

            return rows;
        }

        /// <summary>Copies every ticked row. A failing row is marked and the rest carry on.</summary>
        public static (int Written, int Failed) RunExport(IEnumerable<TransferRow> aRows)
        {
            int written = 0, failed = 0;
            foreach (var row in aRows.Where(r => r.Selectable && r.Selected && r.SourcePath != null && r.TargetPath != null).ToList())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(row.TargetPath));
                    File.Copy(row.SourcePath, row.TargetPath, true);
                    row.Status = TransferStatus.Done;
                    row.Selectable = row.Selected = false;
                    written++;
                }
                catch (Exception ex)
                {
                    row.Status = TransferStatus.Failed;
                    row.Detail = ex.Message;
                    row.Selected = false;
                    failed++;
                }
            }
            return (written, failed);
        }

        #endregion

        #region Import

        /// <summary>
        /// Matches files to voice-over assets by name. A file whose name keeps a tag matches the stored
        /// name exactly; one without a tag matches the stored name minus its tag; for the main culture
        /// the "_culture" suffix may also be missing. A file whose tag differs from the stored one (for
        /// example exported before the asset was last replaced) is offered as a conflict, never applied
        /// on its own. Every ambiguity becomes a conflict row whose choice starts on "(skip)".
        /// </summary>
        public static List<TransferRow> PlanImport(IEnumerable<VoRecord> aRecords, TransferSettings aSettings, IEnumerable<string> aFiles,
            Func<string, string, bool> aSameContent = null, Func<string, long> aFileLength = null)
        {
            aSameContent ??= (_, _) => false;
            aFileLength ??= f => new FileInfo(f).Length;

            var records = aRecords.Where(r => r.StoredName != null).ToList();
            var assets = records.GroupBy(r => r.AssetId).Select(g => g.First()).ToList();
            var sharedBy = records.GroupBy(r => r.AssetId).ToDictionary(g => g.Key, g => g.Select(r => r.FragmentId).Distinct().Count());

            // Every name each asset answers to: with its own tag (exact) and without any tag (loose).
            var exactKeys = new Dictionary<string, List<VoRecord>>(StringComparer.OrdinalIgnoreCase);
            var looseKeys = new Dictionary<string, List<VoRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets)
            {
                var (baseName, tag) = SplitTag(asset.StoredName);
                var names = new List<string> { baseName };
                if (IsMain(asset, aSettings))
                {
                    var stripped = StripCulture(baseName, asset.Culture);
                    if (stripped != null) names.Add(stripped);
                }

                foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    AddKey(looseKeys, name, asset);
                    if (tag != null) AddKey(exactKeys, $"{name}({tag})", asset);
                }
            }

            var rows = new List<TransferRow>();
            var fileToAssets = new Dictionary<string, List<VoRecord>>(StringComparer.OrdinalIgnoreCase);
            var staleTagFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in aFiles.Where(f => AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var (baseName, tag) = SplitTag(name);

                List<VoRecord> matches = null;
                if (tag != null && exactKeys.TryGetValue(name, out var exact))
                    matches = exact;
                else if (tag != null && looseKeys.TryGetValue(baseName, out var stale))
                {
                    matches = stale;
                    staleTagFiles.Add(file);
                }
                else if (tag == null && looseKeys.TryGetValue(name, out var loose))
                    matches = loose;

                if (matches == null || matches.Count == 0)
                {
                    rows.Add(new TransferRow
                    {
                        Status = TransferStatus.NoMatch,
                        Title = Path.GetRelativePath(aSettings.Folder, file),
                        Detail = "No voice-over under the picked object has this name.",
                    });
                    continue;
                }
                fileToAssets[file] = matches.GroupBy(m => m.AssetId).Select(g => g.First()).ToList();
            }

            var assetToFiles = new Dictionary<ulong, List<string>>();
            foreach (var pair in fileToAssets)
                foreach (var record in pair.Value)
                {
                    if (!assetToFiles.TryGetValue(record.AssetId, out var list))
                        assetToFiles[record.AssetId] = list = new List<string>();
                    list.Add(pair.Key);
                }

            var assetRows = new List<TransferRow>();
            foreach (var asset in assets.Where(a => assetToFiles.ContainsKey(a.AssetId)).OrderBy(a => a.StoredName, StringComparer.OrdinalIgnoreCase))
            {
                var candidates = assetToFiles[asset.AssetId];
                var notes = new List<string>();

                // Several byte-identical files are one file in several places (a shared voice-over exported
                // into each of its branches): not an ambiguity, so keep the first and mention the copies.
                if (candidates.Count > 1 && candidates.Skip(1).All(f => aSameContent(candidates[0], f))
                    && candidates.Select(f => Path.GetExtension(f).ToLowerInvariant()).Distinct().Count() == 1)
                {
                    notes.Add($"{candidates.Count} identical copies found, using {Path.GetRelativePath(aSettings.Folder, candidates[0])}");
                    candidates = new List<string> { candidates[0] };
                }

                var relative = candidates.Select(f => Path.GetRelativePath(aSettings.Folder, f)).ToList();
                var shared = sharedBy[asset.AssetId];

                var row = new TransferRow
                {
                    Title = asset.StoredName + asset.Extension,
                    Record = asset,
                    SharedBy = shared,
                };

                var problems = new List<string>();
                if (candidates.Count > 1)
                    problems.Add($"{candidates.Count} files match this voice-over");
                foreach (var file in candidates.Where(f => fileToAssets[f].Count > 1))
                    problems.Add($"{Path.GetFileName(file)} also matches " +
                        string.Join(", ", fileToAssets[file].Where(r => r.AssetId != asset.AssetId).Select(r => r.StoredName + r.Extension)));
                foreach (var file in candidates.Where(staleTagFiles.Contains))
                    problems.Add($"{Path.GetFileName(file)} carries a different tag than the stored {asset.StoredName}{asset.Extension}");
                foreach (var file in candidates.Where(f => !string.Equals(Path.GetExtension(f), asset.Extension, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{Path.GetFileName(file)} is {Path.GetExtension(file)}, the stored file is {asset.Extension}");
                foreach (var file in candidates.Where(f => SafeLength(aFileLength, f) == 0))
                    problems.Add($"{Path.GetFileName(file)} is empty (0 bytes)");

                if (problems.Count > 0)
                {
                    row.Status = TransferStatus.Conflict;
                    notes.AddRange(problems);
                    notes.Add("pick a file to replace it, or leave it skipped");
                    row.Choices = new List<string> { SkipChoice }.Concat(relative).ToList();
                    row.Choice = SkipChoice;
                    // The dropdown is the decision for a conflict; the checkbox stays off.
                    row.Selectable = false;
                    row.Selected = false;
                }
                else if (aSameContent(candidates[0], asset.SourcePath))
                {
                    row.Status = TransferStatus.Unchanged;
                    notes.Add($"{relative[0]} is identical to the stored file");
                }
                else
                {
                    row.Status = TransferStatus.Ready;
                    notes.Add("replace with " + relative[0]);
                    row.Choices = new List<string> { relative[0] };
                    row.Choice = relative[0];
                    row.Selectable = true;
                    row.Selected = true;
                }

                if (shared > 1)
                    notes.Add($"shared by {shared} fragments: replacing it changes all of them");

                row.Detail = string.Join(" · ", notes);
                assetRows.Add(row);
            }

            assetRows.AddRange(rows);
            return assetRows;
        }

        /// <summary>
        /// Rows the import would apply: a clean match that is ticked, or a conflict where a file has been
        /// picked in its dropdown. A conflict left on "(skip)" is never applied.
        /// </summary>
        public static IEnumerable<TransferRow> RowsToApply(IEnumerable<TransferRow> aRows)
        {
            return aRows.Where(r => r.Record != null && r.Choice != null && r.Choice != SkipChoice
                && (r.Status == TransferStatus.Conflict || (r.Status == TransferStatus.Ready && r.Selectable && r.Selected)));
        }

        /// <summary>The name the replacement is staged under: the stored name without its tag, with the new file's extension.</summary>
        public static string StagedFileName(VoRecord aRecord, string aChosenFile)
        {
            return SplitTag(aRecord.StoredName).Base + Path.GetExtension(aChosenFile);
        }

        private static void AddKey(Dictionary<string, List<VoRecord>> aKeys, string aKey, VoRecord aRecord)
        {
            if (!aKeys.TryGetValue(aKey, out var list))
                aKeys[aKey] = list = new List<VoRecord>();
            if (!list.Any(r => r.AssetId == aRecord.AssetId))
                list.Add(aRecord);
        }

        private static long SafeLength(Func<string, long> aLength, string aFile)
        {
            try { return aLength(aFile); } catch { return -1; }
        }

        #endregion

        #region Language matching

        public const string KeepChoice = "(keep current)";

        /// <summary>
        /// The name a voice-over answers to across languages: the tag is dropped, then a trailing
        /// "_culture" for one of the project's cultures (or its two-letter language, so "_de" counts for
        /// "de-DE"). Returns the culture the suffix named, or null when the name has none. Only known
        /// cultures are stripped, so "_SC" or "_final" stay part of the name.
        /// </summary>
        public static (string Base, string Culture) LanguageBase(string aStoredName, IReadOnlyCollection<string> aCultures)
        {
            var (baseName, _) = SplitTag(aStoredName);
            foreach (var (suffix, culture) in CultureSuffixes(aCultures))
            {
                var stripped = StripCulture(baseName, suffix);
                if (stripped != null)
                    return (stripped, culture);
            }
            return (baseName, null);
        }

        /// <summary>
        /// Every suffix that names a culture, longest first so "el-GR" wins over "el". A two-letter
        /// language stands for a regional culture only when exactly one culture has that language.
        /// </summary>
        private static List<(string Suffix, string Culture)> CultureSuffixes(IReadOnlyCollection<string> aCultures)
        {
            var result = aCultures.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).Select(c => (c, c)).ToList();
            foreach (var group in aCultures.Where(c => !string.IsNullOrEmpty(c) && c.Contains('-')).GroupBy(c => c.Split('-')[0], StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() == 1 && !result.Any(r => string.Equals(r.Item1, group.Key, StringComparison.OrdinalIgnoreCase)))
                    result.Add((group.Key, group.First()));
            }
            return result.OrderByDescending(r => r.Item1.Length).ToList();
        }

        /// <summary>
        /// Matches the assets in the picked folder to voice-over slots for the target culture. A slot
        /// answers to the base names of the voice-overs it already holds in other languages. Among the
        /// assets with that base, one named for the target culture wins, then one with no language
        /// suffix; one named for another language is never used. A tie is a conflict. A slot that
        /// already holds a target voice-over is never changed silently: the same asset is "unchanged",
        /// a different one is a conflict whose choice starts on "keep current".
        /// </summary>
        public static List<TransferRow> PlanLanguageMatch(IEnumerable<LanguageSlot> aSlots, IEnumerable<CandidateAsset> aCandidates,
            string aTargetCulture, IReadOnlyCollection<string> aCultures)
        {
            var cultures = aCultures.Concat(new[] { aTargetCulture }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var candidates = aCandidates.Where(c => !string.IsNullOrEmpty(c.StoredName)).ToList();

            var byBase = new Dictionary<string, List<(CandidateAsset Asset, string Culture)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                var (baseName, culture) = LanguageBase(candidate.StoredName, cultures);
                if (!byBase.TryGetValue(baseName, out var list))
                    byBase[baseName] = list = new List<(CandidateAsset, string)>();
                list.Add((candidate, culture));
            }

            var rows = new List<TransferRow>();
            var used = new HashSet<ulong>();
            var wrongLanguage = new HashSet<ulong>();
            var outranked = new HashSet<ulong>();

            foreach (var slot in aSlots.OrderBy(s => s.FragmentPath, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Property))
            {
                var bases = slot.Existing
                    .Where(e => !string.Equals(e.Key, aTargetCulture, StringComparison.OrdinalIgnoreCase))
                    .Select(e => LanguageBase(e.Value, cultures).Base)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (bases.Count == 0)
                    continue;

                var found = bases.Where(byBase.ContainsKey).SelectMany(b => byBase[b]).GroupBy(f => f.Asset.AssetId).Select(g => g.First()).ToList();
                if (found.Count == 0)
                    continue;

                var named = found.Where(f => string.Equals(f.Culture, aTargetCulture, StringComparison.OrdinalIgnoreCase)).ToList();
                var plain = found.Where(f => f.Culture == null).ToList();
                foreach (var other in found.Where(f => f.Culture != null && !string.Equals(f.Culture, aTargetCulture, StringComparison.OrdinalIgnoreCase)))
                    wrongLanguage.Add(other.Asset.AssetId);

                var best = named.Count > 0 ? named : plain;
                if (best.Count == 0)
                    continue;
                if (named.Count > 0)
                    foreach (var p in plain)
                        outranked.Add(p.Asset.AssetId);
                foreach (var b in best)
                    used.Add(b.Asset.AssetId);

                slot.ExistingAssets.TryGetValue(aTargetCulture, out var currentId);
                var row = new TransferRow
                {
                    Title = $"{slot.FragmentPath} · {slot.Property}",
                    Slot = slot,
                    ChoiceAssets = ChoiceLabels(best.Select(b => b.Asset)),
                };
                var summary = new List<string>();
                if (bases.Count > 1)
                    summary.Add("its voice-overs in other languages have different names: " + string.Join(", ", bases));

                if (best.Count > 1)
                {
                    row.Status = TransferStatus.Conflict;
                    summary.Insert(0, $"{best.Count} assets fit equally well: pick one, or leave it");
                    row.Choices = new List<string> { currentId != 0 ? KeepChoice : SkipChoice }.Concat(row.ChoiceAssets.Keys).ToList();
                    row.Choice = row.Choices[0];
                }
                else if (currentId == best[0].Asset.AssetId)
                {
                    row.Status = TransferStatus.Unchanged;
                    summary.Insert(0, "already bound to this asset");
                }
                else if (currentId != 0)
                {
                    row.Status = TransferStatus.Conflict;
                    summary.Insert(0, "already has a different voice-over in this language: pick the new one to replace it, or keep the current one");
                    row.Choices = new List<string> { KeepChoice }.Concat(row.ChoiceAssets.Keys).ToList();
                    row.Choice = KeepChoice;
                }
                else
                {
                    row.Status = TransferStatus.Ready;
                    summary.Insert(0, "bind the new file below");
                    row.Choices = row.ChoiceAssets.Keys.ToList();
                    row.Choice = row.Choices[0];
                    row.Selectable = row.Selected = true;
                }

                // What matched, and both names of everything involved, so the pair can be checked by eye.
                var lines = new List<string> { string.Join(" · ", summary) };
                var matchedBases = best.Select(b => LanguageBase(b.Asset.StoredName, cultures).Base).Distinct(StringComparer.OrdinalIgnoreCase);
                lines.Add("Matched on name: " + string.Join(", ", matchedBases.Select(Quote)) + "  (tags and language suffixes ignored)");
                foreach (var existing in slot.Existing.Where(e => !string.Equals(e.Key, aTargetCulture, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Key))
                    lines.Add($"Existing [{existing.Key}]   file {FileOf(slot, existing.Key)}   ·   articy name {Quoted(slot.ExistingDisplay, existing.Key)}");
                if (currentId != 0)
                    lines.Add($"Current [{aTargetCulture}]   file {FileOf(slot, aTargetCulture)}   ·   articy name {Quoted(slot.ExistingDisplay, aTargetCulture)}");
                foreach (var b in best)
                    lines.Add($"New [{aTargetCulture}]   file {b.Asset.StoredName}{b.Asset.Extension}   ·   articy name {Quote(b.Asset.DisplayName)}   ·   in {Where(b.Asset)}   ({(b.Culture == null ? "no language suffix" : "named for " + aTargetCulture)})");

                row.Detail = string.Join("\n", lines);
                rows.Add(row);
            }

            // Several slots taking the same asset is normal for one line used in several places; say so.
            foreach (var group in rows.Where(r => r.Status == TransferStatus.Ready).GroupBy(r => r.ChoiceAssets[r.Choice]).Where(g => g.Count() > 1))
                foreach (var row in group)
                    row.Detail += $"\nThe same asset goes to {group.Count()} slots (one line used in several places)";

            foreach (var candidate in candidates.Where(c => !used.Contains(c.AssetId)).OrderBy(c => c.StoredName, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new TransferRow
                {
                    Status = TransferStatus.NoMatch,
                    Title = $"{candidate.StoredName}{candidate.Extension}   ·   articy name {Quote(candidate.DisplayName)}   ·   in {Where(candidate)}",
                    Detail = outranked.Contains(candidate.AssetId)
                        ? "an asset named for this language was used instead"
                        : wrongLanguage.Contains(candidate.AssetId)
                            ? "its name carries another language's suffix, so it is not used for this language"
                            : "no voice-over slot has this name in another language",
                });
            }

            return rows;
        }

        /// <summary>Rows the binding would apply: a ticked clean match, or a conflict with an asset picked.</summary>
        public static IEnumerable<TransferRow> BindingsToApply(IEnumerable<TransferRow> aRows)
        {
            return aRows.Where(r => r.Slot != null && r.Choice != null && r.ChoiceAssets != null && r.ChoiceAssets.ContainsKey(r.Choice)
                && (r.Status == TransferStatus.Conflict || (r.Status == TransferStatus.Ready && r.Selectable && r.Selected)));
        }

        /// <summary>Choice label for each asset; two assets with the same name and place get their id appended.</summary>
        private static Dictionary<string, ulong> ChoiceLabels(IEnumerable<CandidateAsset> aAssets)
        {
            var list = aAssets.ToList();
            var result = new Dictionary<string, ulong>();
            foreach (var asset in list)
            {
                var label = Describe(asset);
                if (list.Count(a => Describe(a) == label) > 1)
                    label += $" [{asset.AssetId:X}]";
                result[label] = asset.AssetId;
            }
            return result;
        }

        /// <summary>Dropdown label: the file name first (what is matched), then articy's name and the folder.</summary>
        private static string Describe(CandidateAsset aAsset)
        {
            return $"{aAsset.StoredName}{aAsset.Extension} · {Quote(aAsset.DisplayName)} · {Where(aAsset)}";
        }

        private static string Where(CandidateAsset aAsset) => string.IsNullOrEmpty(aAsset.Location) ? "(asset root)" : aAsset.Location;

        private static string Quote(string aName) => string.IsNullOrWhiteSpace(aName) ? "(none)" : "\"" + aName + "\"";

        private static string Quoted(Dictionary<string, string> aNames, string aCulture) => Quote(aNames.TryGetValue(aCulture, out var name) ? name : null);

        private static string FileOf(LanguageSlot aSlot, string aCulture)
        {
            aSlot.Existing.TryGetValue(aCulture, out var name);
            aSlot.ExistingExtension.TryGetValue(aCulture, out var extension);
            return (name ?? "(none)") + (extension ?? "");
        }

        #endregion
    }
}
