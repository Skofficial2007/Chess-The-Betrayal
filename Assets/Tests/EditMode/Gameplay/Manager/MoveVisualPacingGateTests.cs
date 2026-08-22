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
