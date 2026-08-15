using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.AI.OpeningBook
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
        private const string CompiledAssetPath = OpeningBookBuilder.DefaultAssetPath;

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

        /// <summary>A tier that plays the book for as long as the book has an answer — the shape
        /// the walk had before difficulty tiers could cut it short.</summary>
        private static readonly AIProfile UnlimitedBook = new AIProfile(
            "walk", maxDepth: 5, timeBudget: new AITimeBudget(1000, 1500), blunderRate: 0f,
            blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0,
            useOpeningBook: true, openingBookDepthPlies: 0);

        private List<string> WalkBookLine(IRandomSource rng) => WalkBookLine(rng, UnlimitedBook);

        /// <summary>
        /// Follows the book from the starting position until it declines to answer, returning the
        /// moves played in coordinate notation. Applies each move exactly the way the compiler and
        /// a real match do — the engine applies it, then the turn flips separately — because
        /// ApplyMove does not flip the turn on its own.
        ///
        /// Asks OpeningBookPolicy before every lookup, in the same order the live agent does, so a
        /// tier that is only allowed a few moves of theory stops here exactly where it stops in a
        /// real game.
        /// </summary>
        private List<string> WalkBookLine(IRandomSource rng, AIProfile profile)
        {
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            var played = new List<string>();
            var legalMoves = new List<MoveCommand>();

            for (int ply = 0; ply < WalkSafetyLimit; ply++)
            {
                if (!OpeningBookPolicy.ShouldConsult(profile, board))
                    return played;

                MoveCommand? bookMove = OpeningBookLookup.TryGetBookMove(_book, board, _engine, rng);
                if (bookMove == null)
                    return played;

                MoveCommand move = bookMove.Value;

                // The lookup already re-validates against the legal move list before returning, so
                // this is a second, independent check that what came back is playable in the
                // position actually on the board — the property that matters to a real match.
                legalMoves.Clear();
                _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
                Assert.That(legalMoves.Any(m => PackedMove.Pack(m) == PackedMove.Pack(move)), Is.True,
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
        public void ShippedBook_EachTier_StopsExactlyAtItsOwnAllowance()
        {
            const int Seed = 20260728;
            int uncapped = WalkBookLine(new SystemRandomSource(Seed)).Count;

            foreach (AIProfile tier in AIProfileTable.BuiltIn)
            {
                int expected = tier.OpeningBookDepthPlies <= 0
                    ? uncapped
                    : System.Math.Min(tier.OpeningBookDepthPlies, uncapped);

                int actual = WalkBookLine(new SystemRandomSource(Seed), tier).Count;

                TestContext.Out.WriteLine(
                    $"{tier.Id}: allowance {(tier.OpeningBookDepthPlies == 0 ? "whole book" : tier.OpeningBookDepthPlies + " plies")} " +
                    $"-> played {actual} plies of theory (uncapped walk is {uncapped}).");

                Assert.That(actual, Is.EqualTo(expected),
                    $"'{tier.Id}' should stop after {expected} plies of theory but played {actual}.");
            }
        }

        [Test]
        public void ShippedBook_EveryTierAllowance_IsOneRealOpeningsActuallyReach()
        {
            // An allowance longer than the openings the book offers would be a setting that never
            // does anything: it reads as a deliberate difficulty choice in the table while changing
            // nothing at all. Openings vary in length, so an allowance is not expected to cut every
            // one of them short — the check is that it cuts most of them short, which is what
            // separates a live difficulty setting from a decorative number.
            const int SeedCount = 40;

            foreach (AIProfile tier in AIProfileTable.BuiltIn)
            {
                if (tier.OpeningBookDepthPlies <= 0) continue;

                int shortened = 0;
                for (int seed = 0; seed < SeedCount; seed++)
                {
                    if (WalkBookLine(new SystemRandomSource(seed)).Count > tier.OpeningBookDepthPlies)
                        shortened++;
                }

                TestContext.Out.WriteLine(
                    $"{tier.Id}: {tier.OpeningBookDepthPlies}-ply allowance shortens {shortened} of {SeedCount} openings.");

                Assert.That(shortened, Is.GreaterThanOrEqualTo(SeedCount / 2),
                    $"'{tier.Id}' allows {tier.OpeningBookDepthPlies} plies of theory, which only shortened " +
                    $"{shortened} of {SeedCount} openings — that allowance is barely doing anything.");
            }
        }

        [Test]
        public void ShippedBook_TheLadder_PlaysSteadilyMoreTheoryAsItGetsHarder()
        {
            // The allowances only read as a difficulty ladder if they are ordered like one. This is
            // the property a future retune is most likely to break by editing a single row.
            const int Seed = 4242;

            int Theory(string id)
            {
                foreach (AIProfile tier in AIProfileTable.BuiltIn)
                {
                    if (tier.Id == id) return WalkBookLine(new SystemRandomSource(Seed), tier).Count;
                }

                Assert.Fail($"No '{id}' row in the built-in roster.");
                return 0;
            }

            int easy = Theory("easy"), normal = Theory("normal"), aggressive = Theory("aggressive");
            int hard = Theory("hard"), extreme = Theory("extreme"), impossible = Theory("impossible");

            Assert.That(easy, Is.LessThan(normal), "easy should play less theory than normal.");
            Assert.That(normal, Is.LessThan(aggressive), "normal should play less theory than aggressive.");
            Assert.That(aggressive, Is.LessThan(hard), "aggressive should play less theory than hard.");
            Assert.That(hard, Is.LessThanOrEqualTo(extreme), "hard should never out-read extreme.");
            Assert.That(extreme, Is.EqualTo(impossible), "the top two tiers both get the whole book.");
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
