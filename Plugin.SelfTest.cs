using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Articy.Api;
using Articy.Api.Plugins;
using Articy.ModelFramework;
using Kurisu.VoiceOverTools.Core;

namespace Kurisu.VoiceOverTools
{
    // Self-test for Export / Import Voice-Overs, run inside articy on a COPY of a project. The naming
    // and matching rules are covered outside articy (Tests/); this checks what only articy can show:
    // that exports reproduce the stored files, that a plan of the export comes back unchanged, and
    // what ChangeAsset really does to an asset (its file, name, tag, metadata and bindings).
    public partial class Plugin
    {
        private void RunVoiceOverSelfTest(MacroCommandDescriptor aDescriptor, List<ObjectProxy> aSelectedObjects)
        {
            var root = aSelectedObjects?.FirstOrDefault(o => o != null && o.IsValid) ?? Session.GetSystemFolder(SystemFolderNames.Flow);
            var project = Session.GetProjectFolderPath();

            var answer = MessageBox.Show(
                $"This self-test REPLACES one voice-over file in this project and then puts it back. articy writes the replacement to disk at once, so closing without saving does not undo it.\n\nProject: {project}\nUnder: {DescribeObject(root)}\n\nRun it only on a copy of a project. Continue?",
                "Voice-Over Self-Test", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK)
                return;

            var report = new StringBuilder();
            int passed = 0, failed = 0;
            void Check(bool aOk, string aWhat)
            {
                report.AppendLine((aOk ? "PASS  " : "FAIL  ") + aWhat);
                if (aOk) passed++; else failed++;
            }
            void Note(string aText) => report.AppendLine("      " + aText);

            var work = Path.Combine(Path.GetTempPath(), "Kurisu.VoiceOverSelfTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(work);
            report.AppendLine($"Voice-Over self-test, {DateTime.Now:u}");
            // The code that actually ran. articy keeps an already-loaded plugin running after an update is
            // installed, until it restarts (seen 2026-11-27: a 1.1.4 run reported as if 1.1.5 were installed).
            report.AppendLine($"Plugin code: {typeof(Plugin).Assembly.GetName().Version} (installed manifest: {Manifest?.Version})");
            report.AppendLine($"Project: {project}");
            report.AppendLine($"Under: {DescribeObject(root)}");
            report.AppendLine($"Work folder: {work}");
            report.AppendLine();

            try
            {
                var cultures = (Session.GetVoiceOverLanguages() ?? new List<Articy.Api.Data.ApiLanguageInfo>()).Select(l => l.CultureName).ToList();
                var main = Session.GetPrimaryLanguage()?.CultureName;
                var records = CollectVoiceOvers(root, cultures);
                Note($"{records.Count} voice-over bindings, {records.Select(r => r.AssetId).Distinct().Count()} assets, languages {string.Join(", ", cultures)}, main {main}");
                if (records.Count == 0)
                {
                    // Nothing to test is not a failure: a project without voice-overs has no import to break.
                    report.AppendLine(cultures.Count == 0
                        ? "SKIP  this project has no voice-over languages, so there is nothing to test"
                        : "SKIP  no voice-overs are bound under the picked object, so there is nothing to test");
                    return;
                }
                Check(true, "found voice-overs to test with");

                // 1. Export, deepest split, and compare every written file to its source.
                var exportFolder = Path.Combine(work, "export");
                var settings = new TransferSettings { Folder = exportFolder, MainCulture = main, HierarchyLevels = 6, Speaker = SpeakerSplit.AfterHierarchy, FolderPerLanguage = true };
                var exportRows = VoiceOverTransferCore.PlanExport(records, settings);
                var (written, exportFailed) = VoiceOverTransferCore.RunExport(exportRows);
                Note($"export: {written} written, {exportFailed} failed, " + string.Join(", ", exportRows.GroupBy(r => r.StatusText).Select(g => $"{g.Count()} {g.Key}")));
                Check(exportFailed == 0, "export: no write failures");
                var mismatched = exportRows.Where(r => r.Status == TransferStatus.Done && !SameContent(r.TargetPath, r.SourcePath)).ToList();
                Check(mismatched.Count == 0, $"export: every written file is byte-identical to its stored file ({mismatched.Count} differ)");
                foreach (var row in exportRows.Where(r => r.Status is TransferStatus.Missing or TransferStatus.Conflict).Take(10))
                    Note($"{row.StatusText}: {row.Title}: {row.Detail}");

                // 2. Plan the import of that export: everything must come back unchanged.
                var importSettings = new TransferSettings { Folder = exportFolder, MainCulture = main };
                var importRows = VoiceOverTransferCore.PlanImport(records, importSettings, Directory.EnumerateFiles(exportFolder, "*", SearchOption.AllDirectories), SameContent);
                var notUnchanged = importRows.Where(r => r.Status != TransferStatus.Unchanged).ToList();
                Check(notUnchanged.Count == 0, $"round trip: the export imports back unchanged ({notUnchanged.Count} rows did not)");
                foreach (var row in notUnchanged.Take(10))
                    Note($"{row.StatusText}: {row.Title}: {row.Detail}");

                // 3. Replace one unshared voice-over with another voice-over's real file, then restore it.
                var sharedCount = records.GroupBy(r => r.AssetId).ToDictionary(g => g.Key, g => g.Select(r => r.FragmentId).Distinct().Count());

                // Guard: the target's audio must not also be another LINE's audio. That is what a swap that
                // was never undone looks like, and "original" would then be the wrong recording. The same
                // audio in two languages of one line is legitimate (a project can carry a language as a
                // copy, as one test project does for all 278 of its lines), so only sharing across
                // different fragments counts.
                var hashes = records.Where(r => r.SourcePath != null && File.Exists(r.SourcePath)).GroupBy(r => r.AssetId).Select(g => g.First())
                    .ToDictionary(r => r.AssetId, r => FileHash(r.SourcePath));
                var fragmentsByHash = records.Where(r => hashes.ContainsKey(r.AssetId)).GroupBy(r => hashes[r.AssetId])
                    .ToDictionary(g => g.Key, g => g.Select(r => r.FragmentId).Distinct().Count());
                var duplicated = new HashSet<string>(fragmentsByHash.Where(p => p.Value > 1).Select(p => p.Key));
                if (duplicated.Count > 0)
                    Note($"{hashes.Values.Count(duplicated.Contains)} voice-overs share their audio with a different line; none of them is used for the swap");
                var target = records.FirstOrDefault(r => sharedCount[r.AssetId] == 1 && hashes.TryGetValue(r.AssetId, out var h) && !duplicated.Contains(h));
                var donor = records.FirstOrDefault(r => target != null && r.AssetId != target.AssetId && r.SourcePath != null && File.Exists(r.SourcePath)
                    && string.Equals(r.Extension, target.Extension, StringComparison.OrdinalIgnoreCase) && !SameContent(r.SourcePath, target.SourcePath));
                Check(target != null && donor != null, "found an unshared voice-over and a different file of the same format to swap in");
                if (target == null || donor == null) return;

                var asset = Session.GetObjectById(target.AssetId);
                var original = Path.Combine(work, "original" + target.Extension);
                File.Copy(target.SourcePath, original);
                var before = Snapshot(asset);
                var boundBefore = BoundFragments(target);
                Note($"swapping {Path.GetFileName(target.SourcePath)} for the content of {Path.GetFileName(donor.SourcePath)}");

                var swapFolder = Path.Combine(work, "swap");
                Directory.CreateDirectory(swapFolder);
                File.Copy(donor.SourcePath, Path.Combine(swapFolder, VoiceOverTransferCore.SplitTag(target.StoredName).Base + target.Extension));
                var swapRows = VoiceOverTransferCore.PlanImport(records, new TransferSettings { Folder = swapFolder }, Directory.EnumerateFiles(swapFolder), SameContent);
                var swapRow = swapRows.SingleOrDefault(r => r.Record?.AssetId == target.AssetId);
                Check(swapRow?.Status == TransferStatus.Ready, $"swap: the planted file plans as a clean replace (got {swapRow?.StatusText ?? "no row"})");
                if (swapRow?.Status != TransferStatus.Ready) return;

                // The swap goes through the real import path. Since 1.1.7 the import dates every staged file
                // now, because articy skips a replacement whose name and date match its last import for that
                // asset, and waits for articy's background copy (both measured 2026-11-27).
                var (replaced, replaceFailed) = RunImport(new TransferSettings { Folder = swapFolder }, swapRows);
                Check(replaced == 1 && replaceFailed == 0, $"swap: the import replaced the file ({replaced} replaced, {replaceFailed} failed{(replaceFailed > 0 ? ": " + swapRow.Detail : "")})");
                if (replaced != 1)
                    DiagnoseSkippedReplacement(asset, Path.Combine(swapFolder, swapRow.Choice), donor.SourcePath, Path.Combine(work, "swap_diagnosis"), Check, Note, swapRow);

                var after = Snapshot(asset);
                Note($"stored file before: {before.FileName}");
                Note($"stored file after:  {after.FileName}");
                Check(after.Path != null && SameContent(after.Path, donor.SourcePath), "swap: the stored file now has the new content");
                Check(after.DisplayName == before.DisplayName, $"swap: display name kept ({before.DisplayName} → {after.DisplayName})");
                Check(after.Text == before.Text, "swap: description kept");
                Check(after.ExternalId == before.ExternalId, "swap: external id kept");
                Check(BoundFragments(target).SetEquals(boundBefore), "swap: the same fragments still point to this asset");
                var (baseBefore, tagBefore) = VoiceOverTransferCore.SplitTag(Path.GetFileNameWithoutExtension(before.FileName));
                var (baseAfter, tagAfter) = VoiceOverTransferCore.SplitTag(Path.GetFileNameWithoutExtension(after.FileName));
                Check(string.Equals(baseBefore, baseAfter, StringComparison.OrdinalIgnoreCase), $"swap: the stored name keeps its base ({baseBefore} → {baseAfter})");
                Note(tagBefore == tagAfter ? $"FINDING: ChangeAsset keeps the tag ({tagBefore})" : $"FINDING: ChangeAsset renews the tag ({tagBefore} → {tagAfter})");

                // Restore the original, again through the import: a second replacement of the same voice-over
                // with a file of the same name, which is exactly what articy skipped before 1.1.7.
                var restoreFolder = Path.Combine(work, "restore");
                Directory.CreateDirectory(restoreFolder);
                File.Copy(original, Path.Combine(restoreFolder, baseAfter + target.Extension));
                var refreshed = CollectVoiceOvers(root, cultures);
                var restoreRows = VoiceOverTransferCore.PlanImport(refreshed, new TransferSettings { Folder = restoreFolder }, Directory.EnumerateFiles(restoreFolder), SameContent);
                var restoreRow = restoreRows.SingleOrDefault(r => r.Record?.AssetId == target.AssetId);
                Check(restoreRow?.Status == TransferStatus.Ready, $"restore: the original plans as a clean replace (got {restoreRow?.StatusText ?? "no row"}: {restoreRow?.Detail})");
                if (restoreRow?.Status != TransferStatus.Ready) return;

                var (restored, restoreFailed) = RunImport(new TransferSettings { Folder = restoreFolder }, restoreRows);
                Check(restored == 1 && restoreFailed == 0, $"restore: the import replaced the same voice-over again with a file of the same name ({restored} replaced, {restoreFailed} failed{(restoreFailed > 0 ? ": " + restoreRow.Detail : "")})");
                if (restored != 1)
                {
                    var back = DiagnoseSkippedReplacement(asset, Path.Combine(restoreFolder, restoreRow.Choice), original, Path.Combine(work, "restore_diagnosis"), Check, Note, restoreRow);
                    if (!back)
                        Note($"WARNING: {Path.GetFileName(target.SourcePath)} still holds the swapped-in audio. The original is saved at {original}: copy it back over the stored file with articy closed.");
                }

                var final = Snapshot(asset);
                Check(final.Path != null && SameContent(final.Path, original), "restore: the original content is back");
                Check(final.DisplayName == before.DisplayName && final.Text == before.Text && final.ExternalId == before.ExternalId, "restore: metadata still as before");
                Note($"stored file after restore: {final.FileName}");
            }
            catch (Exception ex)
            {
                Check(false, "unexpected exception: " + ex);
            }
            finally
            {
                report.AppendLine();
                report.AppendLine($"{passed} passed, {failed} failed");
                var reportPath = Path.Combine(work, "VoiceOverSelfTest_report.txt");
                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
                ShowReport("Voice-Over Self-Test", $"{passed} passed, {failed} failed.\n\nReport: {reportPath}\n\n{report}");
            }
        }

        /// <summary>
        /// Runs only when the import failed to replace a file: checks the import reported it as Failed, then
        /// offers the same file again by hand with its date kept and, if that does not land, with the date
        /// set to now, and says which explanation fits. Returns whether the file finally landed.
        /// </summary>
        private bool DiagnoseSkippedReplacement(ObjectProxy aAsset, string aSource, string aExpected, string aFolder,
            Action<bool, string> aCheck, Action<string> aNote, TransferRow aRow)
        {
            aCheck(aRow.Status == TransferStatus.Failed && (aRow.Detail ?? "").Contains("kept the old file"),
                "the failed replacement is reported as Failed, not as replaced");
            aNote($"OriginalSource: {RawText(aAsset, "OriginalSource")}");

            var landed = TryChangeAsset(aAsset, aSource, aExpected, Path.Combine(aFolder, "date_kept"), false, aNote, out var how1);
            aNote("by hand, file date kept: " + how1);
            if (landed)
            {
                aNote("FINDING: the file lands by hand; the import itself needs another look");
                return true;
            }

            landed = TryChangeAsset(aAsset, aSource, aExpected, Path.Combine(aFolder, "date_now"), true, aNote, out var how2);
            aNote("by hand, file date set to now: " + how2);
            aNote(landed
                ? "FINDING: articy skipped a file whose name and date matched its last import, even though the import refreshes the date"
                : "FINDING: neither the date nor waiting explains it: needs another look");
            return landed;
        }

        /// <summary>
        /// Hands a staged copy of the source to ChangeAsset and watches the stored file: right after the
        /// call returns, after WaitForAssetProcessing, then every 200 ms for 10 s while letting articy's
        /// dispatcher run. Returns whether the stored file ended up with the expected content, and how.
        /// </summary>
        private bool TryChangeAsset(ObjectProxy aAsset, string aSource, string aExpected, string aStageFolder, bool aFreshDate,
            Action<string> aNote, out string aHow)
        {
            Directory.CreateDirectory(aStageFolder);
            var stored = aAsset[ObjectPropertyNames.AbsoluteFilePath] as string;
            var staged = Path.Combine(aStageFolder, VoiceOverTransferCore.SplitTag(Path.GetFileNameWithoutExtension(stored)).Base + Path.GetExtension(aSource));
            File.Copy(aSource, staged, true);
            if (aFreshDate)
                File.SetLastWriteTimeUtc(staged, DateTime.UtcNow);
            aNote($"  staged {staged} (date {File.GetLastWriteTime(staged):yyyy-MM-dd HH:mm:ss}, {new FileInfo(staged).Length} bytes)");

            try
            {
                Session.ChangeAsset(aAsset, staged);
            }
            catch (Exception ex)
            {
                aHow = "ChangeAsset threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            bool Landed() => aAsset[ObjectPropertyNames.AbsoluteFilePath] is string path && SameContent(path, aExpected);

            if (Landed()) { aHow = "landed immediately when ChangeAsset returned"; return true; }
            Session.WaitForAssetProcessing(new WaitForAssetProcessingArgs());
            if (Landed()) { aHow = "landed after the processing wait"; return true; }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 10)
            {
                DrainDispatcher();
                System.Threading.Thread.Sleep(200);
                if (Landed()) { aHow = $"landed {clock.Elapsed.TotalSeconds:F1} s after the processing wait"; return true; }
            }

            var now = aAsset[ObjectPropertyNames.AbsoluteFilePath] as string;
            aHow = $"did not land within 10 s (stored file {Path.GetFileName(now)}, {(now != null && File.Exists(now) ? new FileInfo(now).Length + " bytes" : "missing")})";
            return false;
        }

        private static string RawText(ObjectProxy aObject, string aProperty)
        {
            try { return aObject.HasProperty(aProperty) ? aObject[aProperty]?.ToString() ?? "(null)" : "(no such property)"; }
            catch (Exception ex) { return "(unreadable: " + ex.Message + ")"; }
        }

        private static string FileHash(string aPath)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(aPath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private sealed class AssetSnapshot
        {
            public string Path;
            public string FileName;
            public string DisplayName;
            public string Text;
            public string ExternalId;
        }

        private static AssetSnapshot Snapshot(ObjectProxy aAsset)
        {
            var path = aAsset[ObjectPropertyNames.AbsoluteFilePath] as string;
            return new AssetSnapshot
            {
                Path = path,
                FileName = path == null ? "(none)" : Path.GetFileName(path),
                DisplayName = aAsset.HasProperty(ObjectPropertyNames.DisplayName) ? aAsset[ObjectPropertyNames.DisplayName]?.ToString() : null,
                Text = aAsset.HasProperty(ObjectPropertyNames.Text) ? aAsset[ObjectPropertyNames.Text]?.ToString() : null,
                ExternalId = aAsset.HasProperty(ObjectPropertyNames.ExternalId) ? aAsset[ObjectPropertyNames.ExternalId]?.ToString() : null,
            };
        }

        /// <summary>Fragments (and their property) whose voice-over in this record's culture is this record's asset.</summary>
        private HashSet<string> BoundFragments(VoRecord aRecord)
        {
            var result = new HashSet<string>();
            var fragment = Session.GetObjectById(aRecord.FragmentId);
            if (fragment == null) return result;
            foreach (var (property, text) in EnumerateVoiceOverTextProxies(fragment))
            {
                var bound = text.VoiceOverReferences[aRecord.Culture];
                if (bound != null && bound.IsValid && bound.Id == aRecord.AssetId)
                    result.Add($"{fragment.Id:X}.{property}");
            }
            return result;
        }
    }
}
