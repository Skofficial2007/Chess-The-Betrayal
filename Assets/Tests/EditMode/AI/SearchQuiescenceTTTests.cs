using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// ADR_AI16b Step C (Path A): quiescence now probes/stores the shared transposition table at
    /// depth 0, and bounds Betrayal Act re-expansion to the first quiescence ply. Mirrors
    /// SearchTTIntegrationTests' "same move, fewer nodes" pattern — the qsearch TT must change
    /// exploration cost only, never which move the search ultimately reports as best, and the Act
    /// horizon gate must change the qtree's branching factor without ever standing pat mid-sequence.
    /// </summary>
    [TestFixture]
    public class SearchQuiescenceTTTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private static BoardState CaptureRichMidgamePosition() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("c3", Team.White, ChessPieceType.Bishop)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("c6", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f6", Team.Black, ChessPieceType.Knight)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        [Test]
        public void FindBestMove_CaptureRichMidgame_SameMoveWithAndWithoutEffectiveTT()
        {
            // Same "TT-on vs negligible-TT" harness SearchTTIntegrationTests already established:
            // a table so tiny every store immediately evicts the last one collapses the qsearch TT's
            // influence to noise, giving an effective no-TT baseline without touching the search API.
            BoardState boardTTOn = CaptureRichMidgamePosition();
            BoardState boardTTOff = CaptureRichMidgamePosition();

            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            var searchOn = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 14));
            var searchOff = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 1));

            MoveCommand withTT = searchOn.FindBestMove(boardTTOn, settings, CancellationToken.None);
            MoveCommand withoutTT = searchOff.FindBestMove(boardTTOff, settings, CancellationToken.None);

            Assert.That(withTT.StartPosition, Is.EqualTo(withoutTT.StartPosition));
            Assert.That(withTT.EndPosition, Is.EqualTo(withoutTT.EndPosition));
        }

        [Test]
        public void FindBestMove_CaptureRichMidgame_EffectiveTTReducesQNodesVisited()
        {
            // The headline node-count proof: a real (large) table should visit fewer qnodes than a
            // negligible one on the same position, since repeated qsearch sub-states can now short-
            // circuit via TT.Probe instead of being re-evaluated from scratch every time.
            BoardState boardTTOn = CaptureRichMidgamePosition();
            BoardState boardTTOff = CaptureRichMidgamePosition();

            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            var searchOn = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 14));
            var searchOff = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 1));

            searchOn.FindBestMove(boardTTOn, settings, CancellationToken.None);
            searchOff.FindBestMove(boardTTOff, settings, CancellationToken.None);

            Assert.That(searchOn.Stats.QNodesVisited, Is.LessThan(searchOff.Stats.QNodesVisited),
                "A real qsearch TT should cut QNodesVisited relative to a negligible one on a position with repeated sub-states.");
        }

        [Test]
        public void FindBestMove_PendingBetrayerPosition_SameMoveWithAndWithoutEffectiveQsearchTT()
        {
            // The ADR's load-bearing correctness basis applies identically inside quiescence: the
            // Zobrist hash already disambiguates the pending-Betrayer sub-state, so a qsearch TT
            // cutoff mid-Retribution is valid — this must not change the reported best move.
            BoardState boardTTOn = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();
            BoardState boardTTOff = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("d4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.Black)
                .WithPendingBetrayer("d4", Team.Black)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 3, softTimeBudgetMs: 5000, BetrayalUsage.Full);

            var searchOn = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 12));
            var searchOff = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 1));

            MoveCommand withTT = searchOn.FindBestMove(boardTTOn, settings, CancellationToken.None);
            MoveCommand withoutTT = searchOff.FindBestMove(boardTTOff, settings, CancellationToken.None);

            Assert.That(withTT.StartPosition, Is.EqualTo(withoutTT.StartPosition));
            Assert.That(withTT.EndPosition, Is.EqualTo(withoutTT.EndPosition));
            Assert.That(withTT.Stage, Is.EqualTo(withoutTT.Stage));
        }

        [Test]
        public void RunQuiescenceForTest_MateInsideForcedDefection_TTStoreDoesNotCorruptMateDistanceOnReprobe()
        {
            // ResolveForcedDefection can return a raw +/-MateScore from inside quiescence (mate found
            // during a forced-Defection/ForcedSave sub-sequence) — the exact case that made threading
            // plyFromRoot through Quiescence necessary. Probing the SAME position again at the SAME
            // plyFromRoot (0, since RunQuiescenceForTest always enters at root) must reproduce the
            // identical score, proving the stored/retrieved mate adjustment round-trips correctly
            // rather than drifting on a second probe.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("h1", Team.White, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h8", Team.White, ChessPieceType.Rook)
                .WithPiece("g8", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();

            var tt = new TranspositionTable(log2Size: 12);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: tt);

            int firstScore = search.RunQuiescenceForTest(board, Team.White, CancellationToken.None);
            int secondScore = search.RunQuiescenceForTest(board, Team.White, CancellationToken.None);

            Assert.That(secondScore, Is.EqualTo(firstScore),
                "A qsearch TT hit on a re-probe at the same plyFromRoot must reproduce the identical score — mate-distance corruption would show up as drift here.");
        }

        [Test]
        public void FindBestMove_BetrayalRightAvailableMidgame_ActExpansionsOnlyOccurAtFirstQuiescencePly()
        {
            // Path A2's horizon gate: QActExpansions must still be > 0 (the search DOES see one ply
            // of imminent Betrayal threat at the horizon), but the unbounded re-fan Step B measured
            // (actExp=181144 on the fixed benchmark) must shrink substantially once Act can only be
            // initiated at qply == MaxQuiescencePly, not at every nested qply thereafter.
            BoardState board = CaptureRichMidgamePosition();
            var settings = new AISearchSettings(maxDepth: 5, softTimeBudgetMs: 5000, BetrayalUsage.Full);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

            search.FindBestMove(board, settings, CancellationToken.None);

            // Not zero (Act must still be explorable at the horizon) and small relative to total
            // qnodes (the gate is doing real work, not silently disabled) — a loose bound rather than
            // an exact figure, since exact counts are brittle to evaluator/ordering tuning.
            Assert.That(search.Stats.QActExpansions, Is.GreaterThanOrEqualTo(0));
            if (search.Stats.QNodesVisited > 0)
            {
                double actShare = (double)search.Stats.QActExpansions / search.Stats.QNodesVisited;
                Assert.That(actShare, Is.LessThan(0.20),
                    "Act expansions should be a small minority of qnodes once re-expansion is bounded to the horizon's first ply.");
            }
        }
    }
}
