using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Kurisu.VoiceOverTools.Core;

namespace Kurisu.VoiceOverTools.Tests
{
    /// <summary>
    /// Console test runner for VoiceOverTransferCore. Unit cases pin each naming and matching rule;
    /// the synthetic round trip exports hundreds of deliberately awkward voice-overs to a temp folder
    /// under every option combination and checks that importing the result back finds every file
    /// unchanged. Exit code is the number of failures.
    /// </summary>
    internal static class Program
    {
        private static int sPassed;
        private static readonly List<string> sFailures = new List<string>();
        private static string sTemp;

        private static int Main()
        {
            sTemp = Path.Combine(Path.GetTempPath(), "Kurisu.VoiceOverTools.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sTemp);
            try
            {
                NameTests.Run();
                ExportTests.Run();
                ImportTests.Run();
                LanguageTests.Run();
                RoundTripTests.Run();
            }
            catch (Exception ex)
            {
                sFailures.Add("CRASH: " + ex);
            }
            finally
            {
                try { ClearReadOnly(sTemp); Directory.Delete(sTemp, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine($"{sPassed} passed, {sFailures.Count} failed");
            foreach (var failure in sFailures)
                Console.WriteLine("FAIL " + failure);
            return sFailures.Count;
        }

        internal static string Temp(string aName)
        {
            var path = Path.Combine(sTemp, aName);
            Directory.CreateDirectory(path);
            return path;
        }

        internal static void Check(bool aCondition, string aWhat)
        {
            if (aCondition) sPassed++;
            else sFailures.Add(aWhat);
        }

        internal static void Equal<T>(T aExpected, T aActual, string aWhat)
        {
            Check(EqualityComparer<T>.Default.Equals(aExpected, aActual), $"{aWhat}: expected [{aExpected}], got [{aActual}]");
        }

        internal static bool SameContent(string aA, string aB)
        {
            if (aB == null || !File.Exists(aA) || !File.Exists(aB)) return false;
            using var sha = SHA256.Create();
            return sha.ComputeHash(File.ReadAllBytes(aA)).SequenceEqual(sha.ComputeHash(File.ReadAllBytes(aB)));
        }

        private static void ClearReadOnly(string aFolder)
        {
            foreach (var file in Directory.EnumerateFiles(aFolder, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    /// <summary>Builds VoRecords with real files behind them.</summary>
    internal sealed class Store
    {
        private readonly string mFolder;
        private ulong mNextFragment = 0x0100000000000200;
        private ulong mNextAsset = 1;
        private int mNextTag = 0x0A92;

        public Store(string aFolder)
        {
            mFolder = aFolder;
        }

        /// <summary>A stored file named like articy does, "base(TAG).ext", with distinct content.</summary>
        public VoRecord Add(string aBase, string aCulture, string aExtension = ".mp3", string aSpeaker = "Vaggelis",
            string[] aHierarchy = null, ulong? aFragment = null, string aExplicitTag = null)
        {
            var tag = aExplicitTag ?? (mNextTag++).ToString("X4");
            var path = Path.Combine(mFolder, $"{aBase}({tag}){aExtension}");
            File.WriteAllText(path, "audio:" + path);
            return new VoRecord
            {
                AssetId = mNextAsset++,
                FragmentId = aFragment ?? mNextFragment++,
                Culture = aCulture,
                SourcePath = path,
                Speaker = aSpeaker,
                FragmentPath = string.Join(" / ", aHierarchy ?? Array.Empty<string>()),
                Hierarchy = aHierarchy ?? Array.Empty<string>(),
            };
        }

        /// <summary>The same stored asset bound to another fragment.</summary>
        public VoRecord Bind(VoRecord aAsset, ulong aFragment, string[] aHierarchy = null)
        {
            return new VoRecord
            {
                AssetId = aAsset.AssetId,
                FragmentId = aFragment,
                Culture = aAsset.Culture,
                SourcePath = aAsset.SourcePath,
                Speaker = aAsset.Speaker,
                FragmentPath = string.Join(" / ", aHierarchy ?? aAsset.Hierarchy.ToArray()),
                Hierarchy = aHierarchy ?? aAsset.Hierarchy,
            };
        }
    }
}
