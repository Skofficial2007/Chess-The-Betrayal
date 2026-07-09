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

        // Hard backstop for quiescence recursion. A Betrayal sub-phase (Act -> Retribution/Defection
        // -> optional DefensiveOverride) is at most a handful of plies for one turn, and captures
        // fizzle out fast, so a real quiescence line is short. This cap exists purely so that a future
        // Betrayal-state edge case can never StackOverflow the worker thread the way the unresolved
        // forced-Defection loop once did — if we ever hit it we stand pat rather than recurse forever.
        private const int MaxQuiescencePly = 64;

        private readonly IChessEngine _engine;
        private readonly IPositionEvaluator _evaluator;

        // Reused across the whole search — one buffer per depth level to avoid clobbering a
        // parent's move list while recursing. Grown lazily; never freed. No per-node allocation.
        private readonly List<MoveCommand>[] _moveBuffers;
        private readonly List<MoveCommand> _rootMoves = new List<MoveCommand>(64);

        // One move buffer per quiescence recursion level, indexed by the qply budget. Quiescence is
        // reached from Search(depth 0), so it must NOT borrow a depth-indexed _moveBuffers slot — the
        // ancestor Search frames at depth 1..N are still iterating their own _moveBuffers[depth] lists.
        // And a SINGLE shared quiescence buffer is not enough either: a Retribution/DefensiveOverride
        // loop holds its buffer across a -Quiescence(...) recursion that can itself open a new Betrayal
        // sub-phase, which would clobber the parent's list mid-iteration. Keying by qply gives every
        // nested quiescence ply its own buffer with zero per-node allocation (pool built once here).
        private readonly List<MoveCommand>[] _quiescenceBuffers;

        public AlphaBetaSearch(IChessEngine engine, IPositionEvaluator evaluator, int maxSupportedDepth = 32)
        {
            _engine = engine;
            _evaluator = evaluator;
            _moveBuffers = new List<MoveCommand>[maxSupportedDepth + 1];
            for (int i = 0; i < _moveBuffers.Length; i++)
                _moveBuffers[i] = new List<MoveCommand>(64);

            // One buffer per possible qply value (0..MaxQuiescencePly inclusive). Indexed by the live
            // qply so each nested quiescence frame has a private list — see the field comment above.
            _quiescenceBuffers = new List<MoveCommand>[MaxQuiescencePly + 1];
            for (int i = 0; i < _quiescenceBuffers.Length; i++)
                _quiescenceBuffers[i] = new List<MoveCommand>(48);
        }

        /// <summary>The private per-ply quiescence move buffer for the given qply budget.</summary>
        private List<MoveCommand> QuiescenceBuffer(int qply)
        {
            List<MoveCommand> buffer = _quiescenceBuffers[qply];
            buffer.Clear();
            return buffer;
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
                int bestIndexThisDepth = 0;
                int bestScore = -Infinity;
                bool completed = true;

                for (int i = 0; i < _rootMoves.Count; i++)
                {
                    if (ct.IsCancellationRequested) { completed = false; break; }

                    MoveCommand move = _rootMoves[i];
                    ApplyMoveAndTurn(board, move);

                    // Root moves are always the current player's own choices. An Act or Defection
                    // at the root keeps the SAME player to move, so we recurse without negation.
                    int score = ScoreChild(board, move, depth - 1, alpha, beta, rootTeam, ct);

                    UndoMoveAndTurn(board, move);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestThisDepth = move;
                        bestIndexThisDepth = i;
                    }
                    if (score > alpha) alpha = score;
                }

                if (completed)
                {
                    bestMove = bestThisDepth;
                    // Move ordering payoff: put the best move first next iteration for max cutoffs.
                    // Uses the index found above — MoveCommand has no IEquatable, so an IndexOf(move)
                    // lookup here would box through the reflection-based ValueType.Equals fallback.
                    MoveToFront(bestIndexThisDepth);

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
            _engine.GetAllLegalMovesIncludingBetrayal(board, team, _rootMoves);

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
                return Quiescence(board, alpha, beta, perspectiveTeam, MaxQuiescencePly, ct);

            List<MoveCommand> moves = _moveBuffers[depth];
            moves.Clear();

            // GetAllLegalMovesIncludingBetrayal returns ONLY Retribution moves when a betrayer is
            // pending (same fallthrough GetAllLegalMoves has), so the multi-phase machine "just
            // works" as ordinary children here — no phase flag needed in the signature; the
            // board's own state drives which moves come back. Includes Act moves (unlike
            // GetAllLegalMoves) so the search can both play Betrayal itself and see the opponent
            // threatening one, at every ply — DefendOnly strips Act from the AGENT's choices only
            // at the root (see BuildRootMoves); this recursion never filters.
            _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, moves);

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
                ApplyMoveAndTurn(board, move);
                int score = ScoreChild(board, move, depth - 1, alpha, beta, perspectiveTeam, ct);
                UndoMoveAndTurn(board, move);

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
        /// Applies a move AND advances board.CurrentTurn to match — IChessEngine.ApplyMove
        /// deliberately does not touch CurrentTurn (per-ply turn control belongs to the caller),
        /// but GetAllLegalMoves/IsKingInCheck/GetRetributionMoves all read CurrentTurn to know
        /// whose position they're looking at. The search is the caller responsible for keeping it
        /// in sync, using the exact same StageFlipsTurn rule ApplyZobristMove and TurnResolver use.
        /// </summary>
        private void ApplyMoveAndTurn(BoardState board, MoveCommand move)
        {
            _engine.ApplyMove(board, move);
            if (StageFlipsTurn(move.Stage)) board.NextTurn();
        }

        /// <summary>Mirror of <see cref="ApplyMoveAndTurn"/> — restores CurrentTurn before undoing
        /// the move itself, so UndoMove sees the same CurrentTurn ApplyMove was called with.</summary>
        private void UndoMoveAndTurn(BoardState board, MoveCommand move)
        {
            if (StageFlipsTurn(move.Stage)) board.NextTurn();
            _engine.UndoMove(board, move);
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
        /// half-moves by the same player. Delegates to the canonical rule on BetrayalStageRules
        /// (Core) — kept as its own named method here since the search's internal call sites and
        /// SearchTurnFlipAgreementTests already reference AlphaBetaSearch.StageFlipsTurn by name.
        /// </summary>
        internal static bool StageFlipsTurn(BetrayalStage stage) => BetrayalStageRules.FlipsTurn(stage);

        /// <summary>
        /// Quiescence: extends the search through "loud" positions to kill the horizon effect.
        /// BETRAYAL-CRITICAL: if we hit the depth limit while a Betrayer is still pending (Act
        /// played, not yet resolved), the position is NOT quiet — standing pat here would evaluate
        /// a board mid-defection and return garbage (the report's "illusion of material loss").
        /// We must resolve the Retribution/Defection/ForcedSave sub-phase before we're allowed to
        /// stand pat, driving it through exactly the same rule the domain uses in
        /// TurnResolver.ResultFromDefectionOutcome (clear pending + pass the turn on a plain
        /// Defection; keep pending + let the SAME side owe a DefensiveOverride on a ForcedSave).
        ///
        /// <paramref name="qply"/> is a hard recursion budget (defense-in-depth): a Betrayal
        /// sub-phase and capture chains are short, so a genuine line never approaches it — it exists
        /// only so no future Betrayal-state edge case can StackOverflow the way an unresolved forced
        /// Defection once did. When it runs out we stand pat rather than recurse further.
        /// </summary>
        private int Quiescence(BoardState board, int alpha, int beta, Team perspectiveTeam, int qply, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return 0;

            // --- Betrayal quiescence guard ---
            bool betrayerPending =
                board.PendingBetrayerSquare.HasValue &&
                board.BetrayalInitiator.HasValue &&
                board.GetPiece(board.PendingBetrayerSquare.Value).Team == board.BetrayalInitiator.Value;

            if (betrayerPending)
            {
                // Backstop: if we've somehow burned the whole quiescence budget while still mid-
                // Betrayal, stop recursing and evaluate in place rather than risk a StackOverflow.
                if (qply <= 0)
                    return _evaluator.Evaluate(board, perspectiveTeam);

                // Force resolution: generate Retribution moves; if none, the domain's Defection path
                // resolves it. Either way we recurse one more ply into the resolved position.
                List<MoveCommand> retribution = QuiescenceBuffer(qply);
                _engine.GetRetributionMoves(board, board.CurrentTurn,
                                            board.PendingBetrayerSquare.Value, retribution);

                if (retribution.Count == 0)
                    return ResolveForcedDefection(board, alpha, beta, perspectiveTeam, qply, ct);

                OrderMoves(retribution);
                int best = -Infinity;
                for (int i = 0; i < retribution.Count; i++)
                {
                    MoveCommand move = retribution[i];
                    ApplyMoveAndTurn(board, move);
                    // Retribution flips the turn, so negate.
                    Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                    int score = -Quiescence(board, -beta, -alpha, childPerspective, qply - 1, ct);
                    UndoMoveAndTurn(board, move);
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

            if (qply <= 0) return alpha; // out of budget: stand pat on the quiet-ish position.

            // Search captures only (loud moves) plus Act, which the loop below explicitly keeps —
            // reuse this ply's private buffer; filter happens per-move just below.
            List<MoveCommand> moves = QuiescenceBuffer(qply);
            _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, moves);
            OrderMoves(moves);

            for (int i = 0; i < moves.Count; i++)
            {
                MoveCommand move = moves[i];
                if (!move.IsCapture && move.Stage != BetrayalStage.Act) continue; // quiet move, skip

                ApplyMoveAndTurn(board, move);
                // Recurse within quiescence (not back into Search) so the qply budget carries through
                // capture/Act chains — an Act keeps the same side to move and leaves a Betrayer
                // pending, so the same-perspective recurse re-enters the guard above; a capture flips
                // the turn and negates like a normal ply. Mirrors ScoreChild's flip rule.
                int score;
                if (StageFlipsTurn(move.Stage))
                {
                    Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                    score = -Quiescence(board, -beta, -alpha, childPerspective, qply - 1, ct);
                }
                else
                {
                    score = Quiescence(board, alpha, beta, perspectiveTeam, qply - 1, ct);
                }
                UndoMoveAndTurn(board, move);

                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }

            return alpha;
        }

        /// <summary>
        /// Test seam: run a full quiescence evaluation from <paramref name="board"/> for
        /// <paramref name="perspectiveTeam"/>, using the standard open window and ply budget. Lets
        /// SearchCorrectnessTests drive the Betrayal forced-Defection/ForcedSave branches directly on
        /// a crafted pending-Betrayer position, rather than having to engineer a full search tree that
        /// happens to hit the horizon exactly on that state. Not part of the search's own control flow.
        /// </summary>
        internal int RunQuiescenceForTest(BoardState board, Team perspectiveTeam, CancellationToken ct) =>
            Quiescence(board, -Infinity, Infinity, perspectiveTeam, MaxQuiescencePly, ct);

        /// <summary>
        /// The forced-Defection branch of quiescence: no legal Executioner exists, so the Betrayer
        /// defects (Resolution B). This mirrors <see cref="TurnResolver.ResultFromDefectionOutcome"/>
        /// exactly — the reason the old code StackOverflowed is that it recursed WITHOUT reproducing
        /// that resolution, so the pending-Betrayer state was never cleared and the same branch
        /// re-entered on the same square forever.
        ///
        /// Two sub-cases, split on <see cref="DefectionOutcome.RequiresForcedSave"/>:
        ///   - No ForcedSave: the sequence is fully resolved. Clear the pending state and PASS THE
        ///     TURN (toggle turn-hash + flip perspective + swap/negate the window), just like a
        ///     normal turn-flipping ply, then recurse into the opponent's reply.
        ///   - ForcedSave: the defected piece checks its former King, so the SAME side owes a
        ///     mandatory DefensiveOverride (king-save). Pending state stays set and the turn does not
        ///     flip; recurse over GetForcedSaveMoves in the same perspective/window.
        /// On unmake, <see cref="IChessEngine.UndoMove"/> restores PendingBetrayerSquare/
        /// BetrayalInitiator from the Defection move's Previous* snapshot and — for a Defection that
        /// closed its sequence without a ForcedSave — reverses the turn-hash/sub-state hash toggles
        /// ChessEngine.ResolveDefection performed, symmetrically. Any manual CurrentTurn flip made
        /// here must still be reversed FIRST, since the move carries no record of it.
        /// </summary>
        private int ResolveForcedDefection(BoardState board, int alpha, int beta,
                                           Team perspectiveTeam, int qply, CancellationToken ct)
        {
            // ResolveFailedRetribution applies the Defection move directly via ChessEngine (bypassing
            // ApplyMoveAndTurn). A Defection that requires a ForcedSave never flips the turn or
            // touches the turn-hash; one that doesn't require a ForcedSave already did both (and
            // cleared the pending fields) inside ChessEngine.ResolveDefection — see the no-ForcedSave
            // branch below for what's left for the search to do.
            DefectionOutcome outcome = ChessEngine.ResolveFailedRetribution(board);

            int score;
            if (outcome.RequiresForcedSave)
            {
                // ForcedSave: same side owes a DefensiveOverride, pending state stays set, no turn flip.
                // Safe to reuse this ply's buffer — the retribution list that shared it was already
                // consumed (count==0) before we got here, and the save loop below only recurses into
                // Quiescence(qply-1), which owns a different buffer.
                List<MoveCommand> saves = QuiescenceBuffer(qply);
                _engine.GetForcedSaveMoves(board, board.CurrentTurn, saves);

                if (saves.Count == 0)
                {
                    // No legal king-save after the self-check: this side is mated inside the
                    // Betrayal sequence. Score it as a mate against the side to move, in perspective.
                    score = board.CurrentTurn == perspectiveTeam ? -MateScore : MateScore;
                }
                else
                {
                    OrderMoves(saves);
                    int best = -Infinity;
                    for (int i = 0; i < saves.Count; i++)
                    {
                        MoveCommand save = saves[i];
                        ApplyMoveAndTurn(board, save);
                        // DefensiveOverride flips the turn, so negate and swap the window.
                        Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                        int childScore = -Quiescence(board, -beta, -alpha, childPerspective, qply - 1, ct);
                        UndoMoveAndTurn(board, save);
                        if (childScore > best) best = childScore;
                        if (best > alpha) alpha = best;
                        if (alpha >= beta) break;
                    }
                    score = best;
                }
            }
            else
            {
                // No ForcedSave: the Betrayal sequence is fully resolved and the turn passes. This is
                // the exact fix for the old infinite loop: previously the search never flipped, so it
                // re-entered the same pending-Betrayer branch forever.
                //
                // ResolveFailedRetribution (via ChessEngine.ResolveDefection) already toggled the
                // turn-hash AND the pending-Betrayer sub-state hash, and cleared
                // PendingBetrayerSquare/BetrayalInitiator — atomically, the moment it determined no
                // ForcedSave was required. UndoMove below replays the stamped DefectionMove through
                // ApplyZobristMove, which reverses both toggles symmetrically. CurrentTurn is the one
                // piece of state that's ours to flip — it's per-caller bookkeeping the move doesn't
                // carry, exactly like TurnResolver.ResultFromDefectionOutcome flips it directly too.
                board.NextTurn();

                Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                score = -Quiescence(board, -beta, -alpha, childPerspective, qply - 1, ct);

                // Reverse our manual CurrentTurn flip before UndoMove restores the rest.
                board.NextTurn();
            }

            // Unmake the Defection. UndoMove restores PendingBetrayerSquare/BetrayalInitiator from the
            // move's Previous* snapshot, and — for a stamped (ClosesBetrayalSequence) move — reverses
            // the turn-hash and sub-state hash toggles ResolveDefection performed above, symmetrically
            // via ApplyZobristMove. The board returns to exactly the pre-resolution state either way.
            _engine.UndoMove(board, outcome.DefectionMove);
            return score;
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

        private void MoveToFront(int index)
        {
            if (index <= 0) return;

            MoveCommand move = _rootMoves[index];
            _rootMoves.RemoveAt(index);
            _rootMoves.Insert(0, move);
        }
    }
}
