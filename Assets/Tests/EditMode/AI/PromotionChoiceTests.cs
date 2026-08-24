using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Whether the AI really picks what a promoting pawn becomes, or only ever takes a queen.
    ///
    /// Two things have to hold and they fail in different ways. The four promotions must survive
    /// the whole path from move generation to the board, because anything that collapsed them to
    /// one would leave a legal move the engine simply cannot play. And the search must actually
    /// take a lesser piece when a lesser piece wins — that is the part no structural check can
    /// prove, so it is played out on a board where the knight is the only move that works.
    /// </summary>
    [TestFixture]
    public class PromotionChoiceTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
        }

        private static List<MoveCommand> PromotionsIn(List<MoveCommand> moves)
        {
            var promotions = new List<MoveCommand>();
            foreach (MoveCommand move in moves)
            {
                if (move.IsPromotion) promotions.Add(move);
            }
            return promotions;
        }

        #region The choice exists

        [Test]
        public void APawnOneStepFromTheEndIsOfferedAllFourPieces()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("h1", Team.White, ChessPieceType.King)
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var moves = new List<MoveCommand>();
            _engine.GetAllLegalMoves(board, Team.White, moves);

            List<MoveCommand> promotions = PromotionsIn(moves);

            var offered = new HashSet<ChessPieceType>();
            foreach (MoveCommand promotion in promotions) offered.Add(promotion.PromotedTo);

            Assert.That(offered, Is.EquivalentTo(new[]
            {
                ChessPieceType.Queen,
                ChessPieceType.Rook,
                ChessPieceType.Knight,
                ChessPieceType.Bishop
            }), "A promoting pawn must be offered every piece, not just a queen.");
        }

        [Test]
        public void ACapturingPromotionIsAlsoOfferedAllFourPieces()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("h1", Team.White, ChessPieceType.King)
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var moves = new List<MoveCommand>();
            _engine.GetAllLegalMoves(board, Team.White, moves);

            var offeredOnTheCapture = new HashSet<ChessPieceType>();
            foreach (MoveCommand move in PromotionsIn(moves))
            {
                if (move.EndPosition == TestBoardSetupUtility.AlgebraicToVector("f8"))
                {
                    offeredOnTheCapture.Add(move.PromotedTo);
                }
            }

            Assert.That(offeredOnTheCapture.Count, Is.EqualTo(4));
        }

        [Test]
        public void WhicheverPieceIsChosenIsThePieceThatEndsUpOnTheBoard()
        {
            // The choice would be worth nothing if the board put a queen down regardless.
            foreach (ChessPieceType chosen in new[]
            {
                ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop
            })
            {
                BoardState board = TestBoardSetupUtility.CreateEmpty()
                    .WithPiece("h1", Team.White, ChessPieceType.King)
                    .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                    .WithPiece("a8", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithBetrayalRight(false)
                    .WithComputedHash();

                var moves = new List<MoveCommand>();
                _engine.GetAllLegalMoves(board, Team.White, moves);

                MoveCommand promotion = default;
                foreach (MoveCommand move in PromotionsIn(moves))
                {
                    if (move.PromotedTo == chosen) promotion = move;
                }

                Assert.That(promotion.IsPromotion, Is.True, $"No {chosen} promotion was generated.");

                _engine.ApplyMove(board, promotion);

                PieceData arrived = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e8"));
                Assert.That(arrived.Type, Is.EqualTo(chosen),
                    $"Promoting to {chosen} put a {arrived.Type} on the board instead.");
            }
        }

        #endregion

        #region The choice is used

        [Test]
        public void TheSearchTakesAKnightWhenOnlyAKnightWinsTheQueen()
        {
            // White pawn e7. Black king c7, black queen g7. Promoting to a knight lands on e8
            // checking the king AND attacking the queen — the king has to move and the queen falls
            // next move. Any other promotion forks nothing and leaves the queens on. A default to
            // queen fails this outright.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("h1", Team.White, ChessPieceType.King)
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("c7", Team.Black, ChessPieceType.King)
                .WithPiece("g7", Team.Black, ChessPieceType.Queen)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.DefendOnly);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.IsPromotion, Is.True, $"Expected a promotion, got {best}.");
            Assert.That(best.EndPosition, Is.EqualTo(TestBoardSetupUtility.AlgebraicToVector("e8")));
            Assert.That(best.PromotedTo, Is.EqualTo(ChessPieceType.Knight),
                $"The knight fork was available and the search took a {best.PromotedTo}.");
        }

        [Test]
        public void TheSearchStillTakesAQueenWhenNothingArguesAgainstIt()
        {
            // The other half of the same claim: preferring a knight above must be the position
            // talking, not a bias. With no tactic on the board the queen is simply the best piece.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("h1", Team.White, ChessPieceType.King)
                .WithPiece("e7", Team.White, ChessPieceType.Pawn, hasMoved: true)
                .WithPiece("a7", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var settings = new AISearchSettings(maxDepth: 5, timeBudget: TestTimeBudgets.Generous, BetrayalUsage.DefendOnly);
            MoveCommand best = _search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.IsPromotion, Is.True, $"Expected a promotion, got {best}.");
            Assert.That(best.PromotedTo, Is.EqualTo(ChessPieceType.Queen));
        }

        #endregion
    }
}
