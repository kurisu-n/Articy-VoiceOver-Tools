using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kurisu.VoiceOverTools.Core;
using static Kurisu.VoiceOverTools.Tests.Program;

namespace Kurisu.VoiceOverTools.Tests
{
    internal static class ImportTests
    {
        public static void Run()
        {
            ExactTagMatch();
            UntaggedMatch();
            MainLanguageSuffixOptional();
            MainStrippedButTagged();
            StaleTagIsAConflict();
            TwoFilesOneAsset();
            OneFileTwoAssets();
            FormatDiffers();
            EmptyFile();
            IdenticalIsUnchanged();
            FileFilteringAndOddNames();
            SharedAssetFlagged();
            RowsToApplyRules();
            StagedName();
        }

        private static List<TransferRow> Plan(IEnumerable<VoRecord> aRecords, string aFolder, string aMain = null)
        {
            var files = Directory.EnumerateFiles(aFolder, "*", SearchOption.AllDirectories).ToList();
            return VoiceOverTransferCore.PlanImport(aRecords, new TransferSettings { Folder = aFolder, MainCulture = aMain }, files, SameContent);
        }

        private static void Write(string aFolder, string aRelative, string aContent = "new")
        {
            var path = Path.Combine(aFolder, aRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, aContent);
        }

        private static TransferRow RowFor(List<TransferRow> aRows, VoRecord aRecord)
        {
            return aRows.SingleOrDefault(r => r.Record != null && r.Record.AssetId == aRecord.AssetId);
        }

        private static void ExactTagMatch()
        {
            var store = new Store(Temp("imp_exact_src"));
            var a = store.Add("0x01_el", "el", aExplicitTag: "0A92");
            var folder = Temp("imp_exact_in");
            Write(folder, "0x01_el(0A92).mp3");
            var rows = Plan(new[] { a }, folder);
            Equal(TransferStatus.Ready, RowFor(rows, a)?.Status, "exact tag: ready");
        }

        private static void UntaggedMatch()
        {
            var store = new Store(Temp("imp_loose_src"));
            var a = store.Add("0x02_el", "el");
            var folder = Temp("imp_loose_in");
            Write(folder, "sub/deeper/0x02_el.MP3");
            var rows = Plan(new[] { a }, folder);
            Equal(TransferStatus.Ready, RowFor(rows, a)?.Status, "untagged, nested, upper-case extension: ready");
        }

        private static void MainLanguageSuffixOptional()
        {
            var store = new Store(Temp("imp_main_src"));
            var a = store.Add("0x03_el", "el");
            var folder = Temp("imp_main_in");
            Write(folder, "0x03.mp3");
            Equal(TransferStatus.Ready, RowFor(Plan(new[] { a }, folder, "el"), a)?.Status, "suffix missing, main el: ready");
            Check(Plan(new[] { a }, folder).All(r => r.Status == TransferStatus.NoMatch), "suffix missing, no main language: no match");
            Check(Plan(new[] { a }, folder, "en").All(r => r.Status == TransferStatus.NoMatch), "suffix missing, other main language: no match");
        }

        private static void MainStrippedButTagged()
        {
            var store = new Store(Temp("imp_maintag_src"));
            var a = store.Add("0x04_el", "el", aExplicitTag: "1C49");
            var folder = Temp("imp_maintag_in");
            Write(folder, "0x04(1C49).mp3");
            Equal(TransferStatus.Ready, RowFor(Plan(new[] { a }, folder, "el"), a)?.Status, "main stripped, tag kept: ready");
        }

        private static void StaleTagIsAConflict()
        {
            var store = new Store(Temp("imp_stale_src"));
            var a = store.Add("0x05_el", "el", aExplicitTag: "1C49");
            var folder = Temp("imp_stale_in");
            Write(folder, "0x05_el(0A92).mp3");
            var row = RowFor(Plan(new[] { a }, folder), a);
            Equal(TransferStatus.Conflict, row?.Status, "different tag: conflict");
            Equal(VoiceOverTransferCore.SkipChoice, row?.Choice, "different tag: starts skipped");
            Check(row != null && row.Detail.Contains("different tag"), "different tag: explained");
        }

        private static void TwoFilesOneAsset()
        {
            var store = new Store(Temp("imp_two_src"));
            var a = store.Add("0x06_el", "el", aExplicitTag: "0A92");
            var folder = Temp("imp_two_in");
            Write(folder, "take1/0x06_el.mp3", "one");
            Write(folder, "take2/0x06_el(0A92).mp3", "two");
            var row = RowFor(Plan(new[] { a }, folder), a);
            Equal(TransferStatus.Conflict, row?.Status, "two files: conflict");
            Equal(3, row?.Choices.Count, "two files: skip plus both files offered");
            Equal(VoiceOverTransferCore.SkipChoice, row?.Choice, "two files: starts skipped");
        }

        private static void OneFileTwoAssets()
        {
            // With main language el, "X_el" answers to "X", and so does a free-named English "X".
            var store = new Store(Temp("imp_amb_src"));
            var el = store.Add("X_el", "el");
            var en = store.Add("X", "en");
            var folder = Temp("imp_amb_in");
            Write(folder, "X.mp3");
            var rows = Plan(new[] { el, en }, folder, "el");
            Equal(TransferStatus.Conflict, RowFor(rows, el)?.Status, "ambiguous file: conflict on the first asset");
            Equal(TransferStatus.Conflict, RowFor(rows, en)?.Status, "ambiguous file: conflict on the second asset");
            Check(RowFor(rows, el).Detail.Contains("also matches"), "ambiguous file: explained");
        }

