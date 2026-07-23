using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Every positional assertion here calls KingSafety.Score directly, which has no personality-
    /// weight scaling of its own (the same reasoning PawnStructureEvaluationTests uses) — isolating
    /// the term itself from any attack/defense dial noise, since BetrayalAwareEvaluator still scales
    /// the result by DefenseScale before adding it to the total.
    /// </summary>
    [TestFixture]
    public class KingSafetyEvaluationTests
    {
        // Fully shields all three of the White king's files (f/g/h) so open-file danger is zero in
        // every board below unless a test explicitly removes a pawn to expose one.
        private static BoardState ShieldedWhiteKing() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e8", Team.Black, ChessPieceType.King); // far from g1's zone either way

        [Test]
        public void ZoneAttackers_NoEnemyPieceInRange_ScoresZero()
        {
            BoardState board = ShieldedWhiteKing();

            Assert.That(KingSafety.Score(board, Team.White), Is.EqualTo(0));
        }

        [Test]
        public void ZoneAttackers_OneEnemyPieceInRange_ScoresLowerThanNone()
        {
            BoardState none = ShieldedWhiteKing();
            BoardState one = ShieldedWhiteKing().WithPiece("f3", Team.Black, ChessPieceType.Knight); // Chebyshev 2 from g1

            Assert.That(KingSafety.Score(one, Team.White), Is.LessThan(KingSafety.Score(none, Team.White)));
        }

        [Test]
        public void ZoneAttackers_TwoEnemyPiecesInRange_ScoresLowerThanOne()
        {
            BoardState one = ShieldedWhiteKing().WithPiece("f3", Team.Black, ChessPieceType.Knight);
            BoardState two = ShieldedWhiteKing()
                .WithPiece("f3", Team.Black, ChessPieceType.Knight)
                .WithPiece("h3", Team.Black, ChessPieceType.Bishop); // also Chebyshev 2 from g1

            Assert.That(KingSafety.Score(two, Team.White), Is.LessThan(KingSafety.Score(one, Team.White)));
        }

        [Test]
        public void ZoneAttackers_AHeavierPieceInRange_ScoresLowerThanALighterPiece()
        {
            BoardState knightInRange = ShieldedWhiteKing().WithPiece("f3", Team.Black, ChessPieceType.Knight);
            BoardState queenInRange = ShieldedWhiteKing().WithPiece("f3", Team.Black, ChessPieceType.Queen);

            Assert.That(KingSafety.Score(queenInRange, Team.White), Is.LessThan(KingSafety.Score(knightInRange, Team.White)),
                "A Queen massing near the king must be weighted as more dangerous than a Knight in the same square.");
        }

        [Test]
        public void ZoneAttackers_PieceOutsideTheZone_ContributesNothing()
        {
            BoardState none = ShieldedWhiteKing();
            BoardState farAway = ShieldedWhiteKing().WithPiece("f4", Team.Black, ChessPieceType.Queen); // Chebyshev 3 from g1

            Assert.That(KingSafety.Score(farAway, Team.White), Is.EqualTo(KingSafety.Score(none, Team.White)),
                "A piece just past the zone radius must not be counted, even a Queen.");
        }

        [Test]
        public void OpenFile_KingWithNoPawnCover_ScoresLowerThanAShieldedKing()
        {
            BoardState shielded = ShieldedWhiteKing();
            BoardState bare = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King);

            Assert.That(KingSafety.Score(bare, Team.White), Is.LessThan(KingSafety.Score(shielded, Team.White)));
        }

        [Test]
        public void OpenFile_OnlyTheKingsOwnAdjacentFilesCount()
        {
            // A friendly pawn far from the king's own file/adjacent-files (a-file) does nothing for
            // g1's own exposure on f/g/h.
            BoardState bare = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King);
            BoardState irrelevantPawn = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e8", Team.Black, ChessPieceType.King);

            Assert.That(KingSafety.Score(irrelevantPawn, Team.White), Is.EqualTo(KingSafety.Score(bare, Team.White)));
        }

        [Test]
        public void DefectorNearKing_OwnPendingBetrayerInOwnZone_ScoresLowerThanWithoutIt()
        {
            // White's own Knight on f3 is the pending Betrayer, initiated by White itself, sitting
            // inside White's own king zone -- one Defection away from reappearing as a Black piece on
            // that exact square with no time spent getting there. The crafted sign-change case the
            // DoD requires: the SAME piece placement scores strictly worse for White once it is
            // flagged as a pending self-Betrayer than it does as an ordinary friendly Knight.
            BoardState withoutPending = ShieldedWhiteKing().WithPiece("f3", Team.White, ChessPieceType.Knight);
            BoardState withPending = ShieldedWhiteKing()
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPendingBetrayer("f3", Team.White)
                .WithComputedHash();

            Assert.That(KingSafety.Score(withPending, Team.White), Is.LessThan(KingSafety.Score(withoutPending, Team.White)));
        }

        [Test]
        public void DefectorNearKing_PendingBetrayerOutsideTheZone_AddsNoDanger()
        {
            BoardState withoutPending = ShieldedWhiteKing().WithPiece("a1", Team.White, ChessPieceType.Rook);
            BoardState withPending = ShieldedWhiteKing()
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPendingBetrayer("a1", Team.White)
                .WithComputedHash();

            Assert.That(KingSafety.Score(withPending, Team.White), Is.EqualTo(KingSafety.Score(withoutPending, Team.White)),
                "A pending Betrayer far from the king must not trigger the defector-tempo term.");
        }

        [Test]
        public void DefectorNearKing_EnemyInitiatedBetrayalInOwnZone_AddsNoDanger()
        {
            // The Betrayer at f3 belongs to BLACK (it has not defected yet -- it still occupies its
            // pre-Defection square and team). From White's own king-safety point of view this piece
            // is an ordinary enemy occupant of the zone today; it is about to become WHITE's own piece
            // if it defects, which cannot make White's king MORE exposed. The defector term must key
            // off BetrayalInitiator == the team being scored, not merely "is Black's piece nearby."
            BoardState ordinaryEnemyPiece = ShieldedWhiteKing().WithPiece("f3", Team.Black, ChessPieceType.Knight);
            BoardState enemyInitiatedPending = ShieldedWhiteKing()
                .WithPiece("f3", Team.Black, ChessPieceType.Knight)
                .WithPendingBetrayer("f3", Team.Black)
                .WithComputedHash();

            Assert.That(KingSafety.Score(enemyInitiatedPending, Team.White), Is.EqualTo(KingSafety.Score(ordinaryEnemyPiece, Team.White)),
                "An enemy-initiated Betrayal sequence must score identically to an ordinary enemy piece in the zone -- no extra danger from a piece about to become the scored team's own.");
        }

        [Test]
        public void Score_KingNotFound_ReturnsZero()
        {
            // Defensive floor: a board with no king for the requested team (never happens in a real
            // game, but the term must not throw or misbehave if TryFindKing ever fails).
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e8", Team.Black, ChessPieceType.King);

            Assert.That(KingSafety.Score(board, Team.White), Is.EqualTo(0));
        }
    }
}
