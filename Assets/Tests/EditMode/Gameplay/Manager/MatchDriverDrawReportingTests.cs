using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Events;
using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// What the rest of the game is told when a match ends in a draw. The panel that announces it
    /// can only be as right as the reason it is handed, and for as long as stalemate was the only
    /// draw in the game, "nobody won" was reported as a stalemate with nothing checking otherwise.
    /// </summary>
    [TestFixture]
    public class MatchDriverDrawReportingTests
    {
        private ChessEngineAdapter _engine;
        private GameOverEventChannel _channel;
        private readonly List<GameOverPayload> _raised = new List<GameOverPayload>();

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _raised.Clear();
            _channel = UnityEngine.ScriptableObject.CreateInstance<GameOverEventChannel>();
            _channel.Register(_raised.Add);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_channel);

        private MatchDriver DriverFor(BoardState board) =>
            new MatchDriver(_engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: _channel, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);

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
        public void CheckForGameEnd_OnARealThreefold_ReportsARepetitionAndNotAStalemate()
        {
            BoardState board = ShufflingEndgame();
            board.PushPosition(board.ZobristHash, irreversible: true);
            MatchDriver driver = DriverFor(board);

            for (int lap = 0; lap < 2; lap++)
            {
                Play(board, "h1", "g1");
                Play(board, "h8", "g8");
                Play(board, "g1", "h1");
                Play(board, "g8", "h8");
            }

            driver.CheckForGameEnd();

            Assert.That(_raised, Has.Count.EqualTo(1), "The game has to actually end.");
            Assert.That(_raised[0].WinningTeam, Is.Null);
            Assert.That(_raised[0].Reason, Is.EqualTo(GameEndReason.Repetition),
                "Two sides who shuffled back to the same position three times were told they had "
                + "been stalemated, which never happened.");
            Assert.That(board.IsGameOver, Is.True);
        }

        [Test]
        public void CheckForGameEnd_AfterFiftyMovesOfNothing_ReportsTheFiftyMoveRule()
        {
            BoardState board = ShufflingEndgame();
            board.PushPosition(board.ZobristHash, irreversible: true);

            // A hundred quiet plies, each recorded as a position of its own so the count is the only
            // thing that can end this game and a repetition cannot get there first.
            for (int i = 0; i < 100; i++)
            {
                board.PushPosition((ulong)(0x5000 + i), irreversible: false);
            }

            DriverFor(board).CheckForGameEnd();

            Assert.That(_raised, Has.Count.EqualTo(1));
            Assert.That(_raised[0].WinningTeam, Is.Null);
            Assert.That(_raised[0].Reason, Is.EqualTo(GameEndReason.FiftyMoveRule));
        }

        /// <summary>
        /// The reason a stalemate still has to arrive as one: the draw reason is optional on the way
        /// through, and a default that guessed wrong would rename every stalemate in the game.
        /// </summary>
        [Test]
        public void CheckForGameEnd_OnAStalemate_StillReportsAStalemate()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("c7", Team.White, ChessPieceType.King)
                .WithPiece("b6", Team.White, ChessPieceType.Queen)
                .WithTurn(Team.Black)
                .WithBetrayalRight(false)
                .WithComputedHash();

            DriverFor(board).CheckForGameEnd();

            Assert.That(_raised, Has.Count.EqualTo(1));
            Assert.That(_raised[0].Reason, Is.EqualTo(GameEndReason.Stalemate));
        }

        /// <summary>
        /// A won game is won. The draw checks sit after mate for a reason, and this is the side of
        /// that ordering the player would notice: a mate delivered in a position the two sides had
        /// already been through three times is still a mate.
        /// </summary>
        [Test]
        public void CheckForGameEnd_OnAMateInAPositionAlreadyRepeated_ReportsACheckmate()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("b6", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.Black)
                .WithBetrayalRight(false)
                .WithComputedHash();

            board.PushPosition(board.ZobristHash, irreversible: true);
            board.PushPosition(board.ZobristHash, irreversible: false);
            board.PushPosition(board.ZobristHash, irreversible: false);

            DriverFor(board).CheckForGameEnd();

            Assert.That(_raised, Has.Count.EqualTo(1));
            Assert.That(_raised[0].Reason, Is.EqualTo(GameEndReason.Checkmate));
            Assert.That(_raised[0].WinningTeam, Is.EqualTo(Team.White),
                "and the side that delivered it has to be the one that wins.");
        }
    }
}
