using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// A ForcedSave-pending board keeps PendingBetrayerSquare/BetrayalInitiator set even though the
    /// Betrayer at that square has already defected (see ChessEngine.ResolveDefection) — the piece
    /// there now belongs to the OPPOSITE team from BetrayalInitiator. GetAllLegalMoves and
    /// GetAllLegalMovesIncludingBetrayal used to only recognize the opposite, pre-defection state
    /// (Betrayer still on the initiator's team, Retribution owed) and silently fell through to
    /// ordinary legal-move generation for this one — which let a search consider moves other than
    /// the mandatory DefensiveOverride, and downstream produced a genuine crash the first time a
    /// full multi-ply game reached this state on its own (found via BenchmarkRunnerTests).
    /// </summary>
    [TestFixture]
    public class BetrayalForcedSaveMoveGenTests
    {
        /// <summary>White's own Rook at e4 has already defected to Black (simulating the moment
        /// right after a self-checking Defection) and is now delivering check to White's king —
        /// BetrayalInitiator stays White (who initiated the original Betrayal), matching what
        /// ChessEngine.ResolveDefection leaves behind when RequiresForcedSave is true.</summary>
        private static BoardState ForcedSavePendingBoard() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.Black, ChessPieceType.Rook) // defected: now Black, still marked pending
                .WithPiece("h4", Team.White, ChessPieceType.Rook) // can capture the defected piece
                .WithTurn(Team.White)
                .WithPendingBetrayer("e4", Team.White)
                .WithComputedHash();

        [Test]
        public void GetAllLegalMoves_ForcedSavePending_ReturnsOnlyDefensiveOverrideMoves()
        {
            BoardState board = ForcedSavePendingBoard();
            var moves = new List<MoveCommand>();

            ChessEngine.GetAllLegalMoves(board, Team.White, moves);

            Assert.That(moves, Is.Not.Empty);
            foreach (MoveCommand move in moves)
                Assert.That(move.Stage, Is.EqualTo(BetrayalStage.DefensiveOverride));
        }

        [Test]
        public void GetAllLegalMovesIncludingBetrayal_ForcedSavePending_ReturnsOnlyDefensiveOverrideMoves()
        {
            BoardState board = ForcedSavePendingBoard();
            var moves = new List<MoveCommand>();

            ChessEngine.GetAllLegalMovesIncludingBetrayal(board, Team.White, moves);

            Assert.That(moves, Is.Not.Empty);
            foreach (MoveCommand move in moves)
                Assert.That(move.Stage, Is.EqualTo(BetrayalStage.DefensiveOverride));
        }

        [Test]
        public void GetAllLegalMoves_ForcedSavePending_MatchesGetForcedSaveMovesDirectly()
        {
            BoardState board = ForcedSavePendingBoard();
            var viaGetAllLegalMoves = new List<MoveCommand>();
            var viaGetForcedSaveMoves = new List<MoveCommand>();

            ChessEngine.GetAllLegalMoves(board, Team.White, viaGetAllLegalMoves);
            ChessEngine.GetForcedSaveMoves(board, Team.White, viaGetForcedSaveMoves);

            Assert.That(viaGetAllLegalMoves.Count, Is.EqualTo(viaGetForcedSaveMoves.Count));
        }

        [Test]
        public void GetForcedSaveMoves_ForcedSavePending_DoesNotThrow()
        {
            BoardState board = ForcedSavePendingBoard();
            var moves = new List<MoveCommand>();

            Assert.DoesNotThrow(() => ChessEngine.GetForcedSaveMoves(board, Team.White, moves));
        }

        [Test]
        public void GetForcedSaveMoves_RetributionStillPending_ThrowsTheInvariantViolation()
        {
            // The OTHER pending state — Betrayer has NOT defected yet — must still be rejected loudly.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook) // still White: not defected
                .WithTurn(Team.Black)
                .WithPendingBetrayer("e4", Team.White)
                .WithComputedHash();
            var moves = new List<MoveCommand>();

            var ex = Assert.Throws<DomainException>(() => ChessEngine.GetForcedSaveMoves(board, Team.White, moves));
            Assert.That(ex.Message, Does.Contain("still belongs to the initiator's team"));
        }

        [Test]
        public void EvaluateGameState_ForcedSavePending_ReportsNormal_NotCheckmateOrStalemate()
        {
            BoardState board = ForcedSavePendingBoard();

            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            Assert.That(state, Is.EqualTo(GameState.Normal),
                "A ForcedSave obligation isn't a real checkmate/stalemate check point — ordinary HasAnyLegalMoves only looks at plain moves, which is meaningless while it's open.");
        }

        [Test]
        public void EvaluateGameState_ForcedSavePending_NoLegalSaveExists_StillReportsNormal()
        {
            // Smothered-mate-shaped ForcedSave: no legal DefensiveOverride exists. This is a loss
            // condition the search resolves itself (see AlphaBetaSearch.PlayForcedSaveMoves scoring
            // an empty save list as mate) — EvaluateGameState must not also try to call it, since it
            // has no Betrayal-aware move generator of its own to check against.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Rook)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c2", Team.Black, ChessPieceType.Knight) // defected, delivering unblockable check
                .WithTurn(Team.White)
                .WithPendingBetrayer("c2", Team.White)
                .WithComputedHash();

            GameState state = ChessEngine.EvaluateGameState(board, Team.White);

            Assert.That(state, Is.EqualTo(GameState.Normal));
        }
    }
}
