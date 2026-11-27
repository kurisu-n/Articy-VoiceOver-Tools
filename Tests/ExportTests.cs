using System.IO;
using System.Linq;
using Kurisu.VoiceOverTools.Core;
using static Kurisu.VoiceOverTools.Tests.Program;

namespace Kurisu.VoiceOverTools.Tests
{
    internal static class ExportTests
    {
        public static void Run()
        {
            SharedAssetWrittenOnce();
            TwoSourcesOneTargetIsAConflict();
            CaseOnlyCollisionIsAConflict();
            MissingSource();
            ExistingTargets();
            LongPath();
            WritesNestedGreekFolders();
            ReadOnlyTargetFailsAloneAndOthersContinue();
        }

        private static void SharedAssetWrittenOnce()
        {
            var store = new Store(Temp("exp_shared_src"));
            var asset = store.Add("0x01_el", "el");
            var records = new[] { asset, store.Bind(asset, 900), store.Bind(asset, 901) };
            var rows = VoiceOverTransferCore.PlanExport(records, new TransferSettings { Folder = Temp("exp_shared_out") });
            Equal(1, rows.Count, "shared asset: one row");
            Check(rows[0].Detail.Contains("shared by 3 fragments"), "shared asset: flagged as shared by 3");
        }

        private static void TwoSourcesOneTargetIsAConflict()
        {
            // The main-language strip turns "X_el" into "X", which is also another asset's free name;
            // with tags stripped both land on "X.mp3".
            var store = new Store(Temp("exp_clash_src"));
            var records = new[] { store.Add("X_el", "el"), store.Add("X", "en") };
            var out_ = Temp("exp_clash_out");
            var rows = VoiceOverTransferCore.PlanExport(records, new TransferSettings { Folder = out_, MainCulture = "el", StripTag = true });
            Equal(1, rows.Count, "clash: one row");
            Equal(TransferStatus.Conflict, rows[0].Status, "clash: conflict");
            Check(!rows[0].Selectable, "clash: not selectable");
            VoiceOverTransferCore.RunExport(rows);
            Check(!File.Exists(Path.Combine(out_, "X.mp3")), "clash: nothing written");
        }

        private static void CaseOnlyCollisionIsAConflict()
        {
            var store = new Store(Temp("exp_case_src"));
            var records = new[] { store.Add("Intro_el", "el"), store.Add("INTRO_el", "el") };
            var rows = VoiceOverTransferCore.PlanExport(records, new TransferSettings { Folder = Temp("exp_case_out"), StripTag = true });
            Equal(TransferStatus.Conflict, rows.Single().Status, "names differing only in case collide on Windows");
        }

        private static void MissingSource()
        {
            var record = new VoRecord { AssetId = 1, FragmentId = 1, Culture = "el", SourcePath = Path.Combine(Temp("exp_missing"), "gone_el(0A92).mp3"), FragmentPath = "Act / Line" };
            var noPath = new VoRecord { AssetId = 2, FragmentId = 2, Culture = "el", SourcePath = null };
            var rows = VoiceOverTransferCore.PlanExport(new[] { record, noPath }, new TransferSettings { Folder = Temp("exp_missing_out") });
            Equal(2, rows.Count(r => r.Status == TransferStatus.Missing), "missing and pathless sources are reported");
            Check(rows.All(r => !r.Selectable), "missing rows are not selectable");
        }

        private static void ExistingTargets()
        {
            var store = new Store(Temp("exp_exist_src"));
            var record = store.Add("0x02_el", "el");
            var out_ = Temp("exp_exist_out");
            File.WriteAllText(Path.Combine(out_, "0x02_el.mp3"), "old");

            var skip = VoiceOverTransferCore.PlanExport(new[] { record }, new TransferSettings { Folder = out_, StripTag = true });
            Equal(TransferStatus.Exists, skip[0].Status, "existing, skip: status");
            Check(!skip[0].Selectable, "existing, skip: not selectable");
            VoiceOverTransferCore.RunExport(skip);
            Equal("old", File.ReadAllText(Path.Combine(out_, "0x02_el.mp3")), "existing, skip: untouched");

            var overwrite = VoiceOverTransferCore.PlanExport(new[] { record }, new TransferSettings { Folder = out_, StripTag = true, Overwrite = true });
            Equal(TransferStatus.Overwrite, overwrite[0].Status, "existing, overwrite: status");
            VoiceOverTransferCore.RunExport(overwrite);
            Check(SameContent(record.SourcePath, Path.Combine(out_, "0x02_el.mp3")), "existing, overwrite: replaced");
        }

