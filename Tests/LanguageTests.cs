using System;
using System.Collections.Generic;
using System.Linq;
using Kurisu.VoiceOverTools.Core;
using static Kurisu.VoiceOverTools.Tests.Program;

namespace Kurisu.VoiceOverTools.Tests
{
    /// <summary>Matching new-language assets to voice-over slots by name.</summary>
    internal static class LanguageTests
    {
        private static readonly string[] sCultures = { "el", "en", "de-DE" };
        private static ulong sNextId = 1000;

        public static void Run()
        {
            LanguageBases();
            SuffixedToSuffixed();
            PlainSlotSuffixedAsset();
            SuffixedSlotPlainAsset();
            NamedBeatsPlain();
            OtherLanguageNeverUsed();
            TwoNamedIsAConflict();
            ExistingTargetBinding();
            OneLineSeveralSlots();
            SlotWithoutOtherLanguages();
            SlotsWithDifferentNames();
            ApplyRules();
            ShowsWhyAndBothNames();
            DisplayNamesNeverMatched();
            Synthetic();
        }

        private static LanguageSlot Slot(string aPath, params (string Culture, string Name)[] aExisting)
        {
            var slot = new LanguageSlot { FragmentId = sNextId++, Property = "Text", FragmentPath = aPath };
            foreach (var (culture, name) in aExisting)
            {
                slot.Existing[culture] = name;
                slot.ExistingAssets[culture] = sNextId++;
            }
            return slot;
        }

        private static CandidateAsset Asset(string aName, string aLocation = "EN")
        {
            return new CandidateAsset { AssetId = sNextId++, StoredName = aName, Extension = ".mp3", Location = aLocation };
        }

        private static List<TransferRow> Plan(IEnumerable<LanguageSlot> aSlots, IEnumerable<CandidateAsset> aAssets, string aTarget = "en")
        {
            return VoiceOverTransferCore.PlanLanguageMatch(aSlots, aAssets, aTarget, sCultures);
        }

        private static ulong Bound(TransferRow aRow) => aRow.ChoiceAssets[aRow.Choice];

        private static void LanguageBases()
        {
            Equal(("name", "el"), VoiceOverTransferCore.LanguageBase("name_el(0A92)", sCultures), "suffix and tag");
            Equal(("name", (string)null), VoiceOverTransferCore.LanguageBase("name(0A92)", sCultures), "no suffix");
            Equal(("name", "en"), VoiceOverTransferCore.LanguageBase("name_EN", sCultures), "case-insensitive");
            Equal(("name", "de-DE"), VoiceOverTransferCore.LanguageBase("name_de-DE", sCultures), "regional culture");
            Equal(("name", "de-DE"), VoiceOverTransferCore.LanguageBase("name_de", sCultures), "two-letter language stands for its one regional culture");
            Equal(("name_de", (string)null), VoiceOverTransferCore.LanguageBase("name_de", new[] { "de-DE", "de-AT" }), "two regional cultures: _de is ambiguous, left alone");
            Equal(("Harbour__SC_01", (string)null), VoiceOverTransferCore.LanguageBase("Harbour__SC_01(1901)", sCultures), "_01 is not a language");
            Equal(("name_el", "en"), VoiceOverTransferCore.LanguageBase("name_el_en", sCultures), "only the last suffix is the language");
            Equal(("model_elf", (string)null), VoiceOverTransferCore.LanguageBase("model_elf", sCultures), "_elf is not _el");
            Equal(("_el", (string)null), VoiceOverTransferCore.LanguageBase("_el", sCultures), "a name that is only a suffix keeps it");
        }

        private static void SuffixedToSuffixed()
        {
            var slot = Slot("Act / Line", ("el", "0x01_el(0A92)"));
            var asset = Asset("0x01_en(1C49)");
            var row = Plan(new[] { slot }, new[] { asset }).Single();
            Equal(TransferStatus.Ready, row.Status, "name_el slot takes name_en");
            Equal(asset.AssetId, Bound(row), "name_el slot takes name_en: right asset");
        }

        private static void PlainSlotSuffixedAsset()
        {
            var slot = Slot("Act / Line", ("el", "Harbour__SC_01(1901)"));
            var asset = Asset("Harbour__SC_01_en(2001)");
            Equal(TransferStatus.Ready, Plan(new[] { slot }, new[] { asset }).Single().Status, "plain name slot takes name_en");
        }

