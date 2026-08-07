using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.View;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.View
{
    /// <summary>
    /// Which captures get walked into and which are struck from where the piece stands. The rule
    /// has to hold for every piece, because getting it wrong on one of them puts a run-up onto a
    /// square that is not on any path the piece could have taken.
    ///
    /// Squares are named the way the board is indexed: file 0-7, rank 0-7.
    /// </summary>
    [TestFixture]
    public class CaptureApproachTests
    {
        private static Vector2Int Sq(int file, int rank) => new Vector2Int(file, rank);

        private static Vector2Int StagingFor(Vector2Int from, Vector2Int to, ChessPieceType type)
        {
            Assert.That(CaptureApproach.TryPlanStagingSquare(from, to, type, out Vector2Int staging), Is.True,
                "Expected this capture to be walked into.");
            return staging;
        }

        private static void AssertStrikesInPlace(Vector2Int from, Vector2Int to, ChessPieceType type)
        {
            Assert.That(CaptureApproach.TryPlanStagingSquare(from, to, type, out Vector2Int staging), Is.False);
            Assert.That(staging, Is.EqualTo(Vector2Int.Invalid), "A capture with no run-up must not hand back a square.");
        }

        #region Captures that get walked into

        [Test]
        public void ABishopCrossingTheBoardStrikesFromTheSquareBeforeItsVictim()
        {
            // c1 to g5 — the move that made the leap read as a teleport. The bishop should arrive
            // at f4 and pounce from there.
            Assert.That(StagingFor(Sq(2, 0), Sq(6, 4), ChessPieceType.Bishop), Is.EqualTo(Sq(5, 3)));
        }

        [Test]
        public void ARookRunningALineStrikesFromTheSquareBeforeItsVictim()
        {
            Assert.That(StagingFor(Sq(0, 0), Sq(0, 7), ChessPieceType.Rook), Is.EqualTo(Sq(0, 6)));
            Assert.That(StagingFor(Sq(7, 3), Sq(1, 3), ChessPieceType.Rook), Is.EqualTo(Sq(2, 3)));
        }

        [Test]
        public void ASliderTwoSquaresOutStillWalksInTheOneSquareBetweenThem()
        {
            // The boundary: one square away is already close enough, two is not. Getting this off
            // by one either strands the shortest run-up or removes it from the closest captures
            // that still need it.
            Assert.That(StagingFor(Sq(2, 0), Sq(4, 2), ChessPieceType.Bishop), Is.EqualTo(Sq(3, 1)));
            Assert.That(StagingFor(Sq(0, 4), Sq(2, 4), ChessPieceType.Rook), Is.EqualTo(Sq(1, 4)));
        }

        [Test]
        public void AQueenIsWalkedInAlongEitherKindOfLine()
        {
            Assert.That(StagingFor(Sq(3, 0), Sq(3, 6), ChessPieceType.Queen), Is.EqualTo(Sq(3, 5)));
            Assert.That(StagingFor(Sq(0, 7), Sq(5, 2), ChessPieceType.Queen), Is.EqualTo(Sq(4, 3)));
        }

        [Test]
        public void TheStagingSquareIsAlwaysNextToTheVictim()
        {
            // The whole point of the run-up is that the strike happens at close quarters. If the
            // staging square were ever further out, the leap would still be stretched.
            foreach (ChessPieceType type in new[] { ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Queen })
            {
                for (int file = 0; file < 8; file++)
                {
                    for (int rank = 0; rank < 8; rank++)
                    {
                        Vector2Int from = Sq(file, rank);
                        Vector2Int to = Sq(4, 4);
                        if (!CaptureApproach.TryPlanStagingSquare(from, to, type, out Vector2Int staging)) continue;

                        Assert.That(SquaresBetween(staging, to), Is.EqualTo(1),
                            $"{type} from {file},{rank} staged at {staging.x},{staging.y}, which is not beside its victim.");
                    }
                }
            }
        }

        [Test]
        public void TheStagingSquareIsAlwaysOnTheBoard()
        {
            // It sits one step back toward the attacker, so it can never fall off the edge — but
            // an off-board square would be silently drawn at a tile that does not exist.
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    if (!CaptureApproach.TryPlanStagingSquare(Sq(file, rank), Sq(3, 5), ChessPieceType.Queen, out Vector2Int staging)) continue;

                    Assert.That(staging.x, Is.InRange(0, 7));
                    Assert.That(staging.y, Is.InRange(0, 7));
                }
            }
        }

        #endregion

        #region Captures struck from where the piece stands

        [Test]
        public void APawnTakingTheSquareDiagonallyAheadStrikesWhereItStands()
        {
            // The close-quarters case that already reads well and must not change.
            AssertStrikesInPlace(Sq(4, 3), Sq(5, 4), ChessPieceType.Pawn);
        }

        [Test]
        public void AKingTakingItsNeighbourStrikesWhereItStands()
        {
            AssertStrikesInPlace(Sq(4, 0), Sq(4, 1), ChessPieceType.King);
        }

        [Test]
        public void ASlidingPieceTakingItsNeighbourStrikesWhereItStands()
        {
            AssertStrikesInPlace(Sq(2, 0), Sq(3, 1), ChessPieceType.Bishop);
            AssertStrikesInPlace(Sq(0, 0), Sq(0, 1), ChessPieceType.Rook);
            AssertStrikesInPlace(Sq(3, 3), Sq(4, 3), ChessPieceType.Queen);
        }

        [Test]
        public void AKnightNeverWalksIn()
        {
            // A knight leaps; closing on the ground is the one thing it does not do. It is also the
            // only piece whose approach square is not guaranteed empty — the rules clear the path
            // for a slider, but a knight jumps over whatever is in the way.
            AssertStrikesInPlace(Sq(1, 0), Sq(2, 2), ChessPieceType.Knight);
            AssertStrikesInPlace(Sq(4, 4), Sq(6, 5), ChessPieceType.Knight);
            AssertStrikesInPlace(Sq(3, 3), Sq(1, 2), ChessPieceType.Knight);
        }

        [Test]
        public void APieceThatDidNotMoveStrikesWhereItStands()
        {
            AssertStrikesInPlace(Sq(4, 4), Sq(4, 4), ChessPieceType.Queen);
        }

        #endregion

        private static int SquaresBetween(Vector2Int a, Vector2Int b)
        {
            return ChessTheBetrayal.Core.Match.MoveTravelTiming.SquaresApart(a.x, a.y, b.x, b.y);
        }
    }
}
