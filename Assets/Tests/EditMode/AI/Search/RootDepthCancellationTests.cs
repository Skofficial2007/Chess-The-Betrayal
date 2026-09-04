using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.AI.Search
{
    /// <summary>
    /// What the deepening loop is allowed to keep when the clock stops it part-way through a depth.
    ///
    /// A depth is only worth committing if every root move in it was actually searched. The loop
    /// asks the cancellation token at the top of each root move, which catches a cancellation during
    /// any move except the last one - that one has no next turn of the loop to be caught by, so the
    /// scan ran off the end still believing it had finished. The move's own score came from a search
    /// that returned early, and a search that returns early returns nothing worth having.
    ///
    /// The rule this holds the loop to is the one the candidate rescore pass in the same file
    /// already follows: a score whose search was cancelled is discarded, not published.
    /// </summary>
    [TestFixture]
    public class RootDepthCancellationTests
    {
        /// <summary>
        /// Cancels the moment it is asked about a position other than the one the search started
        /// from, which is to say from inside a root move's own subtree.
        ///
        /// Timing it by the board rather than by counting calls is what makes this reliable. A count
        /// would have to assume how many times the search evaluates before it reaches the first root
        /// move, and getting that wrong by one cancels before the loop starts - where the existing
        /// check catches it, and the test passes while proving nothing.
        /// </summary>
        private sealed class CancelInsideTheFirstRootMove : IPositionEvaluator
        {
            private readonly CancellationTokenSource _cts;
            private readonly Vector2Int _kingStartedOn;
            private readonly IPositionEvaluator _real = new BetrayalAwareEvaluator();

            public CancelInsideTheFirstRootMove(CancellationTokenSource cts, Vector2Int kingStartedOn)
            {
                _cts = cts;
                _kingStartedOn = kingStartedOn;
            }

            public int MaxCheapToFullSwing => _real.MaxCheapToFullSwing;

            public int Evaluate(BoardState board, Team forTeam)
            {
                CancelOnceInsideAMove(board);
                return _real.Evaluate(board, forTeam);
            }

            // Both paths trip it, because which one the search reaches first is its own business and
            // not something this test should be quietly depending on.
            public int EvaluateCheap(BoardState board, Team forTeam)
            {
                CancelOnceInsideAMove(board);
                return _real.EvaluateCheap(board, forTeam);
            }

            private void CancelOnceInsideAMove(BoardState board)
            {
                if (board.GetPiece(_kingStartedOn).Type != ChessPieceType.King) _cts.Cancel();
            }
        }

        private static readonly Vector2Int KingSquare = BoardSetup.AlgebraicToVector("h1");

        /// <summary>
        /// White has nothing but a king, and a black rook down the g-file leaves it one square to
        /// step to. One root move means the move the clock interrupts is unavoidably the last one,
        /// with no assumption about the order the search happens to visit them in.
        ///
        /// Nothing to betray, either: an Act captures a friendly piece and there is no second white
        /// piece on the board, so the count below is the king's own moves and nothing else.
        /// </summary>
        private static BoardState AKingWithExactlyOneMove()
        {
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("h1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.Rook)
                .WithPiece("a3", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false);
            board.ComputeFullZobristHash();
            return board;
        }

        [Test]
        public void ThePositionUsedHereReallyDoesLeaveExactlyOneMove()
        {
            var engine = new ChessEngineAdapter();
            var legal = new List<MoveCommand>();

            engine.GetAllLegalMovesIncludingBetrayal(AKingWithExactlyOneMove(), Team.White, legal);

            Assert.That(legal, Has.Count.EqualTo(1),
                "The rest of this fixture rests on the interrupted move being the last one in the list.");
        }

        [Test]
        public void ADepthWhoseLastRootMoveWasCancelledIsNotCommitted()
        {
            var engine = new ChessEngineAdapter();
            using var cts = new CancellationTokenSource();
            var search = new AlphaBetaSearch(engine, new CancelInsideTheFirstRootMove(cts, KingSquare));
            var settings = new AISearchSettings(maxDepth: 1, TestTimeBudgets.Generous, BetrayalUsage.Full);

            search.FindBestMove(AKingWithExactlyOneMove(), settings, cts.Token);

            Assert.That(search.LastCompletedDepth, Is.EqualTo(0),
                "The only root move was scored by a search that had already been cancelled, so the depth "
                + "it belongs to never finished and must not be reported as reached.");
        }

        /// <summary>
        /// The same failure said the other way round, because the reported depth is not the only
        /// thing a caller reads. A depth wrongly kept also reports the tier as having finished the
        /// work it was asked for, when the clock is what ended it.
        /// </summary>
        [Test]
        public void ACancelledLastRootMoveIsReportedAsTheClockStopping()
        {
            var engine = new ChessEngineAdapter();
            using var cts = new CancellationTokenSource();
            var search = new AlphaBetaSearch(engine, new CancelInsideTheFirstRootMove(cts, KingSquare));
            var settings = new AISearchSettings(maxDepth: 1, TestTimeBudgets.Generous, BetrayalUsage.Full);

            search.FindBestMove(AKingWithExactlyOneMove(), settings, cts.Token);

            Assert.That(search.StopReason, Is.EqualTo(SearchStopReason.Budget),
                "Reaching the ceiling and being stopped short of it are opposite outcomes, and only one "
                + "of them happened here.");
        }
    }
}
