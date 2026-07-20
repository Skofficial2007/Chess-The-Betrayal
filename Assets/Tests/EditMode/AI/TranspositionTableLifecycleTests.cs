using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the TT's match-scoped lifetime: it must persist across successive FindBestMove calls
    /// on the same AlphaBetaSearch instance (reusing what the previous turn learned is what stops
    /// node counts escalating from one turn to the next), and must be fully wiped by an explicit
    /// Clear() for a new match.
    ///
    /// Probes target a POST-ROOT-MOVE position, not the root's own hash — the root never stores or
    /// short-circuits on the TT (only the recursion below it does), so the root position itself
    /// never gets an entry. One ply into the tree is where Search actually runs.
    /// </summary>
    [TestFixture]
    public class TranspositionTableLifecycleTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private ulong HashAfterFirstLegalMove(BoardState board)
        {
            var moves = new System.Collections.Generic.List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, moves);
            MoveCommand move = moves[0];

            _engine.ApplyMove(board, move);
            if (AlphaBetaSearch.StageFlipsTurn(move.Stage)) board.NextTurn();
            ulong hash = board.ZobristHash;

            if (AlphaBetaSearch.StageFlipsTurn(move.Stage)) board.NextTurn();
            _engine.UndoMove(board, move);

            return hash;
        }

        [Test]
        public void FindBestMove_SecondCallOnSameBoard_ReusesEntriesFromFirstCall()
        {
            var tt = new TranspositionTable(log2Size: 12);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: tt);
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            ulong childHash = HashAfterFirstLegalMove(board);

            search.FindBestMove(board, settings, CancellationToken.None);

            bool hitAfterFirstSearch = tt.Probe(childHash, out _, out _, out _, out _);

            Assert.That(hitAfterFirstSearch, Is.True,
                "A depth-1 child position must have a TT entry after a completed search — otherwise the table isn't persisting anything to reuse next turn.");
        }

        [Test]
        public void SharedTable_AcrossTwoSearchInstances_ClearWipesBothViews()
        {
            var tt = new TranspositionTable(log2Size: 12);
            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(), transpositionTable: tt);
            BoardState board = TestBoardSetupUtility.CreateStandard();
            var settings = new AISearchSettings(maxDepth: 3, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.Full);
            ulong childHash = HashAfterFirstLegalMove(board);

            search.FindBestMove(board, settings, CancellationToken.None);
            Assert.That(tt.Probe(childHash, out _, out _, out _, out _), Is.True);

            tt.Clear();

            Assert.That(tt.Probe(childHash, out _, out _, out _, out _), Is.False,
                "Clear() must be visible to every search sharing this table instance — this is the seam AsyncAIAgent uses per new match.");
        }
    }
}
