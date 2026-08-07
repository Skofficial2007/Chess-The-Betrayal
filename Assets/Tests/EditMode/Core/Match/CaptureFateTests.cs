using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// Which death a captured piece plays. This used to be decided halfway down the branch that
    /// animates the attacker, where a capture that also promoted walked off without burying
    /// anything — the victim was left standing, belonging to no collection, surviving even the
    /// teardown that clears a match. So the case that matters most here is the boring-looking one:
    /// a capture that is also a promotion.
    /// </summary>
    [TestFixture]
    public class CaptureFateTests
    {
        private static Vector2Int Sq(int file, int rank) => new Vector2Int(file, rank);

        private static PieceData Pawn(Team team = Team.White)
            => new PieceData(team, ChessPieceType.Pawn, moveDirection: team == Team.White ? 1 : -1, startRow: 1, hasMoved: true);

        private static PieceData Piece(ChessPieceType type)
            => new PieceData(Team.White, type, moveDirection: 1, startRow: 0, hasMoved: true);

        private static PieceData Victim() => Pawn(Team.Black);

        [Test]
        public void AQuietMoveTakesNothing()
        {
            MoveCommand move = MoveCommand.CreateStandardMove(Sq(0, 0), Sq(0, 4), Piece(ChessPieceType.Rook));

            Assert.That(CaptureFate.Of(move), Is.EqualTo(CapturedPieceFate.NothingCaptured));
        }

        [Test]
        public void AnOrdinaryCaptureIsCrushedByTheAttackerLandingOnIt()
        {
            MoveCommand move = MoveCommand.CreateStandardMove(Sq(0, 0), Sq(0, 4), Piece(ChessPieceType.Rook), Victim());

            Assert.That(CaptureFate.Of(move), Is.EqualTo(CapturedPieceFate.CrushedByTheStamp));
        }

        [Test]
        public void ACaptureThatAlsoPromotesDiesOnItsOwn()
        {
            // The defect. The attacker is replaced by the promoted piece the instant it arrives, so
            // there is nothing left to do the stomping — and the branch that animates a promotion
            // runs before the branch that would have buried this piece.
            MoveCommand move = MoveCommand.CreatePromotionMove(
                Sq(4, 6), Sq(5, 7), Pawn(), ChessPieceType.Queen, Victim());

            Assert.That(CaptureFate.Of(move), Is.EqualTo(CapturedPieceFate.DiesOnItsOwn));
        }

        [Test]
        public void EnPassantDiesOnItsOwnBecauseNothingLandsOnIt()
        {
            MoveCommand move = MoveCommand.CreateEnPassantMove(
                Sq(4, 4), Sq(5, 5), Pawn(), Victim(), Sq(5, 4));

            Assert.That(CaptureFate.Of(move), Is.EqualTo(CapturedPieceFate.DiesOnItsOwn));
        }

        [Test]
        public void APromotionThatTakesNothingHasNoVictimAtAll()
        {
            MoveCommand move = MoveCommand.CreatePromotionMove(
                Sq(4, 6), Sq(4, 7), Pawn(), ChessPieceType.Queen);

            Assert.That(CaptureFate.Of(move), Is.EqualTo(CapturedPieceFate.NothingCaptured));
        }

        [Test]
        public void ACastleTakesNothing()
        {
            MoveCommand move = MoveCommand.CreateCastlingMove(
                Sq(4, 0), Sq(6, 0), Piece(ChessPieceType.King), Sq(7, 0), Sq(5, 0));

            Assert.That(CaptureFate.Of(move), Is.EqualTo(CapturedPieceFate.NothingCaptured));
        }

        [Test]
        public void ABetrayalActIsStillAnOrdinaryCaptureToLookAt()
        {
            // The betrayer takes one of its own pieces and does land on it, so it stomps like any
            // other capture. An Act never promotes — that is deliberate, a betrayer is not rewarded
            // for turning on its own side — so it can never reach the promotion case above.
            MoveCommand act = MoveCommand
                .CreateStandardMove(Sq(3, 3), Sq(4, 4), Piece(ChessPieceType.Bishop), Pawn())
                .WithStage(BetrayalStage.Act);

            Assert.That(CaptureFate.Of(act), Is.EqualTo(CapturedPieceFate.CrushedByTheStamp));
        }

        [Test]
        public void EveryCaptureIsGivenSomeWayToDie()
        {
            // The one guarantee the whole thing rests on: a piece taken off the board is always
            // handed to something that will bury it. Anything answering NothingCaptured for a real
            // capture puts the leak straight back.
            MoveCommand[] captures =
            {
                MoveCommand.CreateStandardMove(Sq(0, 0), Sq(0, 4), Piece(ChessPieceType.Rook), Victim()),
                MoveCommand.CreateStandardMove(Sq(3, 3), Sq(4, 4), Piece(ChessPieceType.King), Victim()),
                MoveCommand.CreatePromotionMove(Sq(4, 6), Sq(5, 7), Pawn(), ChessPieceType.Queen, Victim()),
                MoveCommand.CreatePromotionMove(Sq(4, 6), Sq(5, 7), Pawn(), ChessPieceType.Knight, Victim()),
                MoveCommand.CreateEnPassantMove(Sq(4, 4), Sq(5, 5), Pawn(), Victim(), Sq(5, 4)),
            };

            foreach (MoveCommand move in captures)
            {
                Assert.That(CaptureFate.Of(move), Is.Not.EqualTo(CapturedPieceFate.NothingCaptured),
                    $"A capture from {move.StartPosition.x},{move.StartPosition.y} was left with no death to play.");
            }
        }
    }
}
