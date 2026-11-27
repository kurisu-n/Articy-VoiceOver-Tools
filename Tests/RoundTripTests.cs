using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kurisu.VoiceOverTools.Core;
using static Kurisu.VoiceOverTools.Tests.Program;

namespace Kurisu.VoiceOverTools.Tests
{
    /// <summary>
    /// Builds a synthetic project harder than any real one (Greek, reserved and invalid folder names,
    /// names differing only in case, non-tag brackets, voice-overs shared across branches, three
    /// languages, missing speakers, deep and shallow fragments), exports it under every combination of
    /// options, and imports the result straight back. Whatever the export wrote must come back as
    /// "unchanged" with no conflicts and no stray files; whatever it refused must be explained.
    /// </summary>
    internal static class RoundTripTests
    {
        private static readonly string[] sFolderNames =
        {
            "Act 1", "ACT 1", "Νταλιάνι", "Σκηνή: Η γέφυρα", "CON", "aux.wav", "..", " Intro. ", "Q&A?", "A/B",
            "Διάλογος (2)", "Scene (0A92)", "very long folder name that keeps going for a while", "x", "Ω",
        };

        private static readonly string[] sSpeakers = { "Πέτρος", "Vaggelis", "NUL", "Mr. Smith.", null, "  ", "Κυρά/Μαρία" };

        private static readonly string[] sCultures = { "el", "en", "de-DE" };

        public static void Run()
        {
            var watch = Stopwatch.StartNew();
            var records = Generate(new Random(20261124), Temp("rt_src"), 260);
            int combos = 0;

            foreach (var levels in new[] { 0, 1, 3, 6 })
            foreach (var speaker in new[] { SpeakerSplit.None, SpeakerSplit.BeforeHierarchy, SpeakerSplit.AfterHierarchy })
            foreach (var perLanguage in new[] { false, true })
            foreach (var stripTag in new[] { false, true })
            foreach (var main in new[] { null, "el" })
            {
                combos++;
                var label = $"levels={levels} speaker={speaker} perLang={perLanguage} stripTag={stripTag} main={main ?? "none"}";
                var settings = new TransferSettings
                {
                    Folder = Temp($"rt_out_{combos}"),
                    HierarchyLevels = levels,
                    Speaker = speaker,
                    FolderPerLanguage = perLanguage,
                    StripTag = stripTag,
                    MainCulture = main,
                };
                RoundTrip(records, settings, label);
            }

            Console.WriteLine($"round trip: {combos} option combinations over {records.Count} bindings in {watch.Elapsed.TotalSeconds:F1}s");
        }

        private static void RoundTrip(List<VoRecord> aRecords, TransferSettings aSettings, string aLabel)
        {
            var exportRows = VoiceOverTransferCore.PlanExport(aRecords, aSettings);
            var (written, failed) = VoiceOverTransferCore.RunExport(exportRows);
            Equal(0, failed, aLabel + ": no export failures, e.g. "
                + string.Join(" | ", exportRows.Where(r => r.Status == TransferStatus.Failed).Take(2).Select(r => $"{r.Title}: {r.Detail}")));

            // The generator never makes two different files collide; anything refused must be over-long.
            var refused = exportRows.Where(r => r.Status == TransferStatus.Conflict).ToList();
            Check(refused.All(r => r.Detail.Contains("characters")), aLabel + ": the only refusals are over-long paths: " + string.Join(" | ", refused.Select(r => r.Detail).Take(2)));
            Check(exportRows.All(r => r.Status != TransferStatus.Missing), aLabel + ": every source found");

            var files = Directory.EnumerateFiles(aSettings.Folder, "*", SearchOption.AllDirectories).ToList();
            Equal(written, files.Count, aLabel + ": files on disk equal files written");
            Check(files.All(f => Path.GetFullPath(f).StartsWith(Path.GetFullPath(aSettings.Folder) + Path.DirectorySeparatorChar)), aLabel + ": nothing outside the folder");

            var importRows = VoiceOverTransferCore.PlanImport(aRecords, aSettings, files, SameContent);
            var notUnchanged = importRows.Where(r => r.Status != TransferStatus.Unchanged).ToList();
            Check(notUnchanged.Count == 0, aLabel + $": every exported file imports back unchanged; {notUnchanged.Count} did not, e.g. "
                + string.Join(" | ", notUnchanged.Take(3).Select(r => $"{r.StatusText}: {r.Title}: {r.Detail}")));
            Equal(0, VoiceOverTransferCore.RowsToApply(importRows).Count(), aLabel + ": nothing to apply");

            // Every asset whose file reached the folder is accounted for.
            var exportedAssets = exportRows.Where(r => r.Status == TransferStatus.Done)
                .Select(r => aRecords.First(a => a.SourcePath == r.SourcePath).AssetId).Distinct().Count();
            Equal(exportedAssets, importRows.Count(r => r.Record != null), aLabel + ": one import row per exported voice-over");
        }

        private static List<VoRecord> Generate(Random aRandom, string aFolder, int aAssets)
        {
            var store = new Store(aFolder);
            var records = new List<VoRecord>();
            ulong fragment = 0x0100000000001000;

            for (int i = 0; i < aAssets; i++)
            {
                fragment++;
                var culture = sCultures[i % sCultures.Length];
                var hierarchy = Enumerable.Range(0, aRandom.Next(0, 7)).Select(_ => sFolderNames[aRandom.Next(sFolderNames.Length)]).ToArray();
                var speaker = sSpeakers[aRandom.Next(sSpeakers.Length)];
                var extension = aRandom.Next(5) == 0 ? ".wav" : ".mp3";

                // Mostly articy's own naming, sometimes the older free names with awkward characters.
                string baseName = (i % 7) switch
                {
                    0 => $"Σκηνή {i} (take 2)_{culture}",
                    1 => $"Harbour__SC_{i:00}.final_{culture}",
                    2 => $"line {i} (2)",
                    3 => $"Model_el_{i}_{culture}",
                    _ => $"0x{fragment:X16}_{culture}",
                };

                var record = store.Add(baseName, culture, extension, speaker, hierarchy, fragment);
                records.Add(record);

                // Every ninth voice-over is shared with up to two more fragments, sometimes in other branches.
                if (i % 9 == 0)
                {
                    for (int k = 0; k < aRandom.Next(1, 3); k++)
                    {
                        fragment++;
                        var otherBranch = aRandom.Next(2) == 0 ? record.Hierarchy.ToArray()
                            : Enumerable.Range(0, aRandom.Next(0, 4)).Select(_ => sFolderNames[aRandom.Next(sFolderNames.Length)]).ToArray();
                        records.Add(store.Bind(record, fragment, otherBranch));
                    }
                }
            }
            return records;
        }
    }
}
