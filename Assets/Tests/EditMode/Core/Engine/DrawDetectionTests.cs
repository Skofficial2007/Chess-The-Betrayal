using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core
{
    /// <summary>
    /// The rules half of ending a game nobody is getting anywhere in. A real match ran twenty-nine
    /// consecutive rook shuffles with nothing in the game able to stop them, because the only
    /// threefold and fifty-move detection that existed belonged to the tournament harness.
    /// </summary>
    [TestFixture]
    public class DrawDetectionTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup() => _engine = new ChessEngineAdapter();

        /// <summary>Two kings and two rooks, all with room to shuffle and nothing to capture.</summary>
        private static BoardState ShufflingEndgame() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

        private void Play(BoardState board, string from, string to)
        {
            Vector2Int fromSquare = TestBoardSetupUtility.AlgebraicToVector(from);
            Vector2Int toSquare = TestBoardSetupUtility.AlgebraicToVector(to);

            var legal = new List<MoveCommand>();
            _engine.GetLegalMoves(board, fromSquare, legal);

            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].EndPosition == toSquare)
                {
                    _engine.Advance(board, legal[i]);
                    return;
                }
            }

            Assert.Fail($"No legal move from {from} to {to} - the test position has changed.");
        }

        [Test]
        public void APositionReachedForTheThirdTime_EndsTheGameAsADraw()
        {
            BoardState board = ShufflingEndgame();
            board.PushPosition(board.ZobristHash, irreversible: true); // the opening position counts

            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.EqualTo(GameState.Normal), "First time through, there is nothing to claim.");

            // Both sides walk out and back: the same position, second time.
            Play(board, "h1", "g1");
            Play(board, "h8", "g8");
            Play(board, "g1", "h1");
            Play(board, "g8", "h8");

            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.EqualTo(GameState.Normal), "Twice is not a repetition anyone can claim.");

            // And again: third time.
            Play(board, "h1", "g1");
            Play(board, "h8", "g8");
            Play(board, "g1", "h1");
            Play(board, "g8", "h8");

            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.EqualTo(GameState.DrawByRepetition),
                "Three times is a draw, and a game with no way to reach one is a game two sides can "
                + "shuffle in forever.");
        }

        [Test]
        public void ACheckmate_EndsTheGameEvenOnAPositionThatHasRepeated()
        {
            BoardState board = ShufflingEndgame();

            // Claim the position has been seen twice already, then ask about a real mate.
            board.PushPosition(board.ZobristHash, irreversible: true);
            board.PushPosition(board.ZobristHash, irreversible: false);
            board.PushPosition(board.ZobristHash, irreversible: false);

            BoardState mated = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("b6", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.Black)
                .WithBetrayalRight(false)
                .WithComputedHash();

            mated.PushPosition(mated.ZobristHash, irreversible: false);
            mated.PushPosition(mated.ZobristHash, irreversible: false);
            mated.PushPosition(mated.ZobristHash, irreversible: false);

            Assert.That(_engine.EvaluateGameState(mated, Team.Black, null),
                Is.EqualTo(GameState.Checkmate),
                "A position that ends the game outright ends it, however long the two sides had been "
                + "going nowhere beforehand.");
        }

        [Test]
        public void AHundredPliesWithNoCaptureOrPawnMove_EndsTheGameAsADraw()
        {
            BoardState board = ShufflingEndgame();
            board.PushPosition(board.ZobristHash, irreversible: true);

            // Ninety-nine quiet plies on the record, each a different position so nothing is claimed
            // as a repetition before the count is the thing being tested.
            for (int i = 0; i < 99; i++)
            {
                board.PushPosition((ulong)(0x5000 + i), irreversible: false);
            }

            Assert.That(board.PliesSinceIrreversibleMove, Is.EqualTo(99));
            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.EqualTo(GameState.Normal), "Ninety-nine is not yet fifty moves each.");

            board.PushPosition(0x6000, irreversible: false);

            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.EqualTo(GameState.DrawByFiftyMoveRule));
        }

        [Test]
        public void ACaptureOrPawnMove_StartsTheFiftyMoveCountAgain()
        {
            BoardState board = ShufflingEndgame();
            board.PushPosition(board.ZobristHash, irreversible: true);

            for (int i = 0; i < 99; i++)
            {
                board.PushPosition((ulong)(0x5000 + i), irreversible: false);
            }

            board.PushPosition(0x6000, irreversible: true); // somebody finally took something

            Assert.That(board.PliesSinceIrreversibleMove, Is.Zero);
            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.EqualTo(GameState.Normal),
                "The count runs from the last thing that changed the material on the board, not from "
                + "the start of the game.");
        }

        [Test]
        public void TakingBackAMove_TakesBackThePositionItRecorded()
        {
            BoardState board = ShufflingEndgame();
            board.PushPosition(board.ZobristHash, irreversible: true);

            Play(board, "h1", "g1");
            Play(board, "h8", "g8");
            Play(board, "g1", "h1");
            Play(board, "g8", "h8");
            Play(board, "h1", "g1");
            Play(board, "h8", "g8");
            Play(board, "g1", "h1");

            int beforeTheLastMove = board.PositionCount;
            var legal = new List<MoveCommand>();
            _engine.GetLegalMoves(board, TestBoardSetupUtility.AlgebraicToVector("g8"), legal);

            MoveCommand closing = default;
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].EndPosition == TestBoardSetupUtility.AlgebraicToVector("h8")) closing = legal[i];
            }

            _engine.Advance(board, closing);
            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.EqualTo(GameState.DrawByRepetition), "That move completed the third occurrence.");

            _engine.UndoMove(board, closing);

            Assert.That(board.PositionCount, Is.EqualTo(beforeTheLastMove),
                "Taking a move back has to take back the position it put on the record, or a game "
                + "carries the positions of moves nobody ended up playing.");
            Assert.That(_engine.EvaluateGameState(board, board.CurrentTurn, null),
                Is.Not.EqualTo(GameState.DrawByRepetition),
                "and the draw that move created has to go with it.");
        }
    }
}
