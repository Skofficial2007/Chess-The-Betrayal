using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Plays the shipped opening book the way a real match does — from the standard starting
    /// position, one lookup per turn, following whatever it answers until it runs out of theory.
    ///
    /// The other book fixtures compile their own book from inline text, which proves the compiler
    /// and the lookup work but says nothing about the book the game actually ships. These tests
    /// use the real asset, so they describe real behaviour: how far the AI plays from memory
    /// before it has to think, that it hands back to the search cleanly when theory ends, and
    /// that the book offers genuine variety rather than one line on repeat.
    ///
    /// No search runs here at all. A book hit is a binary search plus a legal-move check, so this
    /// whole fixture is effectively instant.
    /// </summary>
    [TestFixture]
    public class OpeningBookTheoryWalkTests
    {
        private const string CompiledAssetPath = "Assets/AI/Opening Book/OpeningBook.asset";

        // Far beyond the longest line the book could hold, so a walk that somehow never ends fails
        // as a bounded assertion instead of hanging the test run.
        private const int WalkSafetyLimit = 200;

        private ChessEngineAdapter _engine;
        private OpeningBookAsset _book;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _book = AssetDatabase.LoadAssetAtPath<OpeningBookAsset>(CompiledAssetPath);
            Assert.That(_book, Is.Not.Null, $"No compiled opening book found at '{CompiledAssetPath}'.");
        }

        /// <summary>
        /// Follows the book from the starting position until it declines to answer, returning the
        /// moves played in coordinate notation. Applies each move exactly the way the compiler and
        /// a real match do — the engine applies it, then the turn flips separately — because
        /// ApplyMove does not flip the turn on its own.
        /// </summary>
        private List<string> WalkBookLine(IRandomSource rng)
        {
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            var played = new List<string>();
            var legalMoves = new List<MoveCommand>();

            for (int ply = 0; ply < WalkSafetyLimit; ply++)
            {
                MoveCommand? bookMove = OpeningBookLookup.TryGetBookMove(_book, board, _engine, rng);
                if (bookMove == null)
                    return played;

                MoveCommand move = bookMove.Value;

                // The lookup already re-validates against the legal move list before returning, so
                // this is a second, independent check that what came back is playable in the
                // position actually on the board — the property that matters to a real match.
                legalMoves.Clear();
                _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
                Assert.That(legalMoves.Any(m => AlphaBetaSearch.PackMove(m) == AlphaBetaSearch.PackMove(move)), Is.True,
                    $"The book returned {ToToken(move)} at ply {ply + 1}, which is not legal in that position.");
                Assert.That(move.Stage, Is.EqualTo(BetrayalStage.None),
                    $"The book returned a Betrayal move ({ToToken(move)}) at ply {ply + 1}; it only covers ordinary theory.");

                _engine.ApplyMove(board, move);
                if (BetrayalStageRules.FlipsTurn(move.Stage))
                    board.NextTurn();
                board.AssertZobristConsistency();

                played.Add(ToToken(move));
            }

            Assert.Fail($"The book never stopped answering within {WalkSafetyLimit} plies.");
            return played;
        }

        private static string ToToken(MoveCommand move)
        {
            string square(Vector2Int v) => $"{(char)('a' + v.x)}{v.y + 1}";
            string promotion = move.PromotedTo switch
            {
                ChessPieceType.Queen => "q",
                ChessPieceType.Rook => "r",
                ChessPieceType.Bishop => "b",
                ChessPieceType.Knight => "n",
                _ => ""
            };
            return $"{square(move.StartPosition)}{square(move.EndPosition)}{promotion}";
        }

        [Test]
        public void ShippedBook_FromStartingPosition_PlaysTheoryWithoutSearching()
        {
            List<string> line = WalkBookLine(new SystemRandomSource(seed: 20260728));

            TestContext.Out.WriteLine($"Book line followed: {string.Join(" ", line)} ({line.Count} plies)");

            // Both colours must be answered, otherwise the book is only usable when the AI happens
            // to move first — a book that only knows White's moves would still pass a single-lookup
            // test while being half as useful in a real match.
            Assert.That(line.Count, Is.GreaterThanOrEqualTo(2),
                "The book should answer at least an opening move and a reply before running out of theory.");
        }

        [Test]
        public void ShippedBook_WhenTheoryRunsOut_DeclinesSoTheSearchTakesOver()
        {
            // The handover is what makes the book safe to extend: the AI is only ever skipping the
            // search while it genuinely knows the position, and the moment it doesn't, the lookup
            // returns nothing and the normal search runs. Walking to the end of a line and asking
            // once more is the direct test of that.
            var rng = new SystemRandomSource(seed: 4242);
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            int pliesFollowed = 0;
            while (pliesFollowed < WalkSafetyLimit)
            {
                MoveCommand? bookMove = OpeningBookLookup.TryGetBookMove(_book, board, _engine, rng);
                if (bookMove == null) break;

                _engine.ApplyMove(board, bookMove.Value);
                if (BetrayalStageRules.FlipsTurn(bookMove.Value.Stage))
                    board.NextTurn();
                pliesFollowed++;
            }

            Assert.That(pliesFollowed, Is.LessThan(WalkSafetyLimit), "The book never ran out of theory.");
            Assert.That(OpeningBookLookup.TryGetBookMove(_book, board, _engine, rng), Is.Null,
                "Once theory ends the book must keep declining, so the search reliably takes over.");
        }

        [Test]
        public void ShippedBook_AcrossManySeeds_OffersMoreThanOneOpening()
        {
            // A book whose weights all collapsed onto one entry would still return legal theory
            // every time, so every other test here would pass while the AI played the identical
            // opening in every single game. Sampling many seeds is the only way that shows up.
            var distinctFirstMoves = new HashSet<string>();
            var distinctLines = new HashSet<string>();

            for (int seed = 0; seed < 50; seed++)
            {
                List<string> line = WalkBookLine(new SystemRandomSource(seed));
                if (line.Count == 0) continue;
                distinctFirstMoves.Add(line[0]);
                distinctLines.Add(string.Join(" ", line));
            }

            TestContext.Out.WriteLine(
                $"Across 50 seeds: {distinctFirstMoves.Count} distinct first move(s) " +
                $"({string.Join(", ", distinctFirstMoves.OrderBy(m => m))}), {distinctLines.Count} distinct line(s).");

            Assert.That(distinctFirstMoves.Count, Is.GreaterThan(1),
                "The book always opened with the same move across 50 seeds — the weighted pick is not offering variety.");
        }

        [Test]
        public void ShippedBook_SameSeed_ReplaysTheSameLine()
        {
            // Book choice is random, so a reported bug in an AI game is only reproducible if the
            // same seed replays the same opening. Worth pinning because it is a property of the
            // lookup consuming the random source in a fixed order, which is easy to break.
            List<string> first = WalkBookLine(new SystemRandomSource(seed: 777));
            List<string> second = WalkBookLine(new SystemRandomSource(seed: 777));

            Assert.That(second, Is.EqualTo(first), "The same seed must always produce the same book line.");
        }
    }
}
