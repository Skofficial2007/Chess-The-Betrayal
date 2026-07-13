using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// MoveSelectionPolicy applies AIProfile's personality dials (blunder roll, tie-break window,
    /// Betrayal-aggression reweight) to a search's ranked root-move output. These tests construct
    /// that ranked output directly (no real search) so each dial can be exercised in isolation
    /// against crafted scores. See ADR_AI23_Profile_EventStream_OpeningBook.md Section 2.2/2.3.
    /// </summary>
    [TestFixture]
    public class MoveSelectionPolicyTests
    {
        private static MoveCommand Move(int fromX, int fromY, int toX, int toY, BetrayalStage stage = BetrayalStage.None)
        {
            var piece = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1);
            return new MoveCommand(
                new Vector2Int(fromX, fromY), new Vector2Int(toX, toY), piece, stage: stage);
        }

        private static AIProfile ProfileWith(float blunderRate = 0f, int blunderMarginCp = 0,
            float betrayalAggression = 0f, int tieBreakWindowCp = 0) =>
            new AIProfile("test", maxDepth: 1, softTimeBudgetMs: 1000, blunderRate, blunderMarginCp,
                betrayalAggression, attackDefenseBias: 1f, tieBreakWindowCp, useOpeningBook: false);

        /// <summary>Always returns 0f from NextFloat (forces any probability roll to succeed / any
        /// weighted pick to land on the first candidate) and 0 from NextInt.</summary>
        private sealed class ZeroRandomSource : IRandomSource
        {
            public bool NextBool() => false;
            public int NextInt(int maxExclusive) => 0;
            public float NextFloat() => 0f;
        }

        /// <summary>Throws on any call — proves a code path never touches the RNG at all.</summary>
        private sealed class ThrowingRandomSource : IRandomSource
        {
            public bool NextBool() => throw new System.InvalidOperationException("Unexpected RNG call.");
            public int NextInt(int maxExclusive) => throw new System.InvalidOperationException("Unexpected RNG call.");
            public float NextFloat() => throw new System.InvalidOperationException("Unexpected RNG call.");
        }

        [Test]
        public void SelectFinalMove_FixedSeed_ProducesBitIdenticalResultsAcrossRuns()
        {
            var rootMoves = new List<MoveCommand>
            {
                Move(0, 1, 0, 2), // best, index 0
                Move(1, 1, 1, 3), // index 1, within margin/window
                Move(2, 1, 2, 3), // index 2, within margin/window
            };
            int[] rootScores = { 100, 90, 85 };
            var profile = ProfileWith(blunderRate: 0.5f, blunderMarginCp: 30, tieBreakWindowCp: 20);

            var policy1 = new MoveSelectionPolicy();
            var policy2 = new MoveSelectionPolicy();
            var rng1 = new SystemRandomSource(seed: 12345);
            var rng2 = new SystemRandomSource(seed: 12345);

            MoveCommand result1 = policy1.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, rng1);
            MoveCommand result2 = policy2.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, rng2);

            Assert.That(result1.StartPosition, Is.EqualTo(result2.StartPosition));
            Assert.That(result1.EndPosition, Is.EqualTo(result2.EndPosition));
            Assert.That(result1.Stage, Is.EqualTo(result2.Stage));
        }

        [Test]
        public void SelectFinalMove_BlunderRoll_AlwaysWithinBlunderMarginCp_OfBest()
        {
            var rootMoves = new List<MoveCommand>
            {
                Move(0, 1, 0, 2), // best, score 100
                Move(1, 1, 1, 3), // score 80, within a 30cp margin
                Move(2, 1, 2, 3), // score 40, outside a 30cp margin
            };
            int[] rootScores = { 100, 80, 40 };
            var profile = ProfileWith(blunderRate: 1f, blunderMarginCp: 30); // always rolls the blunder

            var policy = new MoveSelectionPolicy();
            var rng = new ZeroRandomSource(); // NextFloat()=0 < BlunderRate; NextInt picks first candidate

            MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, rng);

            // Only index 1 (score 80) is within [100-30, 100) excluding the best itself.
            Assert.That(result.EndPosition, Is.EqualTo(rootMoves[1].EndPosition),
                "The blunder pick must be a candidate within BlunderMarginCp of best, never the out-of-margin move.");
        }

        [Test]
        public void SelectFinalMove_BlunderRoll_NeverPicksBestIndexItself()
        {
            var rootMoves = new List<MoveCommand>
            {
                Move(0, 1, 0, 2),
                Move(1, 1, 1, 3),
            };
            int[] rootScores = { 100, 95 };
            var profile = ProfileWith(blunderRate: 1f, blunderMarginCp: 50);

            var policy = new MoveSelectionPolicy();
            var rng = new ZeroRandomSource();

            MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, rng);

            Assert.That(result.EndPosition, Is.Not.EqualTo(rootMoves[0].EndPosition),
                "A successful blunder roll must pick a DIFFERENT move than the best — '2nd/3rd-best, never noise.'");
        }

        [Test]
        public void SelectFinalMove_BlunderRoll_NoCandidatesWithinMargin_FallsThroughToTieBreak()
        {
            var rootMoves = new List<MoveCommand>
            {
                Move(0, 1, 0, 2), // best, score 100 — the only move
            };
            int[] rootScores = { 100 };
            var profile = ProfileWith(blunderRate: 1f, blunderMarginCp: 10);

            var policy = new MoveSelectionPolicy();
            var rng = new ThrowingRandomSource(); // must not be consulted further after empty candidate set... but NextFloat() IS called for the roll itself

            // NextFloat() is called once for the roll (must succeed/return something), so use Zero instead.
            var zeroRng = new ZeroRandomSource();
            MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, zeroRng);

            Assert.That(result.EndPosition, Is.EqualTo(rootMoves[0].EndPosition),
                "With no other legal moves, a blunder roll must never force a synthesized move — it falls through to the best.");
        }

        [Test]
        public void SelectFinalMove_BetrayalAggression_NeverSelectsOutsideTieBreakWindow()
        {
            const int tieBreakWindowCp = 20;
            var rootMoves = new List<MoveCommand>
            {
                Move(0, 1, 0, 2), // best, non-Act, score 100
                Move(1, 1, 1, 3, BetrayalStage.Act), // inside window: 100 - 20 + 1 = 81
                Move(2, 1, 2, 3, BetrayalStage.Act), // outside window: 100 - 20 - 50 = 30
            };
            int[] rootScores = { 100, 81, 30 };
            var profile = ProfileWith(betrayalAggression: 1.0f, tieBreakWindowCp: tieBreakWindowCp);

            var policy = new MoveSelectionPolicy();
            var rng = new ZeroRandomSource(); // biases toward the first (highest-weighted-cumulative) candidate

            for (int trial = 0; trial < 10; trial++)
            {
                MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, rng);
                Assert.That(result.EndPosition, Is.Not.EqualTo(rootMoves[2].EndPosition),
                    "Betrayal-aggression reweights WITHIN the tie-break window only — it must never pull in a move outside it.");
            }
        }

        [Test]
        public void SelectFinalMove_ZeroDialProfile_ReturnsExactBestMove_ZeroRngCalls()
        {
            var rootMoves = new List<MoveCommand> { Move(0, 1, 0, 2), Move(1, 1, 1, 3) };
            int[] rootScores = { 100, 90 };

            var policy = new MoveSelectionPolicy();
            var throwingRng = new ThrowingRandomSource();

            MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, AIProfile.None, throwingRng);

            Assert.That(result.EndPosition, Is.EqualTo(rootMoves[0].EndPosition));
        }

        [Test]
        public void SelectFinalMove_WindowSizeOne_Deterministic_ZeroRngCalls()
        {
            var rootMoves = new List<MoveCommand> { Move(0, 1, 0, 2), Move(1, 1, 1, 3) };
            int[] rootScores = { 100, 50 }; // distinct 2nd-best, well outside a zero-width window
            var profile = ProfileWith(blunderRate: 0f, tieBreakWindowCp: 0);

            var policy = new MoveSelectionPolicy();
            var throwingRng = new ThrowingRandomSource();

            MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, throwingRng);

            Assert.That(result.EndPosition, Is.EqualTo(rootMoves[0].EndPosition),
                "TieBreakWindowCp == 0 with BlunderRate == 0 (Impossible-tier shape) must be structurally deterministic — zero RNG calls.");
        }

        [Test]
        public void SelectFinalMove_AllActWindow_MaximumAggressionAversion_ReturnsBestScoredMove()
        {
            // Every candidate inside the tie-break window is an Act move, and BetrayalAggression
            // is pinned to -1 (its floor) — every weight in the window collapses to zero, which
            // used to fall through to whichever candidate happened to be last in scan order
            // instead of the best-scored one.
            var rootMoves = new List<MoveCommand>
            {
                Move(0, 1, 0, 2, BetrayalStage.Act), // best, score 100
                Move(1, 1, 1, 3, BetrayalStage.Act), // score 90, within window
                Move(2, 1, 2, 3, BetrayalStage.Act), // score 85, within window
            };
            int[] rootScores = { 100, 90, 85 };
            var profile = ProfileWith(betrayalAggression: -1f, tieBreakWindowCp: 20);

            var policy = new MoveSelectionPolicy();
            var rng = new ZeroRandomSource();

            MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, rng);

            Assert.That(result.EndPosition, Is.EqualTo(rootMoves[0].EndPosition),
                "A zero-total-weight window must fall back to the best-scored move, not the last candidate scanned.");
        }

        [Test]
        public void SelectFinalMove_NullRandomSource_ReturnsExactBestMove()
        {
            var rootMoves = new List<MoveCommand> { Move(0, 1, 0, 2), Move(1, 1, 1, 3) };
            int[] rootScores = { 100, 99 };
            var profile = ProfileWith(blunderRate: 1f, tieBreakWindowCp: 50); // would otherwise definitely roll

            var policy = new MoveSelectionPolicy();

            MoveCommand result = policy.SelectFinalMove(rootMoves, rootScores, rootMoves.Count, 0, profile, rng: null);

            Assert.That(result.EndPosition, Is.EqualTo(rootMoves[0].EndPosition),
                "A null IRandomSource means no personality — always the search's own best move.");
        }
    }
}
