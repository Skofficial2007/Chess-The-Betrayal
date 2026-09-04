using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// MoveVisualPacingGate is the seam every move source (human input, AI, future network) now
    /// enqueues through instead of calling MatchDriver.PlayMove directly, so a fast decision-maker
    /// (the AI, especially post-search-performance-work) can't outrun the previous move's on-board
    /// animation. These tests drive it with a fake estimator/playMove capture so pacing behavior is
    /// provable without any real animation or Unity scene.
    /// </summary>
    [TestFixture]
    public class MoveVisualPacingGateTests
    {
        private static MoveCommand MakeMove(int seed)
        {
            var piece = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1, hasMoved: false);
            return MoveCommand.CreateStandardMove(new Vector2Int(seed % 8, 1), new Vector2Int(seed % 8, 3), piece);
        }

        private List<MoveCommand> _playedMoves;
        private MoveVisualPacingGate _gate;

        [SetUp]
        public void Setup()
        {
            _playedMoves = new List<MoveCommand>();
            _gate = new MoveVisualPacingGate(move => _playedMoves.Add(move), move => 1f);
        }

        [Test]
        public void Enqueue_PlaysImmediatelyWhenIdle()
        {
            MoveCommand move = MakeMove(0);
            _gate.Enqueue(move);

            Assert.That(_playedMoves, Has.Count.EqualTo(1));
            Assert.That(_playedMoves[0], Is.EqualTo(move));
        }

        [Test]
        public void Enqueue_SecondMoveWaitsUntilFirstsPacingElapses()
        {
            MoveCommand first = MakeMove(0);
            MoveCommand second = MakeMove(1);

            _gate.Enqueue(first);
            _gate.Enqueue(second);

            Assert.That(_playedMoves, Has.Count.EqualTo(1), "Second move must not play while the first is still pacing.");

            _gate.Tick(0.5f);
            Assert.That(_playedMoves, Has.Count.EqualTo(1), "Half the pacing window has elapsed — still not enough.");

            _gate.Tick(0.5f);
            Assert.That(_playedMoves, Has.Count.EqualTo(2), "A full second has now elapsed — the queued move plays.");
            Assert.That(_playedMoves[1], Is.EqualTo(second));
        }

        [Test]
        public void Enqueue_NeverDropsAMove_PlaysAllInOrder()
        {
            MoveCommand a = MakeMove(0);
            MoveCommand b = MakeMove(1);
            MoveCommand c = MakeMove(2);

            _gate.Enqueue(a);
            _gate.Enqueue(b);
            _gate.Enqueue(c);

            _gate.Tick(1f);
            _gate.Tick(1f);

            Assert.That(_playedMoves, Is.EqualTo(new[] { a, b, c }));
        }

        [Test]
        public void Tick_WithNothingPendingOrPacing_DoesNothing()
        {
            _gate.Tick(5f);
            Assert.That(_playedMoves, Is.Empty);
        }

        [Test]
        public void IsPacing_TrueWhileWindowActive_FalseOnceDrained()
        {
            _gate.Enqueue(MakeMove(0));
            Assert.That(_gate.IsPacing, Is.True);

            _gate.Tick(1f);
            Assert.That(_gate.IsPacing, Is.False);
        }

        /// <summary>
        /// Undo's half of the contract. A queued move was decided against a position that an undo
        /// destroys, so it must be thrown away rather than played once the window elapses — and the
        /// gate has to reopen immediately, or the player's own next move would sit behind the
        /// remains of a pacing window belonging to a move that never happened.
        /// </summary>
        /// <summary>
        /// A Betrayal Act that ends in a Defection leaves the board with a piece swap still to
        /// play, and no move command can say it is coming - only the driver applying the Act finds
        /// that out. Whoever learns of it holds the gate for it, and the point of holding is that
        /// the move behind it genuinely waits.
        /// </summary>
        [Test]
        public void HoldFor_KeepsTheNextMoveWaitingBeyondTheOneInFront()
        {
            _gate.Enqueue(MakeMove(0));
            _gate.Enqueue(MakeMove(1));

            _gate.HoldFor(0.5f);

            _gate.Tick(1f);
            Assert.That(_playedMoves, Has.Count.EqualTo(1),
                "The move in front had a second budgeted and the swap adds half of one on top.");

            _gate.Tick(0.5f);
            Assert.That(_playedMoves, Has.Count.EqualTo(2), "and then it goes.");
        }

        [Test]
        public void HoldFor_OnAnIdleGate_StillMakesTheNextMoveWait()
        {
            _gate.HoldFor(0.5f);
            _gate.Enqueue(MakeMove(0));

            Assert.That(_playedMoves, Is.Empty, "Work the board picked up is waited out either way.");

            _gate.Tick(0.5f);
            Assert.That(_playedMoves, Has.Count.EqualTo(1));
        }

        [Test]
        public void HoldFor_NothingOrLess_ChangesNothing()
        {
            _gate.Enqueue(MakeMove(0));
            _gate.Enqueue(MakeMove(1));

            _gate.HoldFor(0f);
            _gate.HoldFor(-5f);

            _gate.Tick(1f);
            Assert.That(_playedMoves, Has.Count.EqualTo(2), "The move in front kept exactly its own second.");
        }

        [Test]
        public void Clear_DropsQueuedMovesAndReopensTheGate()
        {
            MoveCommand played = MakeMove(0);
            MoveCommand abandoned = MakeMove(1);

            _gate.Enqueue(played);
            _gate.Enqueue(abandoned);
            Assert.That(_playedMoves, Has.Count.EqualTo(1));

            _gate.Clear();

            Assert.That(_gate.IsPacing, Is.False, "Clearing must reopen the gate, not just empty the queue.");

            _gate.Tick(5f);
            Assert.That(_playedMoves, Is.EqualTo(new[] { played }),
                "The queued move must never be played after a Clear.");

            MoveCommand afterUndo = MakeMove(2);
            _gate.Enqueue(afterUndo);
            Assert.That(_playedMoves, Is.EqualTo(new[] { played, afterUndo }),
                "The next move must play immediately — the cleared gate holds no leftover window.");
        }

        [Test]
        public void Enqueue_UsesPerMoveEstimatedDuration()
        {
            var variableGate = new MoveVisualPacingGate(
                move => _playedMoves.Add(move),
                move => move.IsCapture ? 2f : 0.5f);

            var attacker = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1, hasMoved: false);
            var victim = new PieceData(Team.Black, ChessPieceType.Pawn, moveDirection: -1, startRow: 6, hasMoved: false);
            MoveCommand capture = MoveCommand.CreateStandardMove(new Vector2Int(0, 1), new Vector2Int(0, 2), attacker, victim);
            MoveCommand quiet = MakeMove(1);

            variableGate.Enqueue(capture);
            variableGate.Enqueue(quiet);

            variableGate.Tick(1.5f);
            Assert.That(_playedMoves, Has.Count.EqualTo(1), "Capture's 2s pacing window hasn't elapsed yet.");

            variableGate.Tick(0.5f);
            Assert.That(_playedMoves, Has.Count.EqualTo(2));
        }
    }
}
