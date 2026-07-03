using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// Validates ITurnResolver.ResolveVoluntaryDefection — the Skip button's entry point. A
    /// voluntary skip while a legal Retribution exists must resolve identically to the forced
    /// "no legal Retribution" path, including the Defensive Override self-check (rulebook 5B).
    /// </summary>
    [TestFixture]
    public class BetrayalVoluntarySkipTests
    {
        private TurnResolver _resolver;

        [SetUp]
        public void Setup()
        {
            _resolver = new TurnResolver();
        }

        [Test]
        public void ResolveVoluntaryDefection_EvenWithLegalExecutorAvailable_FlipsBetrayerToOpponent()
        {
            // Arrange: White has a legal Executioner (the Rook on e4 could capture h8's Knight-turned-
            // Betrayer if it moved there) — but the player is choosing to Skip instead.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("e1", Team.White, ChessPieceType.Rook) // legal Executioner, unused
                .WithPiece("h8", Team.White, ChessPieceType.Knight) // Betrayer
                .WithPendingBetrayer("h8", Team.White);

            TurnAdvanceResult result = _resolver.ResolveVoluntaryDefection(board);

            PieceData defected = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("h8"));
            Assert.That(defected.Team, Is.EqualTo(Team.Black), "Skip must defect the Betrayer exactly like a forced Defection.");
            Assert.That(result.DidDefect, Is.True);
        }

        [Test]
        public void ResolveVoluntaryDefection_NoSelfCheck_PassesTurnToOpponent()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.White, ChessPieceType.Knight) // Betrayer, defecting far from King
                .WithPendingBetrayer("h8", Team.White);

            TurnAdvanceResult result = _resolver.ResolveVoluntaryDefection(board);

            Assert.That(result.RequiresForcedSave, Is.False);
            Assert.That(result.NextPhase, Is.EqualTo(TurnPhase.Normal));
            Assert.That(result.TurnPassedToOpponent, Is.True);
        }

        [Test]
        public void ResolveVoluntaryDefection_DefectionCausesSelfCheck_RoutesToForcedSave()
        {
            // Same fixture shape as BetrayalDefectionTests.ResolveFailedRetribution_..._RequiresForcedSaveIsTrue,
            // but triggered via a voluntary skip instead of "no legal Retribution" — must behave identically.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // Betrayer. Once it defects to Black, it checks e1.
                .WithPendingBetrayer("e4", Team.White);

            TurnAdvanceResult result = _resolver.ResolveVoluntaryDefection(board);

            Assert.That(result.RequiresForcedSave, Is.True, "Defensive Override must trigger regardless of why Defection happened.");
            Assert.That(result.NextPhase, Is.EqualTo(TurnPhase.ForcedSave));
            Assert.That(result.TurnPassedToOpponent, Is.False, "Turn does not pass yet — the forced Save move is still pending.");
        }
    }
}
