using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Positions that produce a particular Betrayal, for the fixtures that need one played rather
    /// than described.
    ///
    /// Kept out of BoardSetup, which holds general primitives and ships inside Tooling: a position
    /// chosen to force one branch is a test's business rather than the game's.
    /// </summary>
    internal static class BetrayalScenario
    {
        /// <summary>
        /// The White Knight on f4 may betray the White Pawn on d3, and the Knight it becomes checks
        /// the White King on e1 — so a Defensive Override is owed and the turn cannot end on the
        /// Defection itself. The Black Rook on e8 pins White's Rook to its own King, which is what
        /// leaves no legal Retribution and forces the Defection in the first place.
        ///
        /// The a-file pawns are for quiet plies either side of the Betrayal, which several of these
        /// cases need in order to have a turn to compare against.
        /// </summary>
        public static void ArrangeOneThatForcesASave(BoardState board)
        {
            board.Clear();
            board.WithPiece("e1", Team.White, ChessPieceType.King)
                 .WithPiece("h8", Team.Black, ChessPieceType.King)
                 .WithPiece("e4", Team.White, ChessPieceType.Rook)
                 .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                 .WithPiece("f4", Team.White, ChessPieceType.Knight)
                 .WithPiece("d3", Team.White, ChessPieceType.Pawn)
                 .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                 .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                 .WithTurn(Team.White)
                 .WithBetrayalRight(true)
                 .WithComputedHash();
        }

        /// <summary>The Act that starts it, read off the engine rather than hand-built so a change
        /// to what counts as a Betrayal target fails these tests instead of passing them.</summary>
        public static MoveCommand TheActThatStartsIt(BoardState board)
        {
            var targets = new List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(board, BoardSetup.AlgebraicToVector("f4"), targets);
            return targets[0];
        }
    }
}
