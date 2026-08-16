using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Gameplay.Interaction;
using ChessTheBetrayal.Tests.Utilities;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Interaction
{
    /// <summary>
    /// What the executor does once a Betrayal is already under way. Both sub-phases narrow the board
    /// to a handful of moves — the side that was betrayed may only execute the Betrayer, and a side
    /// left in check by a defection may only get out of it — so a tap that would be perfectly legal
    /// a moment earlier has to be turned down here.
    ///
    /// The positions are reached by playing the Act through the engine rather than by setting the
    /// pending-betrayer fields directly, so a test can never describe a board the game could not
    /// actually arrive at.
    /// </summary>
    [TestFixture]
    public class LocalMoveExecutorSubPhaseTests
    {
        private sealed class StubClock : IClockSnapshotSource
        {
            public ClockState State;
            public ClockState? Current => State;
        }

        private static Vector2Int Sq(string algebraic) => TestBoardSetupUtility.AlgebraicToVector(algebraic);

        private BoardState _board;
        private IChessEngine _engine;
        private LocalMoveExecutor _executor;
        private TurnPhase _phase;
        private StubClock _clock;

        private readonly List<MoveCommand> _confirmed = new List<MoveCommand>();
        private readonly List<(Vector2Int from, Vector2Int to)> _rejected = new List<(Vector2Int, Vector2Int)>();
        private readonly List<(Vector2Int from, Vector2Int to, bool isCapture)> _asked =
            new List<(Vector2Int, Vector2Int, bool)>();

        [SetUp]
        public void SetUp()
        {
            _confirmed.Clear();
            _rejected.Clear();
            _asked.Clear();
            _phase = TurnPhase.Normal;
            _clock = new StubClock();
            _engine = new ChessEngineAdapter();
        }

        private void UseBoard(BoardState board)
        {
            _board = board;
            _executor = new LocalMoveExecutor(_board, _engine, () => _phase, _clock, logMoves: false);
            _executor.OnMoveConfirmed += move => _confirmed.Add(move);
            _executor.OnMoveRejected += (from, to) => _rejected.Add((from, to));
            _executor.OnPromotionRequired += (from, to, isCapture) => _asked.Add((from, to, isCapture));
        }

        /// <summary>
        /// White's queen can turn on her own pawn directly in front of her, and White's own rook is
        /// waiting down the same file to execute her for it. The executioner is deliberately White:
        /// Retribution is carried out by the side that was betrayed, which is the betrayer's own.
        /// </summary>
        private static BoardState QueenBetraysWithARookWaiting() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                .WithPiece("d6", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        /// <summary>
        /// Plays the Act through the engine and leaves the executor sitting in whatever sub-phase
        /// the engine says comes next. Asserts its way there so a broken setup fails as a setup
        /// failure rather than as whatever the test was actually about.
        /// </summary>
        private void PlayTheActAndEnterTheNextPhase(string from, string to)
        {
            DomainResult<MoveCommand> act =
                MoveRequest.Resolve(_engine, _board, Sq(from), Sq(to), BetrayalStage.Act);
            Assert.That(act.IsSuccess, Is.True, $"Setup: {from} to {to} is meant to be a legal Act.");

            TurnAdvanceResult afterAct = _engine.Advance(_board, act.Value);
            _phase = afterAct.NextPhase;
        }

        [Test]
        public void Retribution_APieceThatCanReachTheBetrayer_IsConfirmed()
        {
            UseBoard(QueenBetraysWithARookWaiting());
            PlayTheActAndEnterTheNextPhase("d1", "d2");

            Assert.That(_phase, Is.EqualTo(TurnPhase.RetributionPending),
                "Setup: a waiting executioner means the betrayed side owes Retribution.");

            _executor.RequestMove(Sq("d6"), Sq("d2"));

            Assert.That(_rejected, Is.Empty);
            Assert.That(_confirmed.Count, Is.EqualTo(1), "The rook may execute the Betrayer.");
            Assert.That(_confirmed[0].StartPosition, Is.EqualTo(Sq("d6")));
            Assert.That(_confirmed[0].EndPosition, Is.EqualTo(Sq("d2")));
        }

        [Test]
        public void Retribution_AMoveAimedAnywhereButTheBetrayer_IsRefused()
        {
            UseBoard(QueenBetraysWithARookWaiting());
            PlayTheActAndEnterTheNextPhase("d1", "d2");

            // The rook is the executioner and d5 is an empty square it could reach any other turn.
            _executor.RequestMove(Sq("d6"), Sq("d5"));

            Assert.That(_confirmed, Is.Empty,
                "Nothing but the Betrayer may be played while Retribution is owed.");
            Assert.That(_rejected, Is.EqualTo(new[] { (Sq("d6"), Sq("d5")) }));
        }

        [Test]
        public void Retribution_APieceThatCannotReachTheBetrayer_IsRefused()
        {
            UseBoard(QueenBetraysWithARookWaiting());
            PlayTheActAndEnterTheNextPhase("d1", "d2");

            // Right target, wrong piece: the king on a1 is nowhere near d2.
            _executor.RequestMove(Sq("a1"), Sq("d2"));

            Assert.That(_confirmed, Is.Empty);
            Assert.That(_rejected, Is.EqualTo(new[] { (Sq("a1"), Sq("d2")) }));
        }

        /// <summary>
        /// White's rook reaches across the board to turn on the knight on e8, and the only piece that
        /// can execute it there is a pawn one square away from the last rank. Executing is a capture
        /// like any other, so it promotes — unlike the Act itself, which never does.
        /// </summary>
        private static BoardState RookBetraysWhereOnlyAPawnCanExecute() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.Black, ChessPieceType.King)
                .WithPiece("e1", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.White, ChessPieceType.Knight)
                .WithPiece("d7", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        [Test]
        public void Retribution_APawnExecutingOnTheLastRank_AsksWhatItBecomes()
        {
            UseBoard(RookBetraysWhereOnlyAPawnCanExecute());
            PlayTheActAndEnterTheNextPhase("e1", "e8");

            Assert.That(_phase, Is.EqualTo(TurnPhase.RetributionPending),
                "Setup: the pawn on d7 can reach the Betrayer, so Retribution is owed.");

            _executor.RequestMove(Sq("d7"), Sq("e8"));

            Assert.That(_confirmed, Is.Empty,
                "The move waits until the player has said what the pawn becomes.");
            Assert.That(_rejected, Is.Empty);
            Assert.That(_asked.Count, Is.EqualTo(1), "The player must be asked to choose a piece.");
            Assert.That(_asked[0].to, Is.EqualTo(Sq("e8")));
        }

        /// <summary>
        /// White's rook turns on the pawn shielding its own king, and no other White piece can reach
        /// the square to punish it. The engine settles the Defection there and then, which leaves the
        /// rook defected to Black and checking the king it just abandoned.
        /// </summary>
        private static BoardState BetrayalThatLeavesTheKingInCheck() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("e5", Team.White, ChessPieceType.Rook)
                .WithPiece("e3", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        [Test]
        public void ForcedSave_AMoveThatGetsTheKingOut_IsConfirmed()
        {
            UseBoard(BetrayalThatLeavesTheKingInCheck());
            PlayTheActAndEnterTheNextPhase("e5", "e3");

            Assert.That(_phase, Is.EqualTo(TurnPhase.ForcedSave),
                "Setup: the defected rook checks the king, so a save is owed.");

            _executor.RequestMove(Sq("e1"), Sq("d1"));

            Assert.That(_rejected, Is.Empty);
            Assert.That(_confirmed.Count, Is.EqualTo(1), "Stepping off the file answers the check.");
            Assert.That(_confirmed[0].EndPosition, Is.EqualTo(Sq("d1")));
        }

        [Test]
        public void ForcedSave_AMoveThatLeavesTheKingInCheck_IsRefused()
        {
            UseBoard(BetrayalThatLeavesTheKingInCheck());
            PlayTheActAndEnterTheNextPhase("e5", "e3");

            // e2 is a legal king step on any other turn, and here it stays on the checked file.
            _executor.RequestMove(Sq("e1"), Sq("e2"));

            Assert.That(_confirmed, Is.Empty);
            Assert.That(_rejected, Is.EqualTo(new[] { (Sq("e1"), Sq("e2")) }),
                "A save that does not save is not a legal move here.");
        }
    }
}