        private static void LongPath()
        {
            var store = new Store(Temp("exp_long_src"));
            var deep = Enumerable.Range(0, 6).Select(i => new string((char)('A' + i), 60)).ToArray();
            var record = store.Add("0x03_el", "el", aHierarchy: deep);
            var rows = VoiceOverTransferCore.PlanExport(new[] { record }, new TransferSettings { Folder = Temp("exp_long_out"), HierarchyLevels = 6 });
            Equal(TransferStatus.Conflict, rows.Single().Status, "over-long path is caught before writing");
            Check(rows[0].Detail.Contains("characters"), "over-long path explains itself");
        }

        private static void WritesNestedGreekFolders()
        {
            var store = new Store(Temp("exp_greek_src"));
            var hierarchy = new[] { "Νταλιάνι", "Σκηνή 1: Η γέφυρα", "CON", "..", "Διάλογος." };
            var records = new[]
            {
                store.Add("0x10_el", "el", aSpeaker: "Πέτρος", aHierarchy: hierarchy),
                store.Add("0x10_en", "en", aSpeaker: "Πέτρος", aHierarchy: hierarchy),
                store.Add("0x11_el", "el", aSpeaker: null, aHierarchy: hierarchy),
            };
            var out_ = Temp("exp_greek_out");
            var settings = new TransferSettings { Folder = out_, MainCulture = "el", StripTag = true, HierarchyLevels = 5, Speaker = SpeakerSplit.AfterHierarchy, FolderPerLanguage = true };
            var rows = VoiceOverTransferCore.PlanExport(records, settings);
            var (written, failed) = VoiceOverTransferCore.RunExport(rows);
            Equal(3, written, "Greek nested: all written");
            Equal(0, failed, "Greek nested: none failed");

            var expected = Path.Combine(out_, "el", "Νταλιάνι", "Σκηνή 1_ Η γέφυρα", "CON_", "_", "Διάλογος", "Πέτρος", "0x10.mp3");
            Check(File.Exists(expected), "Greek nested: expected path exists: " + expected);
            Check(File.Exists(Path.Combine(out_, "en", "Νταλιάνι", "Σκηνή 1_ Η γέφυρα", "CON_", "_", "Διάλογος", "Πέτρος", "0x10_en.mp3")), "Greek nested: English keeps its suffix under en");
            Check(File.Exists(Path.Combine(out_, "el", "Νταλιάνι", "Σκηνή 1_ Η γέφυρα", "CON_", "_", "Διάλογος", "(no speaker)", "0x11.mp3")), "Greek nested: no-speaker folder");
            Check(Directory.EnumerateFiles(out_, "*", SearchOption.AllDirectories).All(f => Path.GetFullPath(f).StartsWith(Path.GetFullPath(out_))), "nothing written outside the chosen folder");
        }

        private static void ReadOnlyTargetFailsAloneAndOthersContinue()
        {
            var store = new Store(Temp("exp_ro_src"));
            var records = new[] { store.Add("0x20_el", "el"), store.Add("0x21_el", "el") };
            var out_ = Temp("exp_ro_out");
            var locked = Path.Combine(out_, "0x20_el.mp3");
            File.WriteAllText(locked, "locked");
            File.SetAttributes(locked, FileAttributes.ReadOnly);

            var rows = VoiceOverTransferCore.PlanExport(records, new TransferSettings { Folder = out_, StripTag = true, Overwrite = true });
            var (written, failed) = VoiceOverTransferCore.RunExport(rows);
            Equal(1, written, "read-only target: the other file is written");
            Equal(1, failed, "read-only target: that one fails");
            Check(rows.Any(r => r.Status == TransferStatus.Failed && !string.IsNullOrEmpty(r.Detail)), "read-only target: failure explained in the row");
        }
    }
}