        private static void SuffixedSlotPlainAsset()
        {
            var slot = Slot("Act / Line", ("el", "0x02_el(0A92)"));
            var asset = Asset("0x02(2002)");
            Equal(TransferStatus.Ready, Plan(new[] { slot }, new[] { asset }).Single().Status, "name_el slot takes plain name");
        }

        private static void NamedBeatsPlain()
        {
            var slot = Slot("Act / Line", ("el", "0x03_el(0A92)"));
            var plain = Asset("0x03(2003)");
            var named = Asset("0x03_en(2004)");
            var rows = Plan(new[] { slot }, new[] { plain, named });
            var row = rows.Single(r => r.Slot != null);
            Equal(TransferStatus.Ready, row.Status, "named and plain: no conflict");
            Equal(named.AssetId, Bound(row), "named and plain: the one named for the language wins");
            var loser = rows.Single(r => r.Status == TransferStatus.NoMatch);
            Check(loser.Detail.Contains("named for this language"), "named and plain: the plain one says why it lost");
        }

        private static void OtherLanguageNeverUsed()
        {
            var slot = Slot("Act / Line", ("el", "0x04_el(0A92)"));
            var german = Asset("0x04_de(2005)");
            var rows = Plan(new[] { slot }, new[] { german });
            Check(rows.All(r => r.Slot == null), "a _de asset is never bound as English");
            Check(rows.Single().Detail.Contains("another language"), "a _de asset says why");
        }

        private static void TwoNamedIsAConflict()
        {
            var slot = Slot("Act / Line", ("el", "0x05_el(0A92)"));
            var rows = Plan(new[] { slot }, new[] { Asset("0x05_en(2006)", "EN/take1"), Asset("0x05_en(2007)", "EN/take2") });
            var row = rows.Single();
            Equal(TransferStatus.Conflict, row.Status, "two named assets: conflict");
            Equal(VoiceOverTransferCore.SkipChoice, row.Choice, "two named assets: starts skipped");
            Equal(3, row.Choices.Count, "two named assets: skip plus both offered");
        }

        private static void ExistingTargetBinding()
        {
            var asset = Asset("0x06_en(2008)");
            var same = Slot("Act / Same", ("el", "0x06_el(0A92)"));
            same.Existing["en"] = "0x06_en(2008)";
            same.ExistingAssets["en"] = asset.AssetId;
            Equal(TransferStatus.Unchanged, Plan(new[] { same }, new[] { asset }).Single().Status, "already bound to that asset: unchanged");

            var other = Slot("Act / Other", ("el", "0x06_el(0A92)"), ("en", "old_en(1111)"));
            var row = Plan(new[] { other }, new[] { asset }).Single();
            Equal(TransferStatus.Conflict, row.Status, "bound to another asset: conflict");
            Equal(VoiceOverTransferCore.KeepChoice, row.Choice, "bound to another asset: starts on keep current");
            Equal(0, VoiceOverTransferCore.BindingsToApply(new[] { row }).Count(), "keep current applies nothing");
        }

        private static void OneLineSeveralSlots()
        {
            var a = Slot("Act / A", ("el", "shared_el(0A92)"));
            var b = Slot("Act / B", ("el", "shared_el(0A92)"));
            var asset = Asset("shared_en(2009)");
            var rows = Plan(new[] { a, b }, new[] { asset });
            Equal(2, rows.Count(r => r.Status == TransferStatus.Ready), "one line, two slots: both bound");
            Check(rows.All(r => r.Detail.Contains("2 slots")), "one line, two slots: noted");
        }

        private static void SlotWithoutOtherLanguages()
        {
            var slot = Slot("Act / Only English", ("en", "0x07_en(0A92)"));
            var rows = Plan(new[] { slot }, new[] { Asset("0x07_en(2010)") });
            Check(rows.All(r => r.Slot == null), "a slot with no other language has no name to match");
        }

        private static void SlotsWithDifferentNames()
        {
            var slot = Slot("Act / Mixed", ("el", "line_el(0A92)"), ("de-DE", "zeile_de-DE(0A93)"));
            var rows = Plan(new[] { slot }, new[] { Asset("zeile_en(2011)") });
            var row = rows.Single(r => r.Slot != null);
            Equal(TransferStatus.Ready, row.Status, "matches through any other language's name");
            Check(row.Detail.Contains("different names"), "mixed names are pointed out");
        }

