using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Static evaluation from one team's point of view. Positive = good for forTeam.
    /// Scores a position by reading the per-team piece lists.
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

        // Per sheltering pawn, well under the King PST's ~40-50cp swings — additive nuance on top
        // of the existing king-safety table, not a competing dominant term.
        private const int KingShelterBonusPerPawn = 10;

        private readonly EvaluationWeights _weights;

        public BetrayalAwareEvaluator() : this(EvaluationWeights.Identity)
        {
        }

        public BetrayalAwareEvaluator(EvaluationWeights weights)
        {
            _weights = weights;
        }

        public int Evaluate(BoardState board, Team forTeam)
        {
            int whiteScore = MaterialAndPosition(board, Team.White);
            int blackScore = MaterialAndPosition(board, Team.Black);

            int score = whiteScore - blackScore; // White's perspective

            // Betrayal option value (Term 1). Scaled by BetrayalOptionScale so a more
            // Betrayal-aggressive profile values holding/reaching the option more.
            // The right is a single global resource. Whoever it's "for" is the side to move while
            // it's still available — but since it's once-per-match-total and first-come, we simply
            // credit the side to move for holding a live option. Cheap and symmetric.
            if (board.BetrayalRightAvailable)
            {
                int betrayalTerm = (int)(BetrayalRightBonus * _weights.BetrayalOptionScale);
                score += (board.CurrentTurn == Team.White ? betrayalTerm : -betrayalTerm);
            }

            // Convert to forTeam's perspective (negamax-friendly).
            return forTeam == Team.White ? score : -score;
        }

        /// <summary>
        /// Material is NEVER scaled — asymmetric material weighting breaks negamax's zero-sum
        /// frame. Everything positional splits into two buckets by whether the piece square sits
        /// on the scoring side's own half (Defense, including the new king-shelter term) or past
        /// the midline into enemy territory (Attack), each independently scaled.
        /// </summary>
        private int MaterialAndPosition(BoardState board, Team team)
        {
            int material = 0;
            int attackPst = 0;
            int defensePst = 0;
            var indices = board.GetPieceIndices(team);

            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                PieceData piece = board.GetPiece(x, y);

                material += BaseValue(piece.Type);

                int bonus = PieceSquareTables.Bonus(piece.Type, x, y, team, board.TileCountX, board.TileCountY);
                // Same row normalization PieceSquareTables.Bonus uses internally — row 0 is the
                // scoring side's own back rank, row 7 is the opponent's. row >= 4 means the piece
                // is on/past the midline into enemy territory.
                int row = team == Team.White ? y : board.TileCountY - 1 - y;
                if (row >= 4) attackPst += bonus;
                else defensePst += bonus;
            }

            int shelterBonus = KingShelterBonus(board, team);

            return material
                + (int)(attackPst * _weights.AttackScale)
                + (int)((defensePst + shelterBonus) * _weights.DefenseScale);
        }

        /// <summary>
        /// Minimal king-safety term: counts friendly pawns on the 3 squares directly in front of
        /// the king. Deliberately simple — no tropism/attack-maps — this is a nuance on top of the
        /// existing King PST table, not a replacement for it.
        /// </summary>
        private static int KingShelterBonus(BoardState board, Team team)
        {
            if (!board.TryFindKing(team, out Vector2Int kingPos)) return 0;

            int forward = team == Team.White ? 1 : -1;
            int shelterY = kingPos.y + forward;

            int shelteredPawns = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                PieceData square = board.GetPiece(kingPos.x + dx, shelterY);
                if (square.Type == ChessPieceType.Pawn && square.Team == team) shelteredPawns++;
            }

            return shelteredPawns * KingShelterBonusPerPawn;
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
