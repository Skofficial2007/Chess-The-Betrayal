using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.EditorTools.OpeningBook;

namespace ChessTheBetrayal.Tests.EditMode.AI.OpeningBook
{
    /// <summary>
    /// Guards the trap book the game actually ships against drifting from the source text a
    /// contributor edits. Compiling is a manual menu command and nothing reruns it when the source
    /// changes, so editing the file and forgetting to recompile leaves the old asset in place with
    /// nothing to say so — the same failure the opening book has, and just as quiet.
    /// </summary>
    [TestFixture]
    public class ShippedTrapBookTests
    {
        private const string SourcePath = TrapBookBuilder.DefaultSourcePath;
        private const string CompiledAssetPath = TrapBookBuilder.DefaultAssetPath;

        private const string RecompileHint =
            "Rebuild it with the 'Chess: The Betrayal/AI/Rebuild Trap Book' menu command, and " +
            "commit the regenerated asset alongside the source change.";

        private static string ReadSourceText() =>
            File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, SourcePath));

        private static TrapBookAsset LoadShippedAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TrapBookAsset>(CompiledAssetPath);
            Assert.That(asset, Is.Not.Null, $"No compiled trap book found at '{CompiledAssetPath}'.");
            return asset;
        }

        [Test]
        public void ShippedAsset_MatchesItsSourceText_EntryForEntry()
        {
            var (keys, blunders, bests, names, schemeVersion) = TrapBookCompiler.Compile(ReadSourceText());
            TrapBookAsset shipped = LoadShippedAsset();

            Assert.That(shipped.EntryCount, Is.EqualTo(keys.Length),
                $"The shipped trap book has {shipped.EntryCount} traps but its source compiles to " +
                $"{keys.Length} — the asset is stale. {RecompileHint}");

            Assert.That(shipped.SchemeVersion, Is.EqualTo(schemeVersion),
                $"The shipped trap book was compiled against a different Zobrist key scheme. {RecompileHint}");

            for (int i = 0; i < keys.Length; i++)
            {
                Assert.That(shipped.PositionKeyAt(i), Is.EqualTo(keys[i]), $"Key mismatch at trap {i}. {RecompileHint}");
                Assert.That(shipped.BlunderMoveAt(i), Is.EqualTo(blunders[i]), $"Losing move mismatch at trap {i}. {RecompileHint}");
                Assert.That(shipped.BestMoveAt(i), Is.EqualTo(bests[i]), $"Reply mismatch at trap {i}. {RecompileHint}");
                Assert.That(shipped.NameAt(i), Is.EqualTo(names[i]), $"Name mismatch at trap {i}. {RecompileHint}");
            }
        }

        [Test]
        public void ShippedAsset_SchemeVersion_MatchesCurrentZobristScheme()
        {
            // A scheme mismatch is not a crash — every lookup quietly misses, so the trap book
            // silently stops knowing anything while looking perfectly healthy. It can break without
            // the source changing at all, since any change to the Zobrist tables invalidates every
            // hash already baked into the asset.
            Assert.That(LoadShippedAsset().SchemeVersion, Is.EqualTo(BoardState.ZobristSchemeVersion),
                "The shipped trap book's hashes were built from a different set of Zobrist keys than the " +
                $"engine now computes, so every lookup will silently miss. {RecompileHint}");
        }

        [Test]
        public void ShippedAsset_KeysSortedAndUnique_AndEveryTrapNamed()
        {
            TrapBookAsset shipped = LoadShippedAsset();
            var seen = new HashSet<ulong>();

            for (int i = 0; i < shipped.EntryCount; i++)
            {
                if (i > 0)
                {
                    Assert.That(shipped.PositionKeyAt(i), Is.GreaterThan(shipped.PositionKeyAt(i - 1)),
                        $"Keys must be strictly increasing for binary search, but trap {i} is not greater than {i - 1}.");
                }

                Assert.That(seen.Add(shipped.PositionKeyAt(i)), Is.True,
                    $"Trap {i} repeats a position that already appears earlier — the compiler merges those, " +
                    "so a duplicate means the asset was not produced by the current compiler.");

                Assert.That(shipped.NameAt(i), Is.Not.Null.And.Not.Empty,
                    $"Trap {i} has no name, so nothing could tell a player which trap it is.");
                Assert.That(shipped.BlunderMoveAt(i), Is.Not.EqualTo(shipped.BestMoveAt(i)),
                    $"Trap {i} recommends the move it also says loses.");
            }
        }

        [Test]
        public void SourceBook_TrapCount_MeetsTheRecordedFloor()
        {
            // A ratchet, not a target. Records that only ever restate positions already covered
            // would add nothing while looking like real coverage in the diff, so the floor has to
            // be raised deliberately whenever traps are added.
            const int MinimumTraps = 48;

            var (keys, _, _, names, _) = TrapBookCompiler.Compile(ReadSourceText());

            TestContext.Out.WriteLine($"Trap book covers {keys.Length} position(s):");
            foreach (string name in names) TestContext.Out.WriteLine("  " + name);

            Assert.That(keys.Length, Is.GreaterThanOrEqualTo(MinimumTraps),
                $"The trap book compiles to {keys.Length} traps, below the recorded floor of {MinimumTraps}.");
        }
    }
}