        private static void ShowsWhyAndBothNames()
        {
            var slot = Slot("Act / Line", ("el", "0x08_el(0A92)"));
            slot.ExistingExtension["el"] = ".mp3";
            slot.ExistingDisplay["el"] = "Act 1 / Πέτρος: Καλημέρα [0x08_el]";
            var named = Asset("0x08_en(2012)", "EN/takes");
            named.DisplayName = "Good morning take 3";
            var row = Plan(new[] { slot }, new[] { named }).Single();
            var detail = row.Detail;
            Check(detail.Contains("Matched on name: \"0x08\""), "detail: the shared name is shown");
            Check(detail.Contains("Existing [el]") && detail.Contains("file 0x08_el(0A92).mp3"), "detail: the existing file name is shown");
            Check(detail.Contains("articy name \"Act 1 / Πέτρος: Καλημέρα [0x08_el]\""), "detail: the existing articy name is shown");
            Check(detail.Contains("New [en]") && detail.Contains("file 0x08_en(2012).mp3"), "detail: the new file name is shown");
            Check(detail.Contains("articy name \"Good morning take 3\"") && detail.Contains("in EN/takes"), "detail: the new articy name and folder are shown");
            Check(detail.Contains("named for en"), "detail: why this file won");
            Check(row.Choice.StartsWith("0x08_en(2012).mp3"), "dropdown label starts with the file name");
            Console.WriteLine("sample preview row:\n  " + row.StatusText + "  " + row.Title + "\n  " + row.Detail.Replace("\n", "\n  ") + "\n  dropdown: " + row.Choice);

            var plainSlot = Slot("Act / Plain", ("el", "0x09_el(0A93)"));
            var plain = Asset("0x09(2013)");
            Check(Plan(new[] { plainSlot }, new[] { plain }).Single().Detail.Contains("no language suffix"), "detail: a plain file says so");

            var none = Slot("Act / NoName", ("el", "0x0A_el(0A94)"));
            Check(Plan(new[] { none }, new[] { Asset("0x0A_en(2014)") }).Single().Detail.Contains("articy name (none)"), "detail: a missing articy name shows as (none)");
        }

        private static void DisplayNamesNeverMatched()
        {
            var slot = Slot("Act / Line", ("el", "0x0B_el(0A95)"));
            slot.ExistingDisplay["el"] = "Greeting";
            var decoy = Asset("zzz_en(2015)");
            decoy.DisplayName = "0x0B_en";
            var rows = Plan(new[] { slot }, new[] { decoy });
            Check(rows.All(r => r.Slot == null), "an articy name that looks right never makes a match");
        }

        private static void ApplyRules()
        {
            var slot = new LanguageSlot();
            var choices = new Dictionary<string, ulong> { ["a"] = 1, ["b"] = 2 };
            var rows = new List<TransferRow>
            {
                new TransferRow { Status = TransferStatus.Ready, Slot = slot, ChoiceAssets = choices, Choice = "a", Selectable = true, Selected = true },
                new TransferRow { Status = TransferStatus.Ready, Slot = slot, ChoiceAssets = choices, Choice = "a", Selectable = true, Selected = false },
                new TransferRow { Status = TransferStatus.Conflict, Slot = slot, ChoiceAssets = choices, Choice = VoiceOverTransferCore.SkipChoice },
                new TransferRow { Status = TransferStatus.Conflict, Slot = slot, ChoiceAssets = choices, Choice = VoiceOverTransferCore.KeepChoice },
                new TransferRow { Status = TransferStatus.Conflict, Slot = slot, ChoiceAssets = choices, Choice = "b" },
                new TransferRow { Status = TransferStatus.Unchanged, Slot = slot, ChoiceAssets = choices, Choice = "a", Selectable = true, Selected = true },
            };
            Equal(2, VoiceOverTransferCore.BindingsToApply(rows).Count(), "only a ticked clean row and a picked conflict apply");
        }

