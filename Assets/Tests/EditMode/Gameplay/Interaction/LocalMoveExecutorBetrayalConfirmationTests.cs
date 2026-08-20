using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Gameplay.Interaction;
using ChessTheBetrayal.Tooling;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Interaction
{
    /// <summary>
    /// Turning one of your own pieces used to take a single tap on a square that looks no different
    /// from an enemy's, and it spends a right neither side gets back. The executor now holds that
    /// move until somebody answers for it.
    ///
    /// Two things are worth more than the happy path here. One: only the Act waits — if an ordinary
    /// move or capture started waiting too, every turn in the game would need a button press. Two:
    /// the answer can arrive a long time after the question, on a board that has moved on, so what
    /// finally reaches the board is worked out again rather than replayed.
    /// </summary>
    [TestFixture]
    public class LocalMoveExecutorBetrayalConfirmationTests
    {
        private static readonly Vector2Int D1 = BoardSetup.AlgebraicToVector("d1");
        private static readonly Vector2Int D2 = BoardSetup.AlgebraicToVector("d2");
        private static readonly Vector2Int C1 = BoardSetup.AlgebraicToVector("c1");
        private static readonly Vector2Int G4 = BoardSetup.AlgebraicToVector("g4");

        private BoardState _board;
        private LocalMoveExecutor _executor;
        private TurnPhase _phase;
        private StubClock _clock;

        private readonly List<MoveCommand> _confirmed = new List<MoveCommand>();
        private readonly List<(Vector2Int from, Vector2Int to)> _rejected = new List<(Vector2Int, Vector2Int)>();
        private readonly List<(Vector2Int from, Vector2Int to)> _asked = new List<(Vector2Int, Vector2Int)>();

        /// <summary>
        /// White's queen on d1 attacks her own pawn on d2, which is the Act. She can also step to an
        /// empty c1 and take the knight on g4, which are the two ordinary moves that must never be
        /// made to wait. Kings on both sides because every move is checked against them.
        /// </summary>
        private static BoardState QueenCanBetrayHerOwnPawn() =>
            BoardSetup.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g4", Team.Black, ChessPieceType.Knight)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

        [SetUp]
        public void SetUp()
        {
            _confirmed.Clear();
            _rejected.Clear();
            _asked.Clear();
            _phase = TurnPhase.Normal;
            _clock = new StubClock();

            UseBoard(QueenCanBetrayHerOwnPawn());
        }

        private void UseBoard(BoardState board)
        {
            _board = board;
            _executor = new LocalMoveExecutor(_board, new ChessEngineAdapter(), () => _phase, _clock, logMoves: false);
            _executor.OnMoveConfirmed += move => _confirmed.Add(move);
            _executor.OnMoveRejected += (from, to) => _rejected.Add((from, to));
            _executor.OnBetrayalActConfirmationRequired += (from, to) => _asked.Add((from, to));
        }

        [Test]
        public void RequestMove_ForALegalBetrayalAct_HoldsItAndAsksInstead()
        {
            _executor.RequestMove(D1, D2);

            Assert.That(_asked, Is.EqualTo(new[] { (D1, D2) }));
            Assert.That(_confirmed, Is.Empty, "Nothing may reach the board before the player has answered.");
            Assert.That(_rejected, Is.Empty, "Being asked about a move is not the same as being told it was illegal.");
        }

        [Test]
        public void RequestMove_ForAnOrdinaryMove_GoesStraightThrough()
        {
            _executor.RequestMove(D1, C1);

            Assert.That(_asked, Is.Empty, "Asking before every move would make the game unplayable.");
            Assert.That(_confirmed.Count, Is.EqualTo(1));
            Assert.That(_confirmed[0].Stage, Is.EqualTo(BetrayalStage.None));
        }

        [Test]
        public void RequestMove_ForAnOrdinaryCapture_GoesStraightThrough()
        {
            _executor.RequestMove(D1, G4);

            Assert.That(_asked, Is.Empty, "Taking an enemy piece is the ordinary business of a game of chess.");
            Assert.That(_confirmed.Count, Is.EqualTo(1));
            Assert.That(_confirmed[0].HasCapture, Is.True);
            Assert.That(_confirmed[0].Stage, Is.EqualTo(BetrayalStage.None));
        }

        /// <summary>
        /// The question is only worth asking about a move the player could actually make. An Act
        /// that opens a line onto your own king is illegal, and being asked to confirm it would
        /// suggest otherwise.
        /// </summary>
        [Test]
        public void RequestMove_ForABetrayalThatWouldExposeTheKing_IsTurnedDownWithoutAsking()
        {
            // The rook on b1 is all that stands between the black rook on h1 and the white king.
            // Betraying the pawn on b5 would take it off the back rank.
            UseBoard(BoardSetup.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Rook)
                .WithPiece("b5", Team.White, ChessPieceType.Pawn)
                .WithPiece("h1", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true));

            Vector2Int b1 = BoardSetup.AlgebraicToVector("b1");
            Vector2Int b5 = BoardSetup.AlgebraicToVector("b5");

            _executor.RequestMove(b1, b5);

            Assert.That(_asked, Is.Empty);
            Assert.That(_rejected, Is.EqualTo(new[] { (b1, b5) }));
            Assert.That(_confirmed, Is.Empty);
        }

        [Test]
        public void ConfirmBetrayalAct_PlaysTheActItWasHolding()
        {
            _executor.RequestMove(D1, D2);

            _executor.ConfirmBetrayalAct();

            Assert.That(_confirmed.Count, Is.EqualTo(1));
            Assert.That(_confirmed[0].Stage, Is.EqualTo(BetrayalStage.Act));
            Assert.That(_confirmed[0].StartPosition, Is.EqualTo(D1));
            Assert.That(_confirmed[0].EndPosition, Is.EqualTo(D2));
            Assert.That(_confirmed[0].CapturedType, Is.EqualTo(ChessPieceType.Pawn));
        }

        [Test]
        public void CancelBetrayalAct_PlaysNothingAndCallsNothingIllegal()
        {
            _executor.RequestMove(D1, D2);

            _executor.CancelBetrayalAct();

            Assert.That(_confirmed, Is.Empty);
            Assert.That(_rejected, Is.Empty,
                "A rejection is answered by snapping a piece back; backing out of a question is not that.");
        }

        [Test]
        public void CancelBetrayalAct_LeavesTheBoardTakingMovesAgain()
        {
            _executor.RequestMove(D1, D2);
            _executor.CancelBetrayalAct();

            _executor.RequestMove(D1, C1);

            Assert.That(_confirmed.Count, Is.EqualTo(1),
                "Changing your mind must not leave the player unable to move at all.");
            Assert.That(_confirmed[0].EndPosition, Is.EqualTo(C1));
        }

        [Test]
        public void AnsweringWhenNothingWasAsked_DoesNothingEitherWay()
        {
            _executor.ConfirmBetrayalAct();
            _executor.CancelBetrayalAct();

            Assert.That(_confirmed, Is.Empty);
            Assert.That(_rejected, Is.Empty);
        }

        /// <summary>
        /// A tap that lands while the question is still up must not quietly become the move that
        /// gets played when the player finally presses Continue.
        /// </summary>
        [Test]
        public void RequestMove_WhileAnActIsWaiting_IsTurnedDownAndLeavesTheHeldActAlone()
        {
            _executor.RequestMove(D1, D2);

            _executor.RequestMove(D1, C1);

            Assert.That(_rejected, Is.EqualTo(new[] { (D1, C1) }));
            Assert.That(_asked.Count, Is.EqualTo(1), "The question on screen is still the first one.");

            _executor.ConfirmBetrayalAct();

            Assert.That(_confirmed.Count, Is.EqualTo(1));
            Assert.That(_confirmed[0].EndPosition, Is.EqualTo(D2),
                "Confirming answered the Act that was asked about, not the tap that arrived afterwards.");
        }

        /// <summary>
        /// The whole reason the move is worked out again rather than replayed: a question can sit on
        /// screen for as long as the player leaves it there, and the position underneath it does not
        /// have to wait.
        /// </summary>
        [Test]
        public void ConfirmBetrayalAct_WhenTheVictimIsNoLongerThere_TurnsTheMoveDown()
        {
            _executor.RequestMove(D1, D2);

            _board.SetPiece(PieceData.Empty, D2.x, D2.y);

            _executor.ConfirmBetrayalAct();

            Assert.That(_confirmed, Is.Empty);
            Assert.That(_rejected, Is.EqualTo(new[] { (D1, D2) }));
        }

        [Test]
        public void ConfirmBetrayalAct_WhenTheTurnHasMovedOn_TurnsTheMoveDown()
        {
            _executor.RequestMove(D1, D2);

            _phase = TurnPhase.GameOver;

            _executor.ConfirmBetrayalAct();

            Assert.That(_confirmed, Is.Empty);
            Assert.That(_rejected, Is.EqualTo(new[] { (D1, D2) }));
        }

        /// <summary>
        /// The clock keeps running while the question is up, on purpose: deciding whether to betray
        /// is part of the move. So the time the move records is the time it was committed, not the
        /// time it was first considered.
        /// </summary>
        [Test]
        public void ConfirmBetrayalAct_StampsTheTimeTheAnswerCameIn()
        {
            _clock.State = new ClockState { WhiteRemainingMs = 90_000L, BlackRemainingMs = 90_000L };
            _executor.RequestMove(D1, D2);

            _clock.State = new ClockState { WhiteRemainingMs = 72_500L, BlackRemainingMs = 90_000L };
            _executor.ConfirmBetrayalAct();

            Assert.That(_confirmed[0].WhiteRemainingMsAtMove, Is.EqualTo(72_500L),
                "Thinking time spent staring at the question is the player's own.");
        }

        /// <summary>
        /// What happens when the player has turned this warning off: the answer comes back from
        /// inside the very call that asked. Nothing in the executor may depend on that raise
        /// returning before the move is played.
        /// </summary>
        [Test]
        public void RequestMove_WhenAnsweredFromInsideTheQuestion_StillPlaysTheAct()
        {
            _executor.OnBetrayalActConfirmationRequired += (_, __) => _executor.ConfirmBetrayalAct();

            Assert.DoesNotThrow(() => _executor.RequestMove(D1, D2));

            Assert.That(_confirmed.Count, Is.EqualTo(1));
            Assert.That(_confirmed[0].Stage, Is.EqualTo(BetrayalStage.Act));
        }

        [Test]
        public void RequestMove_AfterAnActWasPlayed_IsAcceptedAgain()
        {
            _executor.RequestMove(D1, D2);
            _executor.ConfirmBetrayalAct();
            _confirmed.Clear();

            // The Act itself has not been applied to this board — only confirmed — so the same
            // request is still the legal move it was. What matters is that the executor is no
            // longer holding anything.
            _executor.RequestMove(D1, C1);

            Assert.That(_rejected, Is.Empty);
            Assert.That(_confirmed.Count, Is.EqualTo(1));
        }
    }
}
