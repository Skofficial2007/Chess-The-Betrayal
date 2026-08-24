using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tooling.Strength
{
    /// <summary>
    /// Counts material and nothing else — no piece-square tables, no pawn structure, no king
    /// safety, no king approach, no Betrayal option value.
    ///
    /// This exists to prove that a quiet move is correct without asking the production evaluator
    /// whether it likes the move. A proof that consulted the real evaluator would establish only
    /// that the evaluator agrees with itself, which is worth nothing: the position would pass its
    /// own admissibility check precisely because the thing being measured already believed it.
    /// Searching with material as the only yardstick avoids that entirely — the answer it gives is
    /// "this line wins a pawn by force," a fact about the game rather than an opinion about the
    /// position.
    ///
    /// Kept out of the AI assembly deliberately. This is proof machinery for the test suite and
    /// must never be reachable from the shipped search, where replacing the evaluator with this one
    /// would make the AI play blindfold.
    /// </summary>
    public sealed class MaterialOnlyEvaluator : IPositionEvaluator
    {
        // The same relative scale the production evaluator uses, so a margin expressed in
        // centipawns means the same thing in a proof as it does everywhere else in this codebase.
        // The values are duplicated rather than shared because a proof standard that silently
        // changed whenever someone retuned the real evaluator would not be much of a standard.
        private const int PawnValue = 100;
        private const int KnightValue = 320;
        private const int BishopValue = 325;
        private const int RookValue = 500;
        private const int QueenValue = 975;

        public int Evaluate(BoardState board, Team forTeam)
        {
            int white = MaterialFor(board, Team.White);
            int black = MaterialFor(board, Team.Black);
            int score = white - black;

            return forTeam == Team.White ? score : -score;
        }

        /// <summary>
        /// Identical to the full evaluation by construction: there are no terms held back behind a
        /// lazy cut here, so the cheap score is already exact. This satisfies the interface's
        /// requirement that the cheap score never cost more than the full one and always bound it.
        /// </summary>
        public int EvaluateCheap(BoardState board, Team forTeam) => Evaluate(board, forTeam);

        /// <summary>Nothing sits behind a full path here — the cheap score is the whole score — so
        /// there is no gap for a caller to allow for.</summary>
        public int MaxCheapToFullSwing => 0;

        private static int MaterialFor(BoardState board, Team team)
        {
            int material = 0;
            var indices = board.GetPieceIndices(team);

            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                material += BaseValue(board.GetPiece(x, y).Type);
            }

            return material;
        }

        private static int BaseValue(ChessPieceType type) => type switch
        {
            ChessPieceType.Pawn => PawnValue,
            ChessPieceType.Knight => KnightValue,
            ChessPieceType.Bishop => BishopValue,
            ChessPieceType.Rook => RookValue,
            ChessPieceType.Queen => QueenValue,
            // Both sides always have exactly one king, so any value here cancels in the difference.
            ChessPieceType.King => 0,
            _ => 0
        };
    }
}
