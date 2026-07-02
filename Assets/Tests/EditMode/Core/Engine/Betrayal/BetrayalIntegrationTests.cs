using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// The final proving grounds. These tests execute the full multi-phase sequence
    /// to guarantee that the Turn Economy, State Machine logic, and Zobrist Hashing
    /// all hold together under the stress of a complete Betrayal sequence.
    /// </summary>
    [TestFixture]
    public class BetrayalIntegrationTests
    {
        private List<MoveCommand> _moveBuffer;

        [SetUp]
        public void Setup()
        {
            _moveBuffer = new List<MoveCommand>();
        }

        [Test]
        public void FullSequence_ResolutionA_Success_CompletesTurnAndUpdatesHash()
        {
            // ---------------------------------------------------------
            // 1. SETUP (The Board)
            // White Knight at b1 (Betrayer)
            // White Pawn at a3 (Victim)
            // White Rook at a1 (Executioner)
            // ---------------------------------------------------------
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            board.ComputeFullZobristHash();
            ulong initialHash = board.ZobristHash;

            // ---------------------------------------------------------
            // 2. PHASE 1: THE ACT
            // ---------------------------------------------------------
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b1"), _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.EqualTo(1), "Knight should be able to target the pawn at a3.");
            MoveCommand actMove = _moveBuffer[0];

            // FIX: The engine natively handles the Betrayal state transition and hash toggle now!
            // No manual state manipulation needed in the test.
            ChessEngine.ApplyMoveToBoard(board, actMove);

            Assert.That(board.BetrayalRightAvailable, Is.False, "Engine should natively consume the betrayal right.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).Type, Is.EqualTo(ChessPieceType.Knight), "Knight successfully betrayed the Pawn.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain consistent after Phase 1 Act.");

            // ---------------------------------------------------------
            // 3. PHASE 2: RETRIBUTION (Success)
            // ---------------------------------------------------------
            ChessEngine.GetRetributionMoves(board, Team.White, board.PendingBetrayerSquare.Value, _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.EqualTo(1), "Rook at a1 should be able to execute the Knight at a3.");
            MoveCommand retMove = _moveBuffer[0];

            ChessEngine.ApplyMoveToBoard(board, retMove);
            board.NextTurn(); // GameManager natively ends the turn after Resolution A

            // ---------------------------------------------------------
            // 4. FINAL ASSERTIONS
            // ---------------------------------------------------------
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Engine should natively clear the pending betrayer square upon success.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black), "Turn economy must successfully pass to Black after Resolution A.");
            Assert.That(board.WhiteCaptured.Count, Is.EqualTo(2), "Both the Victim (Pawn) and the Betrayer (Knight) must end up in the graveyard.");
            Assert.That(board.ZobristHash, Is.Not.EqualTo(initialHash));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain consistent across the entire multi-action sequence.");
        }

        [Test]
        public void FullSequence_ResolutionB_DefectionAndForcedSave_CompletesTurnAndUpdatesHash()
        {
            // ---------------------------------------------------------
            // 1. SETUP (The Trap)
            // White Rook at e4 is pinned to the King by Black Rook at e8.
            // White Knight at e3 betrays the White Pawn at d3.
            // When the Knight lands on d3 and defects to Black, it will check the King on e1!
            // ---------------------------------------------------------
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)   // Executioner (Pinned)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)   // Pinning piece
                .WithPiece("e3", Team.White, ChessPieceType.Knight) // Betrayer
                .WithPiece("d3", Team.White, ChessPieceType.Pawn)   // Victim
                .WithTurn(Team.White)
                .WithBetrayalRight(true);

            board.ComputeFullZobristHash();

            // ---------------------------------------------------------
            // 2. PHASE 1: THE ACT
            // ---------------------------------------------------------
            MoveCommand actMove = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e3"), TestBoardSetupUtility.AlgebraicToVector("d3"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e3")), board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d3")), board)
                .WithStage(BetrayalStage.Act);

            // FIX: Natively applied by the engine. Removed manual toggles.
            ChessEngine.ApplyMoveToBoard(board, actMove);

            // ---------------------------------------------------------
            // 3. PHASE 2: RETRIBUTION (Fails due to Pin)
            // ---------------------------------------------------------
            ChessEngine.GetRetributionMoves(board, Team.White, board.PendingBetrayerSquare.Value, _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.EqualTo(0), "No executioners available. Defection triggered.");

            DefectionOutcome outcome = ChessEngine.ResolveFailedRetribution(board);
            Assert.That(outcome.RequiresForcedSave, Is.True, "Defected Knight on d3 is now attacking the White King on e1. Forced Save required.");
            Assert.That(board.GetPiece(outcome.DefectedSquare).Team, Is.EqualTo(Team.Black), "Knight successfully defected to Black.");

            // ---------------------------------------------------------
            // 4. PHASE 3: DEFENSIVE OVERRIDE (Forced Save)
            // ---------------------------------------------------------
            ChessEngine.GetForcedSaveMoves(board, Team.White, _moveBuffer);
            Assert.That(_moveBuffer.Count, Is.GreaterThan(0), "King must have legal moves to escape the check.");
            MoveCommand saveMove = _moveBuffer[0]; // Just take the first valid escape

            ChessEngine.ApplyMoveToBoard(board, saveMove);
            board.NextTurn(); // GameManager natively ends the turn after Defensive Save

            // ---------------------------------------------------------
            // 5. FINAL ASSERTIONS
            // ---------------------------------------------------------
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Engine should natively clear the pending betrayer square upon forced save completion.");
            Assert.That(board.CurrentTurn, Is.EqualTo(Team.Black), "Turn economy must successfully pass to Black after Resolution B -> Forced Save.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Zobrist Hash survived Act + Defection + Forced Save cleanly.");
        }
    }
}