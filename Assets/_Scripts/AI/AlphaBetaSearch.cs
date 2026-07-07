using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Negamax + Alpha-Beta, running against OUR Core engine (make/unmake, GetAllLegalMoves,
    /// GetRetributionMoves) via the IChessEngine seam — never the static ChessEngine class directly,
    /// so the AI assembly depends only on the interface (see IChessEngine.ApplyMove/UndoMove).
    /// Runs on a background thread against an ISOLATED BoardState clone — it never touches the
    /// main-thread board, so the disguise-trick mutation inside move generation is safe.
    ///
    /// The one rule that makes Betrayal search correct:
    /// A standard engine flips the side-to-move every ply. Betrayal breaks that: an Act and a
    /// Defection are half-moves by the same player (the turn does not pass). Our Core already
    /// encodes this — ApplyZobristMove skips the turn-hash toggle for Act and Defection. The
    /// search must mirror it: negate the child score only when the turn actually flipped. When it
    /// didn't (Act, Defection), we recurse without negation and without swapping alpha/beta,
    /// because the same maximizer keeps moving. Alpha and beta therefore carry straight through
    /// the Retribution sub-phase with no special-casing — the sub-phase is just extra plies for
    /// the same player, and GetAllLegalMoves already returns only the legal Retribution moves
    /// when a betrayer is pending, so the branching there is naturally tiny (1-4 executioners).
    ///
    /// CROSS-REFERENCE: this turn-flip rule is re-derived here from move.Stage rather than driven
    /// through ITurnResolver.Advance, because the search needs per-ply control over the Betrayal
    /// sub-phase that Advance's auto-resolution doesn't give it. See the matching remark on
    /// ChessTheBetrayal.Core.Engine.TurnResolver — if the flip rule ever changes, update both.
    /// </summary>
    public sealed class AlphaBetaSearch
    {
        private const int Infinity = 1_000_000;
        private const int MateScore = 900_000;

        private readonly IChessEngine _engine;
        private readonly IPositionEvaluator _evaluator;

        // Reused across the whole search — one buffer per depth level to avoid clobbering a
        // parent's move list while recursing. Grown lazily; never freed. No per-node allocation.
        private readonly List<MoveCommand>[] _moveBuffers;
        private readonly List<MoveCommand> _rootMoves = new List<MoveCommand>(64);

        public AlphaBetaSearch(IChessEngine engine, IPositionEvaluator evaluator, int maxSupportedDepth = 32)
        {
            _engine = engine;
            _evaluator = evaluator;
            _moveBuffers = new List<MoveCommand>[maxSupportedDepth + 1];
            for (int i = 0; i < _moveBuffers.Length; i++)
                _moveBuffers[i] = new List<MoveCommand>(64);
        }

        /// <summary>
        /// Iterative deepening entry point. Returns the best move for board.CurrentTurn.
        /// Caller runs this on a worker thread against a cloned board (see AsyncAIAgent).
        /// </summary>
        public MoveCommand FindBestMove(BoardState board, AISearchSettings settings, CancellationToken ct)
        {
            Team rootTeam = board.CurrentTurn;

            // Build the root move list ONCE. This is where the agent-level Betrayal policy applies.
            BuildRootMoves(board, rootTeam, settings.BetrayalUsage);

            MoveCommand bestMove = _rootMoves.Count > 0 ? _rootMoves[0] : default;

            // Iterative deepening: search depth 1, 2, 3... keeping the best move from the last
            // FULLY COMPLETED depth. If cancelled or over-budget mid-depth, we discard that
            // partial depth and return the previous complete one.
            for (int depth = 1; depth <= settings.MaxDepth; depth++)
            {
                if (ct.IsCancellationRequested) break;

                int alpha = -Infinity;
                int beta = Infinity;
                MoveCommand bestThisDepth = bestMove;
                int bestScore = -Infinity;
                bool completed = true;

                for (int i = 0; i < _rootMoves.Count; i++)
                {
                    if (ct.IsCancellationRequested) { completed = false; break; }

                    MoveCommand move = _rootMoves[i];
                    _engine.ApplyMove(board, move);

                    // Root moves are always the current player's own choices. An Act or Defection
                    // at the root keeps the SAME player to move, so we recurse without negation.
                    int score = ScoreChild(board, move, depth - 1, alpha, beta, rootTeam, ct);

                    _engine.UndoMove(board, move);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestThisDepth = move;
                    }
                    if (score > alpha) alpha = score;
                }

                if (completed)
                {
                    bestMove = bestThisDepth;
                    // Move ordering payoff: put the best move first next iteration for max cutoffs.
                    MoveToFront(bestMove);

                    // Early exit on forced mate found — no deeper search changes the decision.
                    if (bestScore >= MateScore) break;
                }
            }

            return bestMove;
        }

        /// <summary>
        /// Applies the agent-level Betrayal policy (Issue B). Act moves are stripped from the AGENT'S
        /// choices only in DefendOnly mode — but ONLY here at the root. Inside the recursion the
        /// opponent's Act moves are fully explored, so the AI still sees (and correctly fears) a
        /// human-initiated Betrayal even when it won't start one itself.
        /// </summary>
        private void BuildRootMoves(BoardState board, Team team, BetrayalUsage usage)
        {
            _rootMoves.Clear();
            _engine.GetAllLegalMoves(board, team, _rootMoves);

            if (usage == BetrayalUsage.DefendOnly)
            {
                int write = 0;
                for (int i = 0; i < _rootMoves.Count; i++)
                {
                    if (_rootMoves[i].Stage != BetrayalStage.Act)
                        _rootMoves[write++] = _rootMoves[i];
                }
                if (write < _rootMoves.Count)
                    _rootMoves.RemoveRange(write, _rootMoves.Count - write);
            }
        }

        /// <summary>
        /// Recursive negamax. 'perspectiveTeam' is whichever side we're currently scoring FOR
        /// (it changes only when the turn actually flips). alpha/beta are always in perspectiveTeam's frame.
        /// </summary>
        private int Search(BoardState board, int depth, int alpha, int beta, Team perspectiveTeam, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return 0;

            // Terminal / horizon: drop into quiescence so we never evaluate mid-capture or,
            // critically, mid-Retribution (see Quiescence).
            if (depth <= 0)
                return Quiescence(board, alpha, beta, perspectiveTeam, ct);

            List<MoveCommand> moves = _moveBuffers[depth];
            moves.Clear();

            // GetAllLegalMoves returns ONLY Retribution moves when a betrayer is pending, so the
            // multi-phase machine "just works" as ordinary children here — no phase flag needed in
            // the signature; the board's own state drives which moves come back.
            _engine.GetAllLegalMoves(board, board.CurrentTurn, moves);

            if (moves.Count == 0)
            {
                // No legal moves: checkmate (bad for side to move) or stalemate (draw).
                bool inCheck = _engine.IsKingInCheck(board, board.CurrentTurn);
                if (inCheck)
                {
                    // Mate. Prefer faster mates: subtract depth so shallower mates score higher.
                    int mate = -(MateScore - depth);
                    return board.CurrentTurn == perspectiveTeam ? mate : -mate;
                }
                return 0; // stalemate
            }

            OrderMoves(moves);

            int best = -Infinity;
            for (int i = 0; i < moves.Count; i++)
            {
                if (ct.IsCancellationRequested) return best;

                MoveCommand move = moves[i];
                _engine.ApplyMove(board, move);
                int score = ScoreChild(board, move, depth - 1, alpha, beta, perspectiveTeam, ct);
                _engine.UndoMove(board, move);

                if (score > best) best = score;
                if (best > alpha) alpha = best;

                // Beta cutoff. Valid across the Retribution sub-phase precisely because the
                // non-flipping plies don't swap the alpha/beta frame — so a cutoff proven inside
                // a betrayal sequence is still a cutoff for the same maximizer that owns this node.
                if (alpha >= beta) break;
            }

            return best;
        }

        /// <summary>
        /// The heart of Betrayal-correct search: decide whether this child flips the turn.
        ///   - Turn FLIPPED (normal move, Retribution, DefensiveOverride): standard negamax —
        ///     negate the child and swap/negate the alpha-beta window.
        ///   - Turn DID NOT FLIP (Act, Defection): SAME player moves again. Recurse WITHOUT
        ///     negation and WITHOUT swapping the window. perspectiveTeam is unchanged.
        /// This is the exact mirror of Core's ApplyZobristMove turn-hash rule.
        /// </summary>
        private int ScoreChild(BoardState board, MoveCommand move, int childDepth,
                               int alpha, int beta, Team perspectiveTeam, CancellationToken ct)
        {
            if (StageFlipsTurn(move.Stage))
            {
                // Standard negamax step: opponent to move, minimize from our view => negate.
                Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                return -Search(board, childDepth, -beta, -alpha, childPerspective, ct);
            }
            else
            {
                // Same player continues (mid-Betrayal). No negation, no window swap, same frame.
                // NOTE: we do NOT decrement depth differently here — an Act followed by a forced
                // Retribution is two plies of one turn; letting each consume a ply keeps the depth
                // budget honest and prevents infinite non-flipping recursion.
                return Search(board, childDepth, alpha, beta, perspectiveTeam, ct);
            }
        }

        /// <summary>
        /// True when <paramref name="stage"/> passes the turn to the opponent (Retribution,
        /// DefensiveOverride, and ordinary None moves); false for Act and Defection, which are
        /// half-moves by the same player. Mirrors ChessEngine.ApplyZobristMove's turn-hash toggle
        /// and TurnResolver's NextTurn() calls exactly — if that rule ever changes, update both.
        /// </summary>
        internal static bool StageFlipsTurn(BetrayalStage stage) =>
            stage != BetrayalStage.Act && stage != BetrayalStage.Defection;

        /// <summary>
        /// Quiescence: extends the search through "loud" positions to kill the horizon effect.
        /// BETRAYAL-CRITICAL: if we hit the depth limit while a Betrayer is still pending (Act
        /// played, not yet resolved), the position is NOT quiet — standing pat here would evaluate
        /// a board mid-defection and return garbage (the report's "illusion of material loss").
        /// We must resolve the Retribution/Defection sub-phase before we're allowed to stand pat.
        /// </summary>
        private int Quiescence(BoardState board, int alpha, int beta, Team perspectiveTeam, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return 0;

            // --- Betrayal quiescence guard ---
            bool betrayerPending =
                board.PendingBetrayerSquare.HasValue &&
                board.BetrayalInitiator.HasValue &&
                board.GetPiece(board.PendingBetrayerSquare.Value).Team == board.BetrayalInitiator.Value;

            if (betrayerPending)
            {
                // Force resolution: generate Retribution moves; if none, the engine's Defection path
                // will resolve it. Either way we recurse one more ply into the resolved position.
                List<MoveCommand> retribution = _moveBuffers[0];
                retribution.Clear();
                _engine.GetRetributionMoves(board, board.CurrentTurn,
                                            board.PendingBetrayerSquare.Value, retribution);

                if (retribution.Count == 0)
                {
                    // No legal executioner => forced Defection (side-switch). Apply it, evaluate the
                    // resolved board, unmake. This is where the AI actually "sees" the double-swing:
                    // the defected piece now counts for the opponent in the resolved material sum.
                    DefectionOutcome outcome = ChessEngine.ResolveFailedRetribution(board);
                    int resolvedScore = Quiescence(board, alpha, beta, perspectiveTeam, ct);
                    _engine.UndoMove(board, outcome.DefectionMove);
                    return resolvedScore;
                }

                OrderMoves(retribution);
                int best = -Infinity;
                for (int i = 0; i < retribution.Count; i++)
                {
                    MoveCommand move = retribution[i];
                    _engine.ApplyMove(board, move);
                    // Retribution flips the turn, so negate.
                    Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                    int score = -Quiescence(board, -beta, -alpha, childPerspective, ct);
                    _engine.UndoMove(board, move);
                    if (score > best) best = score;
                    if (best > alpha) alpha = best;
                    if (alpha >= beta) break;
                }
                return best;
            }

            // --- Standard quiescence ---
            int standPat = _evaluator.Evaluate(board, perspectiveTeam);
            if (standPat >= beta) return beta;
            if (standPat > alpha) alpha = standPat;

            // Search captures only (loud moves). Reuse a buffer; filter to captures.
            List<MoveCommand> moves = _moveBuffers[0];
            moves.Clear();
            _engine.GetAllLegalMoves(board, board.CurrentTurn, moves);
            OrderMoves(moves);

            for (int i = 0; i < moves.Count; i++)
            {
                MoveCommand move = moves[i];
                if (!move.IsCapture && move.Stage != BetrayalStage.Act) continue; // quiet move, skip

                _engine.ApplyMove(board, move);
                int score = ScoreChild(board, move, 0, alpha, beta, perspectiveTeam, ct);
                _engine.UndoMove(board, move);

                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }

            return alpha;
        }

        /// <summary>
        /// MVV-LVA-ish ordering + Act-first. Good ordering is what makes alpha-beta cut ~b^(d/2)
        /// instead of b^d. Betrayal Act moves are searched early because they're high-variance and
        /// most likely to cause cutoffs (or refutations) — we want them near the front.
        /// </summary>
        private static void OrderMoves(List<MoveCommand> moves)
        {
            // Simple insertion-sort by a cheap score key; move lists are small (<~45), so this is
            // faster than allocating a comparer/delegate and avoids GC.
            for (int i = 1; i < moves.Count; i++)
            {
                MoveCommand key = moves[i];
                int keyScore = OrderScore(key);
                int j = i - 1;
                while (j >= 0 && OrderScore(moves[j]) < keyScore)
                {
                    moves[j + 1] = moves[j];
                    j--;
                }
                moves[j + 1] = key;
            }
        }

        private static int OrderScore(MoveCommand m)
        {
            int s = 0;
            if (m.Stage == BetrayalStage.Act) s += 5000;           // explore betrayals early
            if (m.IsCapture) s += 1000 + PieceRank(m.CapturedType) * 10 - PieceRank(m.PieceType);
            if (m.IsPromotion) s += 800;
            return s;
        }

        private static int PieceRank(ChessPieceType t) => t switch
        {
            ChessPieceType.Queen => 9,
            ChessPieceType.Rook => 5,
            ChessPieceType.Bishop => 3,
            ChessPieceType.Knight => 3,
            ChessPieceType.Pawn => 1,
            _ => 0
        };

        private void MoveToFront(MoveCommand move)
        {
            int idx = _rootMoves.IndexOf(move);
            if (idx > 0)
            {
                _rootMoves.RemoveAt(idx);
                _rootMoves.Insert(0, move);
            }
        }
    }
}
