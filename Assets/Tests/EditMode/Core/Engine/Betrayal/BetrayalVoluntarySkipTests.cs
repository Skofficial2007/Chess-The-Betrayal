using System.Collections.Generic;
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
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _resolver = new TurnResolver();
            _moveBuffer = new List<MoveCommand>();
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

        [Test]
        public void Advance_ActWithNoLegalRetributionAndNoSelfCheck_ZobristStaysConsistentAfterResolution()
        {
            // Regression for a suspected Zobrist desync: ResultFromDefectionOutcome's no-ForcedSave
            // path clears PendingBetrayerSquare/BetrayalInitiator but never toggled the pending-Betrayer
            // SUB-STATE hash back off. Act toggles that hash bit ON (ApplyZobristMove's Act branch);
            // only a terminal Retribution/DefensiveOverride move toggles it back off — and neither of
            // those runs for a plain (non-ForcedSave) Defection. ComputeFullZobristHash only includes
            // the sub-state term while PendingBetrayerSquare.HasValue, so once pending goes null with
            // the incremental hash still carrying that term, AssertZobristConsistency must fail.
            //
            // Driven through the REAL incremental path (Advance, not WithPendingBetrayer) so the
            // sub-state hash is genuinely toggled on by the Act, exactly like live play or the AI search.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("h8", Team.White, ChessPieceType.Knight) // Betrayer, defects far from its King
                .WithPiece("f7", Team.White, ChessPieceType.Pawn) // Victim: a knight-move away from h8
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("h8"), _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.GreaterThan(0), "Knight must have at least one Betrayal Act target.");
            MoveCommand actMove = _moveBuffer[0];

            TurnAdvanceResult result = _resolver.Advance(board, actMove);

            Assert.That(result.RequiresForcedSave, Is.False, "Defecting far from its own King must not self-check.");
            Assert.That(result.NextPhase, Is.EqualTo(TurnPhase.Normal), "No ForcedSave means the sequence is fully resolved.");
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Pending state must be cleared once the sequence resolves.");

            Assert.DoesNotThrow(() => board.AssertZobristConsistency(),
                "Incremental hash must match a full recompute once a no-ForcedSave Defection has fully resolved — " +
                "the pending-Betrayer sub-state hash bit the Act turned on must be turned back off here, since no " +
                "further Retribution/DefensiveOverride move is coming to do it.");
        }
    }
}
