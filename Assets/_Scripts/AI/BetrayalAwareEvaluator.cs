using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Static evaluation from one team's point of view. Positive = good for forTeam.
    /// Allocation-free: reads piece-index lists, no LINQ, no temporaries.
    ///
    /// Betrayal-specific terms (from the AI research analysis):
    ///   1. Option value  — an UNSPENT betrayal right is worth a small bonus to whoever still
    ///                       holds it. Without this, the AI has no reason to preserve the right
    ///                       or to value reaching a position where Betrayal is strong.
    ///   2. Double-swing  — already handled implicitly by material counting AFTER a defection
    ///                       resolves, because DefectPiece flips the piece's team, so a defected
    ///                       knight is simultaneously -320 to its old side and +320 to its new
    ///                       side in the material sum. We do NOT double-count it here; the search
    ///                       reaches the resolved position and evaluates the real board. The term
    ///                       that matters is fearing the swing BEFORE it resolves, which move
    ///                       ordering + quiescence (not static eval) handle.
    /// </summary>
    public sealed class BetrayalAwareEvaluator : IPositionEvaluator
    {
        // Centipawn values (report's scale: P=100, N=320, B=325, R=500, Q=975).
        private const int PawnValue   = 100;
        private const int KnightValue = 320;
        private const int BishopValue = 325;
        private const int RookValue   = 500;
        private const int QueenValue  = 975;

        // Small, deliberately conservative. The betrayal right is optionality, not material —
        // overvaluing it makes the AI hoard the right and play passively. ~1/3 of a pawn.
        private const int BetrayalRightBonus = 35;

        public int Evaluate(BoardState board, Team forTeam)
        {
            int whiteScore = MaterialAndPosition(board, Team.White);
            int blackScore = MaterialAndPosition(board, Team.Black);

            int score = whiteScore - blackScore; // White's perspective

            // --- Betrayal option value (Term 1) ---
            // The right is a single global resource. Whoever it's "for" is the side to move while
            // it's still available — but since it's once-per-match-total and first-come, we simply
            // credit the side to move for holding a live option. Cheap and symmetric.
            if (board.BetrayalRightAvailable)
            {
                score += (board.CurrentTurn == Team.White ? BetrayalRightBonus : -BetrayalRightBonus);
            }

            // Convert to forTeam's perspective (negamax-friendly).
            return forTeam == Team.White ? score : -score;
        }

        private static int MaterialAndPosition(BoardState board, Team team)
        {
            int total = 0;
            var indices = board.GetPieceIndices(team); // O(1), no alloc

            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                PieceData piece = board.GetPiece(x, y);

                total += BaseValue(piece.Type);
                // PST hook: add PieceSquareBonus(piece, x, y, team) here in the tuning pass.
                // Left out of v1 to keep the first working agent simple — material + betrayal
                // option value is enough to get a beating-a-beginner opponent and validate the
                // Betrayal search paths first.
            }

            return total;
        }

        private static int BaseValue(ChessPieceType type) => type switch
        {
            ChessPieceType.Pawn   => PawnValue,
            ChessPieceType.Knight => KnightValue,
            ChessPieceType.Bishop => BishopValue,
            ChessPieceType.Rook   => RookValue,
            ChessPieceType.Queen  => QueenValue,
            ChessPieceType.King   => 0, // king safety handled separately; material-infinite is meaningless here
            _ => 0
        };
    }
}
