using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.EditorTools.OpeningBook;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Guards the one book failure that nothing else can see: the compiled asset the game
    /// actually ships drifting away from the source text a contributor edits.
    ///
    /// Compiling a book is a manual editor menu command, and nothing reruns it when the source
    /// file changes. So editing openings.book.txt and forgetting to recompile leaves the old
    /// asset in place — the game keeps playing the old lines, every other test still passes
    /// (they all compile their own book from inline text), and the mistake is invisible until
    /// someone notices the AI never plays the new theory. These tests read the two real files
    /// and compare them.
    /// </summary>
    [TestFixture]
    public class ShippedOpeningBookTests
    {
        // Taken from the builder rather than restated here, so the test can only ever check the
        // same two files a rebuild actually writes.
        private const string SourcePath = OpeningBookBuilder.DefaultSourcePath;
        private const string CompiledAssetPath = OpeningBookBuilder.DefaultAssetPath;

        private const string RecompileHint =
            "Rebuild it with the 'Chess: The Betrayal/AI/Rebuild Opening Book' menu command, and " +
            "commit the regenerated asset alongside the source change.";

        private static string ReadSourceText() =>
            File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, SourcePath));

        private static OpeningBookAsset LoadShippedAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OpeningBookAsset>(CompiledAssetPath);
            Assert.That(asset, Is.Not.Null, $"No compiled opening book found at '{CompiledAssetPath}'.");
            return asset;
        }

        [Test]
        public void ShippedAsset_MatchesItsSourceText_EntryForEntry()
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile(ReadSourceText());
            OpeningBookAsset shipped = LoadShippedAsset();

            Assert.That(shipped.EntryCount, Is.EqualTo(keys.Length),
                $"The shipped opening book has {shipped.EntryCount} entries but its source text compiles to " +
                $"{keys.Length} — the asset is stale. {RecompileHint}");

            Assert.That(shipped.SchemeVersion, Is.EqualTo(schemeVersion),
                $"The shipped book was compiled against a different Zobrist key scheme. {RecompileHint}");

            for (int i = 0; i < keys.Length; i++)
            {
                Assert.That(shipped.KeyAt(i), Is.EqualTo(keys[i]), $"Key mismatch at entry {i}. {RecompileHint}");
                Assert.That(shipped.PackedMoveAt(i), Is.EqualTo(packedMoves[i]), $"Move mismatch at entry {i}. {RecompileHint}");
                Assert.That(shipped.WeightAt(i), Is.EqualTo(weights[i]), $"Weight mismatch at entry {i}. {RecompileHint}");
            }
        }

        [Test]
        public void ShippedAsset_SchemeVersion_MatchesCurrentZobristScheme()
        {
            // A scheme mismatch is not a crash — the lookup quietly declines every position, so the
            // AI silently stops playing book moves entirely while looking perfectly healthy. Worth
            // its own test because it can break without the source text changing at all: any change
            // to the Zobrist key tables invalidates every hash already baked into the asset.
            OpeningBookAsset shipped = LoadShippedAsset();

            Assert.That(shipped.SchemeVersion, Is.EqualTo(BoardState.ZobristSchemeVersion),
                "The shipped opening book's hashes were built from a different set of Zobrist keys than the " +
                $"engine now computes, so every lookup will silently miss. {RecompileHint}");
        }

        [Test]
        public void ShippedAsset_KeysSortedAndEntriesUnique_SoLookupCanBinarySearch()
        {
            // The lookup binary-searches the keys and then widens to the run sharing that hash.
            // Unsorted keys don't fail loudly — they just make positions unfindable, which reads as
            // "the book doesn't know this line" rather than as a bug. Checked on the shipped asset
            // specifically, since that is the artifact the game loads.
            OpeningBookAsset shipped = LoadShippedAsset();
            var seen = new HashSet<(ulong Key, uint Move)>();

            for (int i = 0; i < shipped.EntryCount; i++)
            {
                if (i > 0)
                {
                    Assert.That(shipped.KeyAt(i), Is.GreaterThanOrEqualTo(shipped.KeyAt(i - 1)),
                        $"Keys must be non-decreasing for binary search, but entry {i} is smaller than entry {i - 1}.");
                }

                Assert.That(seen.Add((shipped.KeyAt(i), shipped.PackedMoveAt(i))), Is.True,
                    $"Entry {i} repeats a (position, move) pair that already appears earlier — the compiler merges " +
                    "these, so a duplicate means the asset was not produced by the current compiler.");

                Assert.That(shipped.WeightAt(i), Is.GreaterThan(0),
                    $"Entry {i} has weight 0, which would make it unpickable in the weighted draw.");
            }
        }

        [Test]
        public void SourceBook_Coverage_MeetsTheRecordedFloor()
        {
            // A ratchet, not a target. Expanding the book is the point of this work, and a set of
            // new lines that only ever transposes into positions the book already knew would add
            // no entries at all while looking like real coverage in the diff. Raise the floor
            // whenever families are added, so that no-op case has to be noticed and explained.
            const int MinimumEntries = 3506;
            const int MinimumVariations = 324;

            string sourceText = ReadSourceText();
            var (keys, _, _, _) = OpeningBookCompiler.Compile(sourceText);

            // Written through TestContext rather than Console so the coverage summary is captured
            // in the test results file, where a headless run can actually read it back.
            TestContext.Out.WriteLine(BuildCoverageReport(sourceText, keys.Length));

            Assert.That(keys.Length, Is.GreaterThanOrEqualTo(MinimumEntries),
                $"The book compiles to {keys.Length} positions, below the recorded floor of {MinimumEntries}.");
            Assert.That(CountVariations(sourceText), Is.GreaterThanOrEqualTo(MinimumVariations),
                $"The book has fewer variations than the recorded floor of {MinimumVariations}.");
        }

        private static int CountVariations(string sourceText)
        {
            int count = 0;
            foreach (string rawLine in sourceText.Replace("\r\n", "\n").Split('\n'))
            {
                if (OpeningBookLine.Parse(rawLine, sourceLineNumber: 1) != null)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Summarises what the book covers, grouped by the comment heading each block of lines
        /// sits under, so a run of this fixture records the coverage rather than just asserting a
        /// number. Per-family position counts are compiled in isolation and therefore overlap —
        /// families sharing an opening move each count the positions they share.
        /// </summary>
        private static string BuildCoverageReport(string sourceText, int totalEntries)
        {
            var report = new StringBuilder();
            report.AppendLine($"Opening book coverage: {totalEntries} positions, {CountVariations(sourceText)} variations.");

            string family = "(unlabelled)";
            var familyLines = new List<string>();

            void FlushFamily()
            {
                if (familyLines.Count == 0) return;
                var (familyKeys, _, _, _) = OpeningBookCompiler.Compile(string.Join("\n", familyLines));
                report.AppendLine($"  {family}: {familyLines.Count} variation(s), {familyKeys.Length} position(s).");
                familyLines.Clear();
            }

            foreach (string rawLine in sourceText.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("#"))
                {
                    // A heading only starts a new family once the previous one has lines, so the
                    // multi-line explanatory comment at the top of the file doesn't split anything.
                    if (familyLines.Count > 0) FlushFamily();
                    family = trimmed.TrimStart('#').Trim();
                    continue;
                }

                if (OpeningBookLine.Parse(rawLine, sourceLineNumber: 1) != null)
                    familyLines.Add(trimmed);
            }

            FlushFamily();
            return report.ToString().TrimEnd();
        }
    }
}
