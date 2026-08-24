using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.Gameplay.Manager;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// The sequencer decides only when each ply of a takeback is shown, so it can be driven with a
    /// fake clock and a capture list — no board, no animation, no Play mode. Every duration here is
    /// 1 second to keep the arithmetic obvious.
    /// </summary>
    [TestFixture]
    public class UndoPlaybackSequencerTests
    {
        private List<MoveUndonePayload> _announced;
        private UndoPlaybackSequencer _sequencer;

        [SetUp]
        public void Setup()
        {
            _announced = new List<MoveUndonePayload>();
            _sequencer = new UndoPlaybackSequencer(p => _announced.Add(p), move => 1f);
        }

        private static MoveCommand MoveFrom(int file)
        {
            var piece = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1, hasMoved: false);
            return MoveCommand.CreateStandardMove(new Vector2Int(file, 1), new Vector2Int(file, 3), piece);
        }

        private static List<MoveCommand> Plies(params int[] files)
        {
            var plies = new List<MoveCommand>();
            for (int i = 0; i < files.Length; i++) plies.Add(MoveFrom(files[i]));
            return plies;
        }

        [Test]
        public void Begin_AnnouncesTheFirstPlyImmediately()
        {
            _sequencer.Begin(Plies(0, 1), landsInCheck: false);

            Assert.That(_announced, Has.Count.EqualTo(1),
                "The takeback starts the moment it is asked for — nothing to wait behind yet.");
            Assert.That(_announced[0].Move.StartPosition.x, Is.EqualTo(0));
        }

        /// <summary>
        /// The point of the whole class: announcing every ply at once would put them all on the
        /// board in the same frame, which is the snap this replaces.
        /// </summary>
        [Test]
        public void Begin_HoldsLaterPliesUntilEachHasHadItsTime()
        {
            _sequencer.Begin(Plies(0, 1, 2), landsInCheck: false);

            Assert.That(_announced, Has.Count.EqualTo(1));

            _sequencer.Tick(0.5f);
            Assert.That(_announced, Has.Count.EqualTo(1), "Half a ply's worth of time is not enough.");

            _sequencer.Tick(0.5f);
            Assert.That(_announced, Has.Count.EqualTo(2));

            _sequencer.Tick(1f);
            Assert.That(_announced, Has.Count.EqualTo(3));
        }

        [Test]
        public void Begin_AnnouncesPliesInTheOrderGiven()
        {
            _sequencer.Begin(Plies(3, 5, 7), landsInCheck: false);
            _sequencer.Tick(1f);
            _sequencer.Tick(1f);

            Assert.That(_announced.ConvertAll(p => p.Move.StartPosition.x), Is.EqualTo(new[] { 3, 5, 7 }),
                "History has to be walked backwards in exactly the order the plies came off the board.");
        }

        [Test]
        public void Begin_MarksOnlyTheLastPlyAsFinal()
        {
            _sequencer.Begin(Plies(0, 1, 2), landsInCheck: false);
            _sequencer.Tick(1f);
            _sequencer.Tick(1f);

            Assert.That(_announced.ConvertAll(p => p.IsFinalPly), Is.EqualTo(new[] { false, false, true }));
        }

        /// <summary>
        /// Only the position actually landed on is described as check. The ones in between are
        /// passed through on the way, and a check frame flashing up mid-rewind would be describing
        /// a position the player never arrives at.
        /// </summary>
        [Test]
        public void Begin_ReportsCheckOnlyOnThePlyThatLandsThere()
        {
            _sequencer.Begin(Plies(0, 1), landsInCheck: true);
            _sequencer.Tick(1f);

            Assert.That(_announced[0].LandsInCheck, Is.False);
            Assert.That(_announced[1].LandsInCheck, Is.True);
        }

        [Test]
        public void IsPlayingBack_TrueUntilTheLastPlyHasBeenAnnounced()
        {
            Assert.That(_sequencer.IsPlayingBack, Is.False, "Nothing is being shown yet.");

            _sequencer.Begin(Plies(0, 1), landsInCheck: false);
            Assert.That(_sequencer.IsPlayingBack, Is.True);

            _sequencer.Tick(1f);
            Assert.That(_sequencer.IsPlayingBack, Is.True, "The last ply still owns its own time on the board.");

            _sequencer.Tick(1f);
            Assert.That(_sequencer.IsPlayingBack, Is.False);
        }

        [Test]
        public void Begin_WithNoPlies_ShowsNothing()
        {
            _sequencer.Begin(new List<MoveCommand>(), landsInCheck: false);

            Assert.That(_announced, Is.Empty);
            Assert.That(_sequencer.IsPlayingBack, Is.False);
        }

        [Test]
        public void Clear_AbandonsATakebackStillBeingShown()
        {
            _sequencer.Begin(Plies(0, 1, 2), landsInCheck: false);

            _sequencer.Clear();

            Assert.That(_sequencer.IsPlayingBack, Is.False);

            _sequencer.Tick(5f);
            Assert.That(_announced, Has.Count.EqualTo(1),
                "A match that ended or restarted must not keep announcing plies from the previous one.");
        }
    }
}
