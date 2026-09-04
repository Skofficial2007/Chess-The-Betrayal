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
    /// What the deepening loop may keep when the clock stops it part-way through a depth.
    ///
    /// It asks the token at the top of each root move, which covers every move but the last - that
    /// one has no next iteration to catch it, so the scan reached its bound still marked complete and
    /// kept a score from a search that had already returned early.
    ///
    /// Same rule the candidate rescore pass in that file already follows: a score whose search was
    /// cancelled is discarded rather than published.
    /// </summary>
    [TestFixture]
    public class RootDepthCancellationTests
    {
        /// <summary>
        /// Cancels as soon as it is asked about any position other than the starting one, which
        /// means it fires from inside a root move's subtree.
        ///
        /// Triggering on the board rather than on a call count is what makes it reliable. A count
        /// would have to assume how many evaluations happen before the loop starts, and being off by
        /// one cancels early - where the existing check catches it and the test passes regardless.
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
        /// White has only a king, and a black rook on the g-file leaves it one square to step to.
        /// With one root move the interrupted move is necessarily the last, so nothing here depends
        /// on the order the search visits them in.
        ///
        /// Nothing to betray either - an Act captures a friendly piece and there is no second white
        /// piece - so the count below is the king's own moves.
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
        /// The same failure from the other side. A depth wrongly kept also reports the tier as
        /// having finished what it was asked for, when the clock is what ended it.
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
