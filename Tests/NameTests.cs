using Kurisu.VoiceOverTools.Core;
using static Kurisu.VoiceOverTools.Tests.Program;

namespace Kurisu.VoiceOverTools.Tests
{
    internal static class NameTests
    {
        public static void Run()
        {
            SplitTag();
            StripCulture();
            ExportName();
            SafeFolderName();
            ExportFolder();
        }

        private static void SplitTag()
        {
            Equal(("0x0100000000001B4D_el", "1C49"), VoiceOverTransferCore.SplitTag("0x0100000000001B4D_el(1C49)"), "articy name");
            Equal(("Harbour__SC_01_CPT_09", "1901"), VoiceOverTransferCore.SplitTag("Harbour__SC_01_CPT_09(1901)"), "old free name");
            Equal(("x", "ABCDE"), VoiceOverTransferCore.SplitTag("x(ABCDE)"), "5-digit tag (project past FFFF)");
            Equal(("x", "abcd"), VoiceOverTransferCore.SplitTag("x(abcd)"), "lower-case hex tag");
            Equal(("line (2)", (string)null), VoiceOverTransferCore.SplitTag("line (2)"), "Windows duplicate is not a tag");
            Equal(("line (0A92)", (string)null), VoiceOverTransferCore.SplitTag("line (0A92)"), "space before the bracket is not a tag");
            Equal(("line(2)", (string)null), VoiceOverTransferCore.SplitTag("line(2)"), "fewer than 4 digits is not a tag");
            Equal(("x(GHIJ)", (string)null), VoiceOverTransferCore.SplitTag("x(GHIJ)"), "non-hex is not a tag");
            Equal(("x(1C49) ", (string)null), VoiceOverTransferCore.SplitTag("x(1C49) "), "tag not at the end");
            Equal(("(1C49)", (string)null), VoiceOverTransferCore.SplitTag("(1C49)"), "a name that is only a tag keeps it, so no empty base");
            Equal(("x(1C49)", "0A92"), VoiceOverTransferCore.SplitTag("x(1C49)(0A92)"), "only the last tag is split off");
            Equal(("", (string)null), VoiceOverTransferCore.SplitTag(null), "null");
            Equal(("Σκηνή_el", "0A92"), VoiceOverTransferCore.SplitTag("Σκηνή_el(0A92)"), "Greek base");
        }

        private static void StripCulture()
        {
            Equal("x", VoiceOverTransferCore.StripCulture("x_el", "el"), "plain");
            Equal("x", VoiceOverTransferCore.StripCulture("x_EL", "el"), "case-insensitive");
            Equal((string)null, VoiceOverTransferCore.StripCulture("_el", "el"), "would leave an empty name");
            Equal((string)null, VoiceOverTransferCore.StripCulture("x_el-GR", "el"), "region suffix is a different culture");
            Equal("x", VoiceOverTransferCore.StripCulture("x_el-GR", "el-GR"), "region culture");
            Equal((string)null, VoiceOverTransferCore.StripCulture("model_elf", "el"), "suffix must be the whole culture");
            Equal((string)null, VoiceOverTransferCore.StripCulture("x", "el"), "no suffix");
            Equal((string)null, VoiceOverTransferCore.StripCulture("x_el", null), "no culture");
            Equal("x_el", VoiceOverTransferCore.StripCulture("x_el_el", "el"), "only one suffix is stripped");
        }

        private static void ExportName()
        {
            var el = new VoRecord { Culture = "el", SourcePath = @"C:\a\0x0100000000001B4D_el(1C49).mp3" };
            var en = new VoRecord { Culture = "en", SourcePath = @"C:\a\0x0100000000001B4D_en(1C4A).mp3" };
            var free = new VoRecord { Culture = "el", SourcePath = @"C:\a\Harbour__SC_01(1901).WAV" };

            var mainKeep = new TransferSettings { MainCulture = "el" };
            var mainStrip = new TransferSettings { MainCulture = "el", StripTag = true };
            var noMain = new TransferSettings();

            Equal("0x0100000000001B4D(1C49).mp3", VoiceOverTransferCore.ExportName(el, mainKeep), "main, keep tag");
            Equal("0x0100000000001B4D.mp3", VoiceOverTransferCore.ExportName(el, mainStrip), "main, strip tag");
            Equal("0x0100000000001B4D_en(1C4A).mp3", VoiceOverTransferCore.ExportName(en, mainKeep), "other language keeps suffix");
            Equal("0x0100000000001B4D_en.mp3", VoiceOverTransferCore.ExportName(en, mainStrip), "other language, strip tag");
            Equal("0x0100000000001B4D_el(1C49).mp3", VoiceOverTransferCore.ExportName(el, noMain), "no main language");
            Equal("Harbour__SC_01(1901).WAV", VoiceOverTransferCore.ExportName(free, mainKeep), "free name untouched, extension case kept");
        }

