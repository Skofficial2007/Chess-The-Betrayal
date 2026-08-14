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
    /// Checks that the opening book never recommends a move the trap book records as losing.
    ///
    /// This is the one failure the other book fixtures cannot see. A book line is stored as one
    /// entry per ply, keyed by the position before the move, with no record of which side the line
    /// was written for — so a line that includes a mistake teaches that mistake as a move to play,
    /// exactly as confidently as it teaches everything else. The result is worse than an ordinary
    /// bad line, because a book move is played instantly with no search: the position where the AI
    /// most needs to think is the one position it is guaranteed not to.
    ///
    /// This has already happened once. A researched line ending in a smothered mate was caught by
    /// hand before it shipped, and it would have given the AI a coin flip between a sound move and
    /// being mated in two. That catch was luck. This makes it a guarantee.
    /// </summary>
    [TestFixture]
    public class OpeningBookTrapSafetyTests
    {
        private static OpeningBookAsset LoadOpeningBook()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OpeningBookAsset>(OpeningBookBuilder.DefaultAssetPath);
            Assert.That(asset, Is.Not.Null, $"No compiled opening book at '{OpeningBookBuilder.DefaultAssetPath}'.");
            return asset;
        }

        private static TrapBookAsset LoadTrapBook()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TrapBookAsset>(TrapBookBuilder.DefaultAssetPath);
            Assert.That(asset, Is.Not.Null, $"No compiled trap book at '{TrapBookBuilder.DefaultAssetPath}'.");
            return asset;
        }

        /// <summary>
        /// Finds every move the book offers for a position, as a set of packed moves.
        /// Entries sharing a hash sit in one contiguous run because the compiler sorts by key.
        /// </summary>
        private static List<(uint Move, ushort Weight)> BookMovesFor(
            ulong[] keys, uint[] moves, ushort[] weights, ulong position)
        {
            var found = new List<(uint, ushort)>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == position) found.Add((moves[i], weights[i]));
            }

            return found;
        }

        /// <summary>
        /// The check itself, run against arbitrary compiled book arrays so it can be pointed at a
        /// deliberately broken book to prove it works.
        /// </summary>
        private static List<string> FindTrapMovesInBook(
            ulong[] keys, uint[] moves, ushort[] weights, TrapBookAsset traps)
        {
            var violations = new List<string>();

            for (int t = 0; t < traps.EntryCount; t++)
            {
                ulong position = traps.PositionKeyAt(t);
                uint losing = traps.BlunderMoveAt(t);

                foreach (var (move, weight) in BookMovesFor(keys, moves, weights, position))
                {
                    if (move != losing) continue;

                    violations.Add(
                        $"The opening book plays the losing move of '{traps.NameAt(t)}' (weight {weight}). " +
                        "Remove or shorten the line that reaches it — a book move is played with no search, " +
                        "so nothing downstream can rescue this.");
                }
            }

            return violations;
        }

        [Test]
        public void ShippedOpeningBook_NeverPlaysAMoveTheTrapBookRecordsAsLosing()
        {
            OpeningBookAsset book = LoadOpeningBook();
            TrapBookAsset traps = LoadTrapBook();

            Assert.That(book.SchemeVersion, Is.EqualTo(traps.SchemeVersion),
                "The two books were compiled against different Zobrist key schemes, so their positions " +
                "cannot be compared at all and this check would silently pass on everything.");

            var keys = new ulong[book.EntryCount];
            var moves = new uint[book.EntryCount];
            var weights = new ushort[book.EntryCount];
            for (int i = 0; i < book.EntryCount; i++)
            {
                keys[i] = book.KeyAt(i);
                moves[i] = book.PackedMoveAt(i);
                weights[i] = book.WeightAt(i);
            }

            var violations = FindTrapMovesInBook(keys, moves, weights, traps);
            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void ShippedBooks_Overlap_IsReported()
        {
            // How much of the trap book the opening book can actually reach. A trap in a position
            // the book never visits is still worth recording, but it is this overlap that says how
            // much work the check above is really doing, and it would otherwise look identical to
            // a check that passes because it compares nothing.
            OpeningBookAsset book = LoadOpeningBook();
            TrapBookAsset traps = LoadTrapBook();

            var report = new StringBuilder();
            int reached = 0, playsRecommended = 0;

            for (int t = 0; t < traps.EntryCount; t++)
            {
                var keys = new ulong[book.EntryCount];
                var moves = new uint[book.EntryCount];
                var weights = new ushort[book.EntryCount];
                for (int i = 0; i < book.EntryCount; i++)
                {
                    keys[i] = book.KeyAt(i);
                    moves[i] = book.PackedMoveAt(i);
                    weights[i] = book.WeightAt(i);
                }

                var offered = BookMovesFor(keys, moves, weights, traps.PositionKeyAt(t));
                if (offered.Count == 0) continue;

                reached++;
                bool recommended = false;
                foreach (var (move, _) in offered)
                {
                    if (move == traps.BestMoveAt(t)) recommended = true;
                }

                if (recommended) playsRecommended++;
                report.AppendLine(
                    $"  reached: {traps.NameAt(t)} — book offers {offered.Count} move(s), " +
                    $"{(recommended ? "including" : "not including")} the recommended reply.");
            }

            TestContext.Out.WriteLine(
                $"Opening book reaches {reached} of {traps.EntryCount} trap positions; " +
                $"{playsRecommended} of those also play the recommended reply.");
            TestContext.Out.WriteLine(report.ToString().TrimEnd());

            Assert.That(traps.EntryCount, Is.GreaterThan(0), "There are no traps to check against.");
        }

        [Test]
        public void TheCheck_DetectsABookThatPlaysATrapMove()
        {
            // Proves the guard above can fail. A check that silently compares nothing looks exactly
            // like a check that passes, and this one is only ever exercised by data that is already
            // correct — so the failing case has to be constructed deliberately.
            //
            // The line below is the Legal Trap played to its losing move: a real book line, in
            // real notation, that a researcher could plausibly hand over, ending in a queen grab
            // that is mated in two.
            const string bookThatWalksIntoATrap =
                "e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5 h5d1 | w=8";

            var (keys, moves, weights, _) = OpeningBookCompiler.Compile(bookThatWalksIntoATrap);
            TrapBookAsset traps = LoadTrapBook();

            var violations = FindTrapMovesInBook(keys, moves, weights, traps);

            Assert.That(violations, Is.Not.Empty,
                "A book playing the losing move of a recorded trap was not detected, so the guard " +
                "above proves nothing.");
            Assert.That(violations[0], Does.Contain("Legal").Or.Contain("Légal"));
        }
    }
}
