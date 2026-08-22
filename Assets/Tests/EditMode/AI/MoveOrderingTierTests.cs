using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the concrete OrderScore tier bands (ADR Sec 2.2): TT/PV move first, then winning
    /// captures, then promotions/equal captures, then Act, then quiets/losing captures last. This
    /// is an ORDERING-ONLY change — SearchCorrectnessTests (chosen moves) must stay green alongside
    /// these, proving demoting Act never changes which move the search ultimately picks.
    /// </summary>
    [TestFixture]
    public class MoveOrderingTierTests
    {
        private static readonly Vector2Int From = TestBoardSetupUtility.AlgebraicToVector("a2");
        private static readonly Vector2Int To = TestBoardSetupUtility.AlgebraicToVector("a3");

        // A distinct destination square for moves that must NOT pack-match the TT move under test —
        // PackMove keys on From/To/PromotedTo/Stage only (not capture status, by design: see the
        // ADR's PackedBestMove note), so a same-square capture and non-capture would otherwise
        // collide and falsely satisfy a TT-match check.
        private static readonly Vector2Int OtherTo = TestBoardSetupUtility.AlgebraicToVector("a4");

        private static MoveCommand StandardMove(ChessPieceType pieceType, ChessPieceType capturedType = ChessPieceType.None, Vector2Int? to = null)
        {
            PieceData piece = new PieceData(Team.White, pieceType, moveDirection: 1, startRow: 1);
            PieceData captured = capturedType == ChessPieceType.None
                ? PieceData.Empty
                : new PieceData(Team.Black, capturedType, moveDirection: -1, startRow: 6);

            return MoveCommand.CreateStandardMove(From, to ?? To, piece, captured);
        }

        private static MoveCommand PromotionMove(ChessPieceType promotedTo, ChessPieceType capturedType = ChessPieceType.None)
        {
            PieceData pawn = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1);
            PieceData captured = capturedType == ChessPieceType.None
                ? PieceData.Empty
                : new PieceData(Team.Black, capturedType, moveDirection: -1, startRow: 6);

            return MoveCommand.CreatePromotionMove(From, To, pawn, promotedTo, captured);
        }

        private static MoveCommand ActMove()
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Knight, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, To, piece).WithStage(BetrayalStage.Act);
        }

        [Test]
        public void TTMove_AlwaysSortsFirst_RegardlessOfShape()
        {
            MoveCommand ttMatch = StandardMove(ChessPieceType.Pawn); // otherwise a Tier-4 quiet move
            uint ttPacked = AlphaBetaSearch.PackMove(ttMatch);

            // Distinct destination square so this winning capture can't pack-match the TT move by
            // coincidence — capture status alone isn't part of the packed key (see StandardMove's note).
            MoveCommand winningCapture = StandardMove(ChessPieceType.Pawn, ChessPieceType.Queen, to: OtherTo);

            int ttScore = AlphaBetaSearch.OrderScore(ttMatch, ttPacked);
            int winningCaptureScore = AlphaBetaSearch.OrderScore(winningCapture, ttPacked);

            Assert.That(ttScore, Is.GreaterThan(winningCaptureScore),
                "The TT/PV move must outrank even a winning capture once it's flagged as this node's TT move.");
        }

        [Test]
        public void WinningCapture_OutranksActMove()
        {
            MoveCommand winningCapture = StandardMove(ChessPieceType.Pawn, ChessPieceType.Queen); // pawn takes queen
            MoveCommand act = ActMove();

            Assert.That(AlphaBetaSearch.OrderScore(winningCapture, 0),
                Is.GreaterThan(AlphaBetaSearch.OrderScore(act, 0)),
                "A winning capture must sort ahead of Act — a cheap cutoff should get a chance before paying Retribution's branching cost.");
        }

        [Test]
        public void ActMove_OutranksQuietMove()
        {
            MoveCommand act = ActMove();
            MoveCommand quiet = StandardMove(ChessPieceType.Knight);

            Assert.That(AlphaBetaSearch.OrderScore(act, 0), Is.GreaterThan(AlphaBetaSearch.OrderScore(quiet, 0)),
                "Act must still sort ahead of an ordinary quiet move — it's demoted, not deprioritized to the bottom.");
        }

        [Test]
        public void ActMove_OutranksLosingCapture()
        {
            MoveCommand act = ActMove();
            MoveCommand losingCapture = StandardMove(ChessPieceType.Queen, ChessPieceType.Pawn); // queen takes pawn

            Assert.That(AlphaBetaSearch.OrderScore(act, 0), Is.GreaterThan(AlphaBetaSearch.OrderScore(losingCapture, 0)),
                "Act must outrank a losing capture (queen takes pawn) per the Tier 3 vs Tier 4 band split.");
        }

        [Test]
        public void Promotion_OutranksEqualCapture()
        {
            MoveCommand promotion = PromotionMove(ChessPieceType.Queen);
            MoveCommand equalCapture = StandardMove(ChessPieceType.Rook, ChessPieceType.Rook);

            Assert.That(AlphaBetaSearch.OrderScore(promotion, 0), Is.GreaterThanOrEqualTo(AlphaBetaSearch.OrderScore(equalCapture, 0)),
                "Promotion must sort at or above an equal-value capture, per the Tier 2 band ordering note.");
        }

        [Test]
        public void EqualCapture_OutranksAct()
        {
            MoveCommand equalCapture = StandardMove(ChessPieceType.Knight, ChessPieceType.Knight);
            MoveCommand act = ActMove();

            Assert.That(AlphaBetaSearch.OrderScore(equalCapture, 0), Is.GreaterThan(AlphaBetaSearch.OrderScore(act, 0)),
                "An equal-trade capture (Tier 2) must still outrank Act (Tier 3).");
        }

        [Test]
        public void QuietMove_IsLowestBand()
        {
            MoveCommand quiet = StandardMove(ChessPieceType.Knight);
            MoveCommand losingCapture = StandardMove(ChessPieceType.Queen, ChessPieceType.Pawn);
            MoveCommand act = ActMove();

            int quietScore = AlphaBetaSearch.OrderScore(quiet, 0);

            Assert.That(quietScore, Is.LessThanOrEqualTo(AlphaBetaSearch.OrderScore(losingCapture, 0)));
            Assert.That(quietScore, Is.LessThan(AlphaBetaSearch.OrderScore(act, 0)));
        }
    }
}