        private static void FormatDiffers()
        {
            var store = new Store(Temp("imp_fmt_src"));
            var a = store.Add("0x07_el", "el", ".mp3");
            var folder = Temp("imp_fmt_in");
            Write(folder, "0x07_el.wav");
            var row = RowFor(Plan(new[] { a }, folder), a);
            Equal(TransferStatus.Conflict, row?.Status, "wav over mp3: conflict");
            Check(row != null && row.Detail.Contains(".wav"), "wav over mp3: explained");
        }

        private static void EmptyFile()
        {
            var store = new Store(Temp("imp_empty_src"));
            var a = store.Add("0x08_el", "el");
            var folder = Temp("imp_empty_in");
            Write(folder, "0x08_el.mp3", "");
            Equal(TransferStatus.Conflict, RowFor(Plan(new[] { a }, folder), a)?.Status, "0-byte file: conflict");
        }

        private static void IdenticalIsUnchanged()
        {
            var store = new Store(Temp("imp_same_src"));
            var a = store.Add("0x09_el", "el");
            var folder = Temp("imp_same_in");
            File.Copy(a.SourcePath, Path.Combine(folder, "0x09_el.mp3"));
            var row = RowFor(Plan(new[] { a }, folder), a);
            Equal(TransferStatus.Unchanged, row?.Status, "identical: unchanged");
            Check(row != null && !row.Selectable, "identical: not selectable");
        }

        private static void FileFilteringAndOddNames()
        {
            var store = new Store(Temp("imp_odd_src"));
            var a = store.Add("0x0A_el", "el");
            var folder = Temp("imp_odd_in");
            Write(folder, "0x0A_el.txt");             // not audio: ignored entirely
            Write(folder, "._0x0A_el.mp3");           // macOS resource fork
            Write(folder, "0x0A_el (2).mp3");         // Windows duplicate
            Write(folder, "0x0A_el - Copy.mp3");      // Explorer copy
            var rows = Plan(new[] { a }, folder);
            Check(RowFor(rows, a) == null, "odd names: nothing guessed onto the asset");
            Equal(3, rows.Count(r => r.Status == TransferStatus.NoMatch), "odd names: the three audio files are listed as no match");
            Check(!rows.Any(r => r.Title.EndsWith(".txt")), "odd names: non-audio file ignored");
        }

        private static void SharedAssetFlagged()
        {
            var store = new Store(Temp("imp_shared_src"));
            var a = store.Add("0x0B_el", "el");
            var folder = Temp("imp_shared_in");
            Write(folder, "0x0B_el.mp3");
            var rows = Plan(new[] { a, store.Bind(a, 77), store.Bind(a, 78) }, folder);
            var row = RowFor(rows, a);
            Equal(TransferStatus.Ready, row?.Status, "shared asset: still a clean match");
            Check(row != null && row.Detail.Contains("shared by 3"), "shared asset: flagged");
            Equal(1, rows.Count(r => r.Record != null), "shared asset: one row, not one per fragment");
        }

        private static void RowsToApplyRules()
        {
            var record = new VoRecord { AssetId = 1, SourcePath = "x" };
            var rows = new List<TransferRow>
            {
                new TransferRow { Status = TransferStatus.Ready, Record = record, Selectable = true, Selected = true, Choice = "a.mp3" },
                new TransferRow { Status = TransferStatus.Ready, Record = record, Selectable = true, Selected = false, Choice = "b.mp3" },
                new TransferRow { Status = TransferStatus.Conflict, Record = record, Choice = VoiceOverTransferCore.SkipChoice },
                new TransferRow { Status = TransferStatus.Conflict, Record = record, Choice = "c.mp3" },
                new TransferRow { Status = TransferStatus.Unchanged, Record = record, Selectable = true, Selected = true, Choice = "d.mp3" },
                new TransferRow { Status = TransferStatus.NoMatch, Selectable = true, Selected = true, Choice = "e.mp3" },
                new TransferRow { Status = TransferStatus.Done, Record = record, Choice = "f.mp3" },
            };
            var applied = VoiceOverTransferCore.RowsToApply(rows).Select(r => r.Choice).ToList();
            Equal("a.mp3,c.mp3", string.Join(",", applied), "only ticked clean rows and conflicts with a picked file apply");
        }

        private static void StagedName()
        {
            var record = new VoRecord { SourcePath = @"C:\p\0x0100000000001B4D_el(1C49).mp3" };
            Equal("0x0100000000001B4D_el.wav", VoiceOverTransferCore.StagedFileName(record, @"D:\in\0x0100000000001B4D.wav"), "staged name: stored base, chosen extension");
            var free = new VoRecord { SourcePath = @"C:\p\line (2)(0A92).mp3" };
            Equal("line (2).mp3", VoiceOverTransferCore.StagedFileName(free, @"D:\x.mp3"), "staged name keeps a non-tag bracket");
        }
    }
}
