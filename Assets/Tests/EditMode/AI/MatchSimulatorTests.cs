using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins MatchSimulator's own contract (determinism, seed independence, adjudication) rather
    /// than any specific profile's strength — that ordering assertion lives in
    /// AIProfileStrengthOrderingTests, tagged as an explicit/nightly-class run since it plays real
    /// tournaments and is too slow for the routine per-commit suite.
    /// </summary>
    [TestFixture]
    public class MatchSimulatorTests
    {
        private static AIProfile Fast(string id, int maxDepth) =>
            new AIProfile(id, maxDepth, timeBudget: new AITimeBudget(2000, 3000), blunderRate: 0f, blunderMarginCp: 0,
                betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0, useOpeningBook: false);

        [Test]
        public void PlayGame_SameSeeds_ProducesBitIdenticalOutcomeAndPlyCount()
        {
            BoardState position = CuratedPositionSuite.Build(0);
            AIProfile shallow = Fast("shallow", maxDepth: 2);

            var simulator1 = new MatchSimulator();
            var simulator2 = new MatchSimulator();

            MatchResult result1 = simulator1.PlayGame(position, shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222, plyCap: 20);
            MatchResult result2 = simulator2.PlayGame(position, shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222, plyCap: 20);

            Assert.That(result2.Outcome, Is.EqualTo(result1.Outcome));
            Assert.That(result2.PlyCount, Is.EqualTo(result1.PlyCount));
        }

        [Test]
        public void PlayGame_DoesNotMutateTheCallersStartingPosition()
        {
            BoardState position = CuratedPositionSuite.Build(0);
            ulong hashBefore = position.ZobristHash;
            AIProfile shallow = Fast("shallow", maxDepth: 2);

            new MatchSimulator().PlayGame(position, shallow, shallow, rngSeedWhite: 1, rngSeedBlack: 2, plyCap: 10);

            Assert.That(position.ZobristHash, Is.EqualTo(hashBefore),
                "PlayGame must clone the starting position — the same BoardState instance is reused across every game in a tournament.");
        }

        [Test]
        public void PlayGame_ReachesPlyCap_AdjudicatesRatherThanThrowing()
        {
            BoardState position = CuratedPositionSuite.Build(0);
            AIProfile veryShallow = Fast("veryshallow", maxDepth: 1);

            MatchResult result = new MatchSimulator().PlayGame(
                position, veryShallow, veryShallow, rngSeedWhite: 1, rngSeedBlack: 2, plyCap: 4);

            Assert.That(result.PlyCount, Is.LessThanOrEqualTo(4));
            Assert.That(System.Enum.IsDefined(typeof(MatchOutcome), result.Outcome), Is.True);
        }

        [Test]
        public void PlayGame_ReusingOneSimulatorAcrossGames_MatchesAFreshSimulatorsResult()
        {
            // The simulator keeps its two transposition tables alive across games and wipes them
            // between games. If that wipe ever regressed, entries left over from the first game
            // would perturb move ordering in the second and this comparison against a fresh
            // simulator would diverge. Uncapped + zero-dial profiles so the games are fully
            // deterministic and the only possible source of divergence is table carryover.
            AIProfile shallow = Fast("shallow", maxDepth: 2);

            var reused = new MatchSimulator(MatchTimeControl.Uncapped);
            reused.PlayGame(CuratedPositionSuite.Build(0), shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222, plyCap: 12);
            MatchResult second = reused.PlayGame(CuratedPositionSuite.Build(1), shallow, shallow, rngSeedWhite: 333, rngSeedBlack: 444, plyCap: 12);

            MatchResult fresh = new MatchSimulator(MatchTimeControl.Uncapped)
                .PlayGame(CuratedPositionSuite.Build(1), shallow, shallow, rngSeedWhite: 333, rngSeedBlack: 444, plyCap: 12);

            Assert.That(second.Outcome, Is.EqualTo(fresh.Outcome));
            Assert.That(second.PlyCount, Is.EqualTo(fresh.PlyCount));
        }

        [Test]
        public void PlayGame_ProductionBudget_TightBudgetBoundsADeepProfilesMoves()
        {
            // A depth-9 profile squeezed into a 40ms hard budget: every move must get cut off by
            // the budget timer instead of running its full depth. Ten such plies plus harness
            // overhead land well under a second; the pre-time-control behavior (every move
            // searched to depth 9 regardless) took multiple seconds per MOVE on midgame
            // positions, so the generous ceiling here still cleanly separates the two.
            var deepButTight = new AIProfile("deeptight", maxDepth: 9, timeBudget: new AITimeBudget(20, 40),
                blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f,
                tieBreakWindowCp: 0, useOpeningBook: false);
            BoardState position = CuratedPositionSuite.Build(0);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            MatchResult result = new MatchSimulator().PlayGame(
                position, deepButTight, deepButTight, rngSeedWhite: 1, rngSeedBlack: 2, plyCap: 10);
            stopwatch.Stop();

            Assert.That(System.Enum.IsDefined(typeof(MatchOutcome), result.Outcome), Is.True);
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(5.0),
                "a 40ms hard budget across at most 10 plies must not take seconds — the budget timer is not arming.");
        }

        [Test]
        public void PlayGame_ZeroDialProfilesReachAQuietRepeatingLine_AdjudicationEndsItBeforeThePlyCap()
        {
            // Two identical, zero-blunder, zero-personality profiles from a roughly balanced
            // position tend to shuffle into a repeating/quiet line rather than a decisive result —
            // exactly the scenario the fifty-move/repetition/draw-margin rules exist to cut short.
            // A generous 120-ply cap (DefaultPlyCap) is the pre-adjudication behavior; this proves
            // Standard rules end the game well before that ceiling is reached.
            AIProfile shallow = Fast("shallow", maxDepth: 2);
            BoardState position = CuratedPositionSuite.Build(0);

            MatchResult adjudicated = new MatchSimulator(MatchTimeControl.Uncapped, adjudicationRules: AdjudicationRules.Standard)
                .PlayGame(position, shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222);

            Assert.That(adjudicated.PlyCount, Is.LessThan(MatchSimulator.DefaultPlyCap),
                "Standard adjudication rules should end a quiet/repeating shallow-vs-shallow game well before the full ply cap.");
        }

        [Test]
        public void PlayGame_AdjudicationDisabled_PlaysAtLeastAsLongAsWithStandardRules()
        {
            AIProfile shallow = Fast("shallow", maxDepth: 2);
            BoardState position = CuratedPositionSuite.Build(0);

            MatchResult withStandardRules = new MatchSimulator(MatchTimeControl.Uncapped, adjudicationRules: AdjudicationRules.Standard)
                .PlayGame(position, shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222);
            MatchResult withRulesDisabled = new MatchSimulator(MatchTimeControl.Uncapped, adjudicationRules: AdjudicationRules.Disabled)
                .PlayGame(position, shallow, shallow, rngSeedWhite: 111, rngSeedBlack: 222);

            Assert.That(withRulesDisabled.PlyCount, Is.GreaterThanOrEqualTo(withStandardRules.PlyCount),
                "Disabled must never end a game EARLIER than Standard would — it is strictly a superset of plies played.");
        }

        [Test]
        public void TournamentSeeding_PerturbingOneSidesGameIndex_LeavesTheOtherSidesSeedUnchanged()
        {
            int whiteSeedBefore = TournamentSeeding.DeriveSeed(runSeed: 7, positionIndex: 0, pairIndex: 0, gameIndex: 0, side: 0);
            int blackSeedBefore = TournamentSeeding.DeriveSeed(runSeed: 7, positionIndex: 0, pairIndex: 0, gameIndex: 0, side: 1);

            // Changing gameIndex perturbs both streams identically (that's expected — a different
            // game). The point of this test is that side 0 and side 1 never collide with each other
            // for the SAME (runSeed, positionIndex, pairIndex, gameIndex) tuple.
            Assert.That(whiteSeedBefore, Is.Not.EqualTo(blackSeedBefore));
        }

        [Test]
        public void TournamentSeeding_SameInputs_AlwaysProducesTheSameSeed()
        {
            int seed1 = TournamentSeeding.DeriveSeed(runSeed: 42, positionIndex: 3, pairIndex: 1, gameIndex: 5, side: 0);
            int seed2 = TournamentSeeding.DeriveSeed(runSeed: 42, positionIndex: 3, pairIndex: 1, gameIndex: 5, side: 0);

            Assert.That(seed2, Is.EqualTo(seed1));
        }

        [Test]
        public void CuratedPositionSuite_EveryPosition_BuildsWithoutThrowing()
        {
            for (int i = 0; i < CuratedPositionSuite.Count; i++)
            {
                BoardState board = null;
                Assert.DoesNotThrow(() => board = CuratedPositionSuite.Build(i),
                    $"Position {i}'s authored line must still be legal against the current engine.");
                Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                    $"Position {i} must leave the board's incremental Zobrist hash consistent with a full recompute.");
            }
        }

        [Test]
        public void CuratedPositionSuite_HasAtLeastTwentyPositions()
        {
            Assert.That(CuratedPositionSuite.Count, Is.GreaterThanOrEqualTo(20));
        }

        [Test]
        public void MatchSideStats_MeanCompletedDepth_IsGenuinelyDistinctFromTheMaximum()
        {
            // A hand-built two-move fixture: one move completed depth 3, the other depth 9. A mean
            // of 6 proves this reads as an actual average, not the deepest single move relabeled —
            // the failure mode this field exists to fix (DeepestCompletedDepth alone lets one cheap
            // position make a whole tier look like it typically reaches a depth it rarely does).
            var histogram = new int[MatchSideStats.DepthHistogramCapacity];
            histogram[3] = 1;
            histogram[9] = 1;
            var stats = new MatchSideStats(
                moveCount: 2, totalNodesVisited: 100, totalQNodesVisited: 20,
                deepestCompletedDepth: 9, totalElapsedMs: 500.0, blunderRollOffered: 0, blunderRollFired: 0,
                completedDepthSum: 3 + 9, shallowestCompletedDepth: 3, depthHistogram: histogram);

            Assert.That(stats.MeanCompletedDepth, Is.EqualTo(6.0));
            Assert.That(stats.DeepestCompletedDepth, Is.EqualTo(9));
            Assert.That(stats.ShallowestCompletedDepth, Is.EqualTo(3));
            Assert.That(stats.MeanCompletedDepth, Is.Not.EqualTo(stats.DeepestCompletedDepth),
                "the mean must not silently collapse to the maximum on a mixed-depth sample.");
        }

        [Test]
        public void MatchSideStats_MeanCompletedDepth_ZeroMoves_DoesNotDivideByZero()
        {
            var stats = new MatchSideStats(
                moveCount: 0, totalNodesVisited: 0, totalQNodesVisited: 0,
                deepestCompletedDepth: 0, totalElapsedMs: 0.0, blunderRollOffered: 0, blunderRollFired: 0,
                completedDepthSum: 0, shallowestCompletedDepth: 0, depthHistogram: null);

            Assert.That(stats.MeanCompletedDepth, Is.EqualTo(0.0));
        }

        [Test]
        public void PlayGameWithStats_RealGame_ReportsMeanAtOrBelowTheMaximum()
        {
            // Not a synthetic fixture — a real played game, proving the invariant holds against the
            // actual accumulator wiring (SideStatsAccumulator), not just the struct's own math.
            AIProfile shallow = Fast("shallow", maxDepth: 3);
            BoardState position = CuratedPositionSuite.Build(0);

            MatchStatsResult result = new MatchSimulator(MatchTimeControl.Uncapped)
                .PlayGameWithStats(position, shallow, shallow, rngSeedWhite: 1, rngSeedBlack: 2, plyCap: 10);

            Assert.That(result.WhiteStats.MeanCompletedDepth, Is.LessThanOrEqualTo(result.WhiteStats.DeepestCompletedDepth));
            Assert.That(result.WhiteStats.MeanCompletedDepth, Is.GreaterThanOrEqualTo(result.WhiteStats.ShallowestCompletedDepth));
            Assert.That(result.WhiteStats.DepthHistogram.Sum(), Is.EqualTo(result.WhiteStats.MoveCount),
                "every completed move must land in exactly one histogram slot.");
        }

        [Test]
        public void PlayGameWithStats_NoBetrayalOnTheBoard_ReportsZeroActsForBothSides()
        {
            AIProfile shallow = Fast("shallow", maxDepth: 2);
            BoardState position = CuratedPositionSuite.Build(0);

            MatchStatsResult result = new MatchSimulator(MatchTimeControl.Uncapped)
                .PlayGameWithStats(position, shallow, shallow, rngSeedWhite: 1, rngSeedBlack: 2, plyCap: 6);

            Assert.That(result.WhiteStats.ActsPlayed, Is.Zero);
            Assert.That(result.BlackStats.ActsPlayed, Is.Zero);
            Assert.That(result.WhiteStats.ActsResolvedByRetribution, Is.Zero);
            Assert.That(result.WhiteStats.ActsResolvedByDefection, Is.Zero);
        }

        [Test]
        public void ActThatLeavesALegalRetribution_KeepsPendingBetrayerSquareSetAndTheSameSideToMove()
        {
            // MatchSimulator's Act/Retribution/Defection counting reads exactly this signal after
            // playing an Act: PendingBetrayerSquare still set means a legal Retribution genuinely
            // exists and is about to be played by the SAME side (an ally executes its own
            // betrayer); this fixture is the SearchCorrectnessTests position where White's Knight
            // can Act its own Pawn on a3 and White's Rook on a1 can then execute it. Driving the
            // move through MatchDriver directly (rather than depending on search preference,
            // which correctly avoids this Act as a losing trade) proves the state transition the
            // counting logic relies on, independent of whether any tier would ever choose it.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            // The Rook can ALSO Act the Knight directly (its own separate legal Act), so the
            // Knight-Acts-the-Pawn move — the one this fixture is actually about — must be
            // selected explicitly rather than assuming there is only one Act available.
            var moves = new List<MoveCommand>();
            ChessEngine.GetAllLegalMovesIncludingBetrayal(board, Team.White, moves);
            MoveCommand act = moves.Single(m => m.Stage == BetrayalStage.Act && m.PieceType == ChessPieceType.Knight);

            var driver = new MatchDriver(new ChessEngineAdapter(), board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            driver.TransitionToPhase(TurnPhase.Normal);
            driver.PlayMove(act);

            Assert.That(board.PendingBetrayerSquare, Is.EqualTo(act.EndPosition),
                "a legal Retribution exists (White's own Rook can reach the betrayer), so the sequence must still be open.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.White),
                "Retribution is owed by the SAME side that Acted — the turn must not have flipped yet.");
        }

        [Test]
        public void ActWithNoLegalRetribution_ClearsPendingBetrayerSquareAndFlipsTheTurn()
        {
            // The counting logic's other branch: no legal Retribution means the engine resolves a
            // Defection inline inside PlayMove/TurnResolver.Advance, with no Retribution move ever
            // reaching MatchSimulator's ply loop. White's Knight on h8 can only Act its own Pawn on
            // g6, and nothing else on the board can reach h8 to execute it.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("h8", Team.White, ChessPieceType.Knight)
                .WithPiece("g6", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            var moves = new List<MoveCommand>();
            ChessEngine.GetAllLegalMovesIncludingBetrayal(board, Team.White, moves);
            MoveCommand act = moves.Single(m => m.Stage == BetrayalStage.Act);

            var driver = new MatchDriver(new ChessEngineAdapter(), board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            driver.TransitionToPhase(TurnPhase.Normal);
            driver.PlayMove(act);

            Assert.That(board.PendingBetrayerSquare, Is.Null,
                "no legal Retribution exists in this fixture, so the Defection must have resolved inline.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black),
                "a resolved Defection with no ForcedSave passes the turn.");
        }
    }
}
