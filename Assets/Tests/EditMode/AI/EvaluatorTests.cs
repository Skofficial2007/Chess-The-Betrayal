using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    [TestFixture]
    public class EvaluatorTests
    {
        private BetrayalAwareEvaluator _evaluator;

        [SetUp]
        public void Setup()
        {
            _evaluator = new BetrayalAwareEvaluator();
        }

        [Test]
        public void Evaluate_EqualMaterialSymmetricPosition_ScoresZeroForBothTeams()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen);

            Assert.That(_evaluator.Evaluate(board, Team.White), Is.EqualTo(0));
            Assert.That(_evaluator.Evaluate(board, Team.Black), Is.EqualTo(0));
        }

        [Test]
        public void Evaluate_MirroredPosition_ScoresNegatedBetweenTeams()
        {
            // A vertically mirrored position (White's setup reflected onto Black's ranks) must
            // score identically for the side it favors regardless of which color that is —
            // Evaluate(board, White) == -Evaluate(board, Black) always, by construction.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("a4", Team.White, ChessPieceType.Knight);

            int whiteScore = _evaluator.Evaluate(board, Team.White);
            int blackScore = _evaluator.Evaluate(board, Team.Black);

            Assert.That(blackScore, Is.EqualTo(-whiteScore));
        }

        [Test]
        public void Evaluate_ExtraQueen_StronglyFavorsThatTeam()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen);

            Assert.That(_evaluator.Evaluate(board, Team.White), Is.GreaterThan(900));
            Assert.That(_evaluator.Evaluate(board, Team.Black), Is.LessThan(-900));
        }

        [Test]
        public void Evaluate_KnightOnRimVsCenter_PrefersCenter()
        {
            BoardState rimBoard = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a1", Team.White, ChessPieceType.Knight);

            BoardState centerBoard = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Knight);

            int rimScore = _evaluator.Evaluate(rimBoard, Team.White);
            int centerScore = _evaluator.Evaluate(centerBoard, Team.White);

            Assert.That(centerScore, Is.GreaterThan(rimScore),
                "A centralized knight must score higher than the same knight parked on the rim.");
        }

        [Test]
        public void Evaluate_BetrayalRightAvailable_CreditsSideToMove()
        {
            BoardState boardWithRight = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            BoardState boardWithoutRight = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false);

            int scoreWithRight = _evaluator.Evaluate(boardWithRight, Team.White);
            int scoreWithoutRight = _evaluator.Evaluate(boardWithoutRight, Team.White);

            Assert.That(scoreWithRight, Is.GreaterThan(scoreWithoutRight),
                "An unspent betrayal right must be worth a positive bonus to the side to move.");
        }

        [Test]
        public void Evaluate_BetrayalRightAvailable_PenalizesOpponentOfSideToMove()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            int whiteScore = _evaluator.Evaluate(board, Team.White);
            int blackScore = _evaluator.Evaluate(board, Team.Black);

            Assert.That(whiteScore, Is.GreaterThan(0), "The side to move should be credited for holding the live option.");
            Assert.That(blackScore, Is.LessThan(0), "The opponent should see the same option as a negative from their perspective.");
        }
    }
}