        /// <summary>
        /// A synthetic project: 400 slots in many naming styles, and an "English" folder holding, per
        /// slot, a random mix of the right file (named or plain), a German file, a duplicate, or
        /// nothing, plus unrelated noise. Every binding must pair names with the same base, never use
        /// another language's file, and every slot with exactly one fitting file must be bound.
        /// </summary>
        private static void Synthetic()
        {
            var random = new Random(20261125);
            var slots = new List<LanguageSlot>();
            var assets = new List<CandidateAsset>();
            var expected = new Dictionary<ulong, ulong>();   // slot fragment -> the one fitting asset
            var styles = new Func<int, string>[]
            {
                i => $"0x{0x0100000000002000 + i:X16}",
                i => $"Harbour__SC_{i:000}",
                i => $"Σκηνή {i} (take 2)",
                i => $"line_{i}_final",
                i => $"Model_el_{i}",
            };

            for (int i = 0; i < 400; i++)
            {
                var name = styles[i % styles.Length](i);
                var source = random.Next(3) == 0 ? "de-DE" : "el";
                var storedSource = random.Next(4) == 0 ? name : $"{name}_{source}";
                var slot = Slot($"Act / {i}", (source, $"{storedSource}({0x0A00 + i:X4})"));
                slots.Add(slot);

                switch (random.Next(6))
                {
                    case 0: // named English file
                        var named = Asset($"{name}_en({0x3000 + i:X4})");
                        assets.Add(named);
                        expected[slot.FragmentId] = named.AssetId;
                        break;
                    case 1: // plain file only
                        var plain = Asset($"{name}({0x4000 + i:X4})");
                        assets.Add(plain);
                        expected[slot.FragmentId] = plain.AssetId;
                        break;
                    case 2: // plain and named: named wins
                        assets.Add(Asset($"{name}({0x4000 + i:X4})"));
                        var winner = Asset($"{name}_EN({0x3000 + i:X4})");
                        assets.Add(winner);
                        expected[slot.FragmentId] = winner.AssetId;
                        break;
                    case 3: // only a German file: must stay unbound
                        assets.Add(Asset($"{name}_de({0x5000 + i:X4})"));
                        break;
                    case 4: // two English takes: conflict
                        assets.Add(Asset($"{name}_en({0x3000 + i:X4})", "EN/a"));
                        assets.Add(Asset($"{name}_en({0x6000 + i:X4})", "EN/b"));
                        break;
                    default: // nothing for this slot
                        break;
                }
            }
            for (int n = 0; n < 40; n++)
                assets.Add(Asset($"unrelated_{n}_en({0x7000 + n:X4})"));

            var rows = Plan(slots, assets);
            var byId = assets.ToDictionary(a => a.AssetId);

            foreach (var row in rows.Where(r => r.Slot != null && r.Status == TransferStatus.Ready))
            {
                var asset = byId[Bound(row)];
                var slotBase = VoiceOverTransferCore.LanguageBase(row.Slot.Existing.Values.First(), sCultures).Base;
                var (assetBase, assetCulture) = VoiceOverTransferCore.LanguageBase(asset.StoredName, sCultures);
                Check(string.Equals(slotBase, assetBase, StringComparison.OrdinalIgnoreCase), $"synthetic: {row.Title} bound to a different name {asset.StoredName}");
                Check(assetCulture == null || assetCulture == "en", $"synthetic: {row.Title} bound to another language's file {asset.StoredName}");
            }

            var ready = rows.Where(r => r.Status == TransferStatus.Ready).ToDictionary(r => r.Slot.FragmentId, Bound);
            int missed = expected.Count(e => !ready.TryGetValue(e.Key, out var got) || got != e.Value);
            Equal(0, missed, "synthetic: every slot with one fitting file is bound to it");
            Equal(expected.Count, ready.Count, "synthetic: nothing else is bound");
            Check(rows.Count(r => r.Status == TransferStatus.Conflict) > 0, "synthetic: duplicate takes became conflicts");
            Check(rows.Where(r => r.Status == TransferStatus.NoMatch).All(r => !string.IsNullOrEmpty(r.Detail)), "synthetic: every unused asset says why");
            Console.WriteLine($"language matching: {slots.Count} slots, {assets.Count} assets, {ready.Count} bound, {rows.Count(r => r.Status == TransferStatus.Conflict)} conflicts");
        }
    }
}
