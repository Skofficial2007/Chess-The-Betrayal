using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Static Exchange Evaluation: works out who wins a capture on one square once every piece
    /// that can reach it has taken its turn recapturing, without actually searching any of those
    /// replies. Standard use is capture ordering (try captures that come out ahead first) and a
    /// quiescence prune (skip a capture whose SEE score can't possibly help, the same way delta
    /// pruning already skips hopeless ones).
    ///
    /// The classic algorithm assumes the two sides strictly alternate recapturing on the target
    /// square — exactly what an ordinary chess exchange looks like. Betrayal breaks that
    /// assumption on a square with a pending Betrayer: a Retribution capture is played by an ALLY
    /// of the piece that just Acted, not by the opponent, so "whoever's turn is next" and "whoever
    /// benefits from the exchange" stop being the same side. Rather than teach the swap algorithm
    /// a second, Betrayal-aware turn order, every call site is required to check
    /// <see cref="IsApplicable"/> first and fall back to ordinary MVV-LVA ordering (no SEE, no
    /// prune) whenever it returns false. See AlphaBetaSearch's capture-ordering and quiescence
    /// prune call sites for where that fallback actually happens.
    ///
    /// Re-scans the target square's attackers fresh after every simulated capture (rather than
    /// computing one attacker list up front) specifically so a sliding piece standing behind
    /// today's capturer — a rook backing up a bishop on the same diagonal-turned-file, for example
    /// — is correctly discovered once the piece in front of it is removed from the board. Getting
    /// this wrong (freezing the attacker list at the start) is a well-known SEE bug: it would
    /// under-count a battery and misjudge who really wins the square.
    /// </summary>
    public static class StaticExchangeEvaluation
    {
        // Same centipawn scale AlphaBetaSearch.CapturedPieceValue already uses for delta pruning,
        // kept as its own copy here rather than a shared reference — SEE and delta pruning are
        // conceptually independent tools that happen to agree on piece values today, not two
        // halves of one mechanism, so there's no shared abstraction actually being duplicated.
        private static int PieceValue(ChessPieceType type) => type switch
        {
            ChessPieceType.Pawn => 100,
            ChessPieceType.Knight => 320,
            ChessPieceType.Bishop => 325,
            ChessPieceType.Rook => 500,
            ChessPieceType.Queen => 975,
            ChessPieceType.King => 20_000, // never actually captured, but must outrank every trade
            _ => 0
        };

        private static readonly int[] SlidingDirectionsX = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly int[] SlidingDirectionsY = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] KnightOffsetsX = { 1, 1, -1, -1, 2, 2, -2, -2 };
        private static readonly int[] KnightOffsetsY = { 2, -2, 2, -2, 1, -1, 1, -1 };

        /// <summary>
        /// True when the classic alternating-recapture assumption actually holds for a capture on
        /// <paramref name="move"/>'s target square. False whenever a Betrayer is pending anywhere
        /// on the board (not just on the target square) — a pending Retribution changes which side
        /// "recaptures" on ITS OWN square, but the same board-wide sub-phase also means every other
        /// square's ordinary captures are being explored inside a forced tactical sequence rather
        /// than a free exchange, so the same caution the NMP/forward-pruning guard already applies
        /// here too. A Betrayal Act move itself is also never SEE-scored — it isn't a material
        /// capture, it stages one.
        /// </summary>
        public static bool IsApplicable(BoardState board, MoveCommand move) =>
            !board.PendingBetrayerSquare.HasValue && move.Stage == BetrayalStage.None;

        /// <summary>
        /// Runs the exchange on <paramref name="move"/>'s destination square and returns the net
        /// material result for the side making <paramref name="move"/>, in centipawns. Positive
        /// means the mover comes out ahead once every recapture is played out; negative means the
        /// square is a loser even before any of the recaptures need to be searched for real.
        /// Callers must have already checked <see cref="IsApplicable"/> — this does not re-check
        /// the Betrayal guard itself, since every call site already has to branch on it anyway to
        /// choose between this and the MVV-LVA fallback.
        /// </summary>
        public static int Evaluate(BoardState board, MoveCommand move)
        {
            Vector2Int target = move.EndPosition;
            Team sideToMove = move.PieceTeam;
            Team opponent = sideToMove == Team.White ? Team.Black : Team.White;

            int captureGain = move.IsEnPassant
                ? PieceValue(ChessPieceType.Pawn)
                : PieceValue(move.CapturedType);

            _removedSquares.Clear();
            _removedSquares.Add(move.StartPosition); // the mover has already left its origin square
            if (move.IsEnPassant && move.EnPassantCapturePosition.HasValue)
                _removedSquares.Add(move.EnPassantCapturePosition.Value); // the captured pawn is gone too

            // Standard swap-off list: gains[0] is the material the very first capture wins. Each
            // later ply is "the value of the piece now sitting on the square, minus whatever the
            // previous capturer had just won" — the well-known trick that folds an alternating
            // exchange into one forward array with no recursion.
            int[] gains = GainBuffer;
            gains[0] = captureGain;
            int onMoveValue = PieceValue(move.PieceType);

            Team toRecapture = opponent;
            int ply = 0;

            while (true)
            {
                if (ply + 1 >= gains.Length) break; // pathological attacker pile-up backstop

                if (!TryFindCheapestAttacker(board, target, toRecapture, out Vector2Int attackerSquare,
                                              out ChessPieceType attackerType))
                    break;

                ply++;
                gains[ply] = onMoveValue - gains[ply - 1];
                onMoveValue = PieceValue(attackerType);

                _removedSquares.Add(attackerSquare);
                toRecapture = toRecapture == Team.White ? Team.Black : Team.White;
            }

            // Fold backwards: the side to move at ply d only goes through with that capture if it
            // beats simply declining (walking away, which leaves the previous ply's result
            // standing) — so each step is the negamax of "take it" (gains[d]) vs "decline"
            // (-gains[d-1], the previous ply's result from this ply's opposite point of view).
            while (ply > 0)
            {
                gains[ply - 1] = -System.Math.Max(-gains[ply - 1], gains[ply]);
                ply--;
            }

            return gains[0];
        }

        // Reused across calls — SEE runs inside move ordering and the qsearch prune, both hot
        // per-node paths, so this mirrors AlphaBetaSearch's own ctor-once-buffer discipline rather
        // than allocating fresh collections per call. Sized well above any realistic single-square
        // attacker count.
        private static readonly int[] GainBuffer = new int[34]; // 32 pieces + 1 headroom either side
        private static readonly List<Vector2Int> _removedSquares = new List<Vector2Int>(32);

        /// <summary>
        /// Finds <paramref name="team"/>'s cheapest piece that currently attacks
        /// <paramref name="targetSquare"/>, live against the real board MINUS every square already
        /// in <see cref="_removedSquares"/> (the pieces already spent earlier in this same
        /// exchange). Re-deriving this fresh at every ply — rather than snapshotting the attacker
        /// list once before the loop starts — is what lets a sliding piece parked behind today's
        /// capturer be discovered the instant the piece in front of it is "removed," exactly like
        /// it would be able to recapture for real once the blocker is gone.
        /// </summary>
        private static bool TryFindCheapestAttacker(BoardState board, Vector2Int targetSquare, Team team,
                                                      out Vector2Int attackerSquare, out ChessPieceType attackerType)
        {
            attackerSquare = default;
            attackerType = ChessPieceType.None;
            int bestValue = int.MaxValue;

            List<int> indices = board.GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                int x = idx % board.TileCountX;
                int y = idx / board.TileCountX;
                Vector2Int position = new Vector2Int(x, y);

                if (_removedSquares.Contains(position)) continue;

                PieceData piece = board.GetPiece(x, y);
                int value = PieceValue(piece.Type);
                if (value >= bestValue) continue; // already have a cheaper (or equal) attacker

                if (!Attacks(board, piece, position, targetSquare)) continue;

                bestValue = value;
                attackerSquare = position;
                attackerType = piece.Type;
            }

            return attackerType != ChessPieceType.None;
        }

        /// <summary>
        /// True if the piece at <paramref name="position"/> attacks <paramref name="targetSquare"/>
        /// against the board's real occupancy, treating every square in
        /// <see cref="_removedSquares"/> as empty regardless of what's actually there. Implemented
        /// directly here (rather than via IPieceMovement.GetAttackedSquares) because that interface
        /// always reads live board occupancy with no way to override it for the pieces this
        /// in-progress exchange has already "used up" — exactly the capability an X-ray-correct SEE
        /// needs and ordinary check/attack detection never does.
        /// </summary>
        private static bool Attacks(BoardState board, PieceData piece, Vector2Int position, Vector2Int targetSquare)
        {
            switch (piece.Type)
            {
                case ChessPieceType.Pawn:
                {
                    int dir = piece.MoveDirection;
                    return targetSquare == new Vector2Int(position.x - 1, position.y + dir)
                        || targetSquare == new Vector2Int(position.x + 1, position.y + dir);
                }

                case ChessPieceType.Knight:
                {
                    for (int d = 0; d < KnightOffsetsX.Length; d++)
                    {
                        if (targetSquare == new Vector2Int(position.x + KnightOffsetsX[d], position.y + KnightOffsetsY[d]))
                            return true;
                    }
                    return false;
                }

                case ChessPieceType.King:
                {
                    int dx = System.Math.Abs(targetSquare.x - position.x);
                    int dy = System.Math.Abs(targetSquare.y - position.y);
                    return dx <= 1 && dy <= 1 && (dx != 0 || dy != 0);
                }

                case ChessPieceType.Rook:
                    return SlidesTo(board, position, targetSquare, startDirection: 0, directionCount: 4);

                case ChessPieceType.Bishop:
                    return SlidesTo(board, position, targetSquare, startDirection: 4, directionCount: 4);

                case ChessPieceType.Queen:
                    return SlidesTo(board, position, targetSquare, startDirection: 0, directionCount: 8);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Walks each ray from <paramref name="from"/>, honoring <see cref="_removedSquares"/> as
        /// empty, and returns true if <paramref name="targetSquare"/> is reached before a real
        /// (non-removed) piece blocks the ray. Directions 0-3 are the rook's straight rays,
        /// 4-7 the bishop's diagonals — SlidingDirectionsX/Y lists both in that order so a Queen can
        /// just walk all 8, and a Rook/Bishop each walk their own 4-direction slice.
        /// </summary>
        private static bool SlidesTo(BoardState board, Vector2Int from, Vector2Int targetSquare,
                                      int startDirection, int directionCount)
        {
            for (int d = startDirection; d < startDirection + directionCount; d++)
            {
                int stepX = SlidingDirectionsX[d];
                int stepY = SlidingDirectionsY[d];

                for (int step = 1; ; step++)
                {
                    Vector2Int square = new Vector2Int(from.x + stepX * step, from.y + stepY * step);
                    if (!board.IsValidIndex(square)) break;

                    if (square == targetSquare) return true;

                    bool occupied = !board.GetPiece(square).IsEmpty && !_removedSquares.Contains(square);
                    if (occupied) break;
                }
            }

            return false;
        }
    }
}