        private static void SafeFolderName()
        {
            Equal("CON_", VoiceOverTransferCore.SafeFolderName("CON"), "reserved device name");
            Equal("con_.txt", VoiceOverTransferCore.SafeFolderName("con.txt"), "reserved with extension: mark the device part");
            Equal("aux_.wav", VoiceOverTransferCore.SafeFolderName("aux.wav"), "reserved stem with an audio-like extension");
            Equal("NUL_.a.b", VoiceOverTransferCore.SafeFolderName("NUL.a.b"), "reserved stem, several dots");
            Equal("AUX_ .txt", VoiceOverTransferCore.SafeFolderName("AUX .txt"), "reserved stem followed by a space");
            Equal("auxiliary", VoiceOverTransferCore.SafeFolderName("auxiliary"), "a word that starts with a device name is fine");
            Equal("COM10", VoiceOverTransferCore.SafeFolderName("COM10"), "COM10 is not reserved");
            Equal("Lpt1_", VoiceOverTransferCore.SafeFolderName("Lpt1"), "reserved, any case");
            Equal("a_b", VoiceOverTransferCore.SafeFolderName("a/b"), "slash");
            Equal("a_b", VoiceOverTransferCore.SafeFolderName("a\\b"), "backslash");
            Equal("Q_ A_", VoiceOverTransferCore.SafeFolderName("Q? A*"), "wildcards");
            Equal("_", VoiceOverTransferCore.SafeFolderName(".."), "parent-folder name cannot escape");
            Equal("_", VoiceOverTransferCore.SafeFolderName("."), "current-folder name");
            Equal("_", VoiceOverTransferCore.SafeFolderName("   "), "blank");
            Equal("_", VoiceOverTransferCore.SafeFolderName(null), "null");
            Equal("Scene 1", VoiceOverTransferCore.SafeFolderName("Scene 1. "), "trailing dot and space");
            Equal("Γέφυρα της Άρτας", VoiceOverTransferCore.SafeFolderName("Γέφυρα της Άρτας"), "Greek kept");
            Equal("a_b", VoiceOverTransferCore.SafeFolderName("a\tb"), "control character");
            Equal("a_b_c", VoiceOverTransferCore.SafeFolderName("a\"b|c"), "quote and pipe");
        }

        private static void ExportFolder()
        {
            var record = new VoRecord { Culture = "el", Speaker = "Πέτρος", Hierarchy = new[] { "Act 1", "Scene: 2", "Dialogue" } };
            var root = @"C:\out";

            Equal(root, VoiceOverTransferCore.ExportFolder(record, new TransferSettings { Folder = root }), "flat");
            Equal(@"C:\out\Act 1\Scene_ 2", VoiceOverTransferCore.ExportFolder(record, new TransferSettings { Folder = root, HierarchyLevels = 2 }), "2 levels, sanitized");
            Equal(@"C:\out\Act 1\Scene_ 2\Dialogue", VoiceOverTransferCore.ExportFolder(record, new TransferSettings { Folder = root, HierarchyLevels = 6 }), "more levels than exist");
            Equal(@"C:\out\Πέτρος\Act 1", VoiceOverTransferCore.ExportFolder(record, new TransferSettings { Folder = root, HierarchyLevels = 1, Speaker = SpeakerSplit.BeforeHierarchy }), "speaker before");
            Equal(@"C:\out\Act 1\Πέτρος", VoiceOverTransferCore.ExportFolder(record, new TransferSettings { Folder = root, HierarchyLevels = 1, Speaker = SpeakerSplit.AfterHierarchy }), "speaker after");
            Equal(@"C:\out\el\Πέτρος", VoiceOverTransferCore.ExportFolder(record, new TransferSettings { Folder = root, FolderPerLanguage = true, Speaker = SpeakerSplit.AfterHierarchy }), "language first, then speaker");
            Equal(@"C:\out\(no speaker)", VoiceOverTransferCore.ExportFolder(new VoRecord { Culture = "el" }, new TransferSettings { Folder = root, Speaker = SpeakerSplit.BeforeHierarchy }), "no speaker");
            Equal(@"C:\out\(no speaker)", VoiceOverTransferCore.ExportFolder(new VoRecord { Culture = "el", Speaker = "  " }, new TransferSettings { Folder = root, Speaker = SpeakerSplit.BeforeHierarchy }), "blank speaker");
            Equal(root, VoiceOverTransferCore.ExportFolder(new VoRecord { Culture = "el", Hierarchy = null }, new TransferSettings { Folder = root, HierarchyLevels = 3 }), "null hierarchy");
            Equal(root, VoiceOverTransferCore.ExportFolder(record, new TransferSettings { Folder = root, HierarchyLevels = -2 }), "negative levels");
        }
    }
}
