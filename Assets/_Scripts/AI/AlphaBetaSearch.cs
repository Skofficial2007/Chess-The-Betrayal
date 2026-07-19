using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ChessTheBetrayal.EditorTools")]

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

        // Null Move Pruning: the reduction grows with depth so a deep node skips more of the
        // opponent's reply before deciding a null move is safe, while a shallow node stays
        // conservative. Minimum search depth to even attempt it scales the same way, so the
        // recursive null-move search itself is never asked to search below depth 0.
        private const int NullMoveBaseReduction = 2;
        private static int NullMoveReduction(int depth) => NullMoveBaseReduction + depth / 6;
        private static int NullMoveMinDepth(int depth) => NullMoveReduction(depth) + 2; // guard 3: depth >= R + 2

        // Reverse futility pruning (a.k.a. static null-move pruning): only attempted within this many
        // plies of the horizon — beyond that the static eval is too weak a proxy for "this node is
        // hopeless for the opponent" to trust without search. Margin grows with depth so a node
        // further from the horizon demands a bigger static cushion before it's allowed to skip search
        // entirely, mirroring how NullMoveReduction scales its own confidence with depth.
        private const int ReverseFutilityMaxDepth = 6;
        private static int ReverseFutilityMargin(int depth) => 90 * depth;

        // Move-count pruning (LMP): once this many quiet moves have already been searched at a
        // shallow node without improving alpha, later quiet moves in the same list are skipped
        // outright rather than searched — a quiet move that far down an already-ordered list is
        // vanishingly unlikely to be the best move at a node this close to the horizon. Only ever
        // applied to quiet, non-TT, non-killer moves (see IsReducibleMove's own exemptions), so a
        // capture/promotion/Betrayal-stage move is never skipped this way.
        private const int LateMovePruningMaxDepth = 4;
        private static int LateMovePruningThreshold(int depth) => 3 + depth * depth;

        // Frontier futility pruning: at a node this close to the horizon, a quiet move that can't
        // even in its best case (static eval + margin) reach alpha is skipped without being
        // searched. The margin scales with depth for the same reason ReverseFutilityMargin's does —
        // more plies of remaining search means more room for the position to improve, so the cutoff
        // needs a bigger cushion to stay safe. Exempt from this: any move that leaves the opponent
        // in check, since that promise of forced follow-up is exactly what a purely-static eval
        // can't see (this is why quiescence itself never stands pat while resolving one either).
        private const int FrontierFutilityMaxDepth = 3;
        private static int FrontierFutilityMargin(int depth) => 150 + 100 * depth;

        // Betrayal/Retribution search extension: a Betrayal Act stages a forced tactical sequence
        // (the target ally MUST execute Retribution next, or the Betrayer defects), so treating it
        // as "just another ply" like an ordinary quiet move risks the horizon landing mid-sequence
        // and mis-valuing a position that hasn't actually finished playing out yet. Granting the
        // very next ply back — searching it at the SAME depth the Act was found at, instead of one
        // shallower — keeps the whole forced sequence inside the normal search window rather than
        // eating into the depth budget the same way a real "free" ply would. Capped per line
        // (extensionsUsedThisLine) so a chain of Betrayals can never re-extend itself forever the
        // way an uncapped forced-capture-chain extension is well known to blow up a search tree.
        private const int MaxBetrayalExtensionsPerLine = 3;

        // Hard backstop for quiescence recursion. A Betrayal sub-phase (Act -> Retribution/Defection
        // -> optional DefensiveOverride) is at most a handful of plies for one turn, and captures
        // fizzle out fast, so a real quiescence line is short. This cap exists purely so that a future
        // Betrayal-state edge case can never StackOverflow the worker thread the way the unresolved
        // forced-Defection loop once did — if we ever hit it we stand pat rather than recurse forever.
        private const int MaxQuiescencePly = 64;

        // A node deep enough to matter but with no TT move at all gets a cheap shallower probe first,
        // purely to seed move ordering for the real search that follows. Below this depth the node is
        // already cheap enough that skipping straight to the real search costs less than the probe would.
        private const int InternalIterativeReductionMinDepth = 4;
        private const int InternalIterativeReductionAmount = 1;

        // Instability time management: once the soft half of the time budget has elapsed, whether
        // iterative deepening starts another depth depends on how settled the root looks. A root is
        // "stable" once the last two completed depths agree on both the best move AND a close-enough
        // score — either signal alone is too weak (the score can swing wildly while the same move
        // stays best because a deeper tactic was just found under it, and the score can stay flat
        // while the best move itself flip-flops between two nearly-equal candidates), so both must
        // hold before another depth is judged unnecessary. The score window reuses the same order of
        // magnitude this codebase already treats as "noise" for move selection (AIProfileTable's own
        // tie-break windows run 10-30cp). The minimum-depth floor reuses AIProfileGuardrails' own
        // "a shallow search can't be trusted" threshold rather than inventing a second one.
        private const int StabilityThresholdCp = 25;

        // Aspiration windows (experimental, off by default — see FindBestMove's own doc comment):
        // instead of always searching a fresh depth with the full [-Infinity, +Infinity] window,
        // guess that the score won't move far from the previous depth's answer and search a narrow
        // band around it first. Most of the time that guess holds and every cutoff in the tree gets
        // tighter for free; when it doesn't, the result lands exactly on the window's edge (a
        // fail-low or fail-high) rather than a real score, and that whole depth must be re-searched
        // with the full window before its result can be trusted — a narrow window can never be
        // allowed to silently stand in for the true value. The margin is wider than
        // StabilityThresholdCp on purpose: that constant exists to detect when two depths agree
        // closely enough to stop searching, which wants to be strict; this one exists to avoid
        // triggering an expensive re-search on perfectly ordinary depth-to-depth drift, which wants
        // to be forgiving.
        private const int AspirationWindowCp = 50;

        private readonly IChessEngine _engine;
        private readonly IPositionEvaluator _evaluator;
        private readonly TranspositionTable _tt;
        private readonly int _maxSupportedDepth;

        // Reused across the whole search — one buffer per ply-from-root to avoid clobbering a
        // parent's move list while recursing. Grown lazily; never freed. No per-node allocation.
        //
        // Indexed by plyFromRoot, NOT by depth. depth used to be a safe index on its own — every
        // recursive step strictly decreased it, so no two active stack frames could ever hold the
        // same depth value at once. The Betrayal/Retribution extension breaks that: an Act that
        // stages a forced Retribution gets +1 ply of depth BACK, so depth can legitimately repeat
        // across nested frames now (a deep descendant can numerically match an ancestor's depth).
        // plyFromRoot never has this problem — it strictly increases on every single recursive step
        // with no exceptions (see ScoreChild, which always receives the caller's already-incremented
        // plyFromRoot down both its flipping and non-flipping branches) — so it's the only safe key
        // once depth-restoring extensions exist. Mirrors _killerMoves' own plyFromRoot keying and
        // maxSupportedDepth-based sizing below. Not readonly (unlike _quiescenceBuffers) because
        // MoveBuffer can grow this array in place via Array.Resize if plyFromRoot ever exceeds the
        // ctor's initial sizing — see MoveBuffer's own doc comment.
        private List<MoveCommand>[] _moveBuffers;
        private readonly List<MoveCommand> _rootMoves = new List<MoveCommand>(64);

        // Parallel to _rootMoves by index — every root move's score at the last FULLY COMPLETED
        // depth (see FindBestMove's completed-gated commit). _rootScoresScratch is the per-depth
        // working copy written unconditionally during the loop; _rootScores only receives a copy
        // from it once a depth actually completes, so a cancelled/partial depth's scores never
        // leak into the externally-visible array. Sized comfortably above realistic
        // Betrayal-inclusive root branching; grown lazily (mirrors _moveBuffers' own policy) so a
        // pathological position can never index out of bounds without violating steady-state
        // zero-GC in the common case. MoveSelectionPolicy.MaxRootMoves must match this sizing.
        private int[] _rootScores = new int[128];
        private int[] _rootScoresScratch = new int[128];

        // One move buffer per quiescence recursion level, indexed by the qply budget. Quiescence is
        // reached from Search(depth 0), so it must NOT borrow a _moveBuffers slot — every ancestor
        // Search frame still on the stack is still iterating its own plyFromRoot-keyed list there.
        // And a SINGLE shared quiescence buffer is not enough either: a Retribution/DefensiveOverride
        // loop holds its buffer across a -Quiescence(...) recursion that can itself open a new Betrayal
        // sub-phase, which would clobber the parent's list mid-iteration. Keying by qply gives every
        // nested quiescence ply its own buffer with zero per-node allocation (pool built once here).
        private readonly List<MoveCommand>[] _quiescenceBuffers;

        // Quiet-move ordering memory, both allocated once here and reused for the whole search —
        // same zero-per-node-allocation discipline as _moveBuffers/_quiescenceBuffers above.
        //
        // History: for a quiet move that caused a beta cutoff, remember it by [piece type][target
        // square] so the same kind of move gets tried earlier next time it's legal somewhere else in
        // the tree. Indexed by ChessPieceType (0..6) and by the 64 board squares (EndPosition.y * 8 +
        // EndPosition.x). Only ever touched for moves with Stage == BetrayalStage.None — a Betrayal
        // Act/Retribution/DefensiveOverride/Defection move is never "just a quiet developing move" in
        // the sense this heuristic is trying to capture, so mixing its stats in would blur the signal
        // for ordinary positions without helping Betrayal ones either.
        private readonly int[,] _historyScores = new int[7, 64];

        // Killers: the two most recent quiet moves that caused a beta cutoff AT THIS EXACT PLY FROM
        // ROOT, tried right after the TT move and winning captures. Keyed by plyFromRoot rather than
        // by whose turn it is, because Search already increments plyFromRoot on every single
        // recursive descent — including the non-flipping Act/Retribution steps inside a Betrayal
        // sequence — so an Act at ply k and its forced Retribution at ply k+1 land in different slots
        // automatically. Keying by side-to-move instead would be wrong here, since Betrayal doesn't
        // flip the mover every ply the way ordinary chess does.
        private readonly uint[,] _killerMoves;

        // Capture ordering's SEE cache, one slot per move-list index — filled once, up front, by
        // OrderMoves' pre-pass, then read (never recomputed) for every comparison the insertion
        // sort makes below. SEE walks every piece on the board, so calling it fresh on each of the
        // O(n^2) comparisons an insertion sort can make would be far more expensive than the
        // ordering win it buys; computing each move's score exactly once avoids that. Sized to the
        // same ceiling as _moveBuffers' individual lists and grown lazily on the same "never shrink"
        // policy as _rootScores.
        private int[] _seeScoreCache = new int[64];

        public AlphaBetaSearch(IChessEngine engine, IPositionEvaluator evaluator, int maxSupportedDepth = 32,
                                TranspositionTable transpositionTable = null)
        {
            _engine = engine;
            _evaluator = evaluator;
            _tt = transpositionTable ?? new TranspositionTable(log2Size: 16);
            _maxSupportedDepth = maxSupportedDepth;
            _moveBuffers = new List<MoveCommand>[maxSupportedDepth + 1];
            for (int i = 0; i < _moveBuffers.Length; i++)
                _moveBuffers[i] = new List<MoveCommand>(64);

            // One buffer per possible qply value (0..MaxQuiescencePly inclusive). Indexed by the live
            // qply so each nested quiescence frame has a private list — see the field comment above.
            _quiescenceBuffers = new List<MoveCommand>[MaxQuiescencePly + 1];
            for (int i = 0; i < _quiescenceBuffers.Length; i++)
                _quiescenceBuffers[i] = new List<MoveCommand>(48);

            // Two killer slots per ply from root, indexed 0..maxSupportedDepth (a null-move-reduced
            // or extended line can in principle exceed the nominal search depth, so the buffer is
            // sized against the same maxSupportedDepth ceiling _moveBuffers already trusts).
            _killerMoves = new uint[maxSupportedDepth + 1, 2];
        }

        /// <summary>The private per-ply quiescence move buffer for the given qply budget.</summary>
        private List<MoveCommand> QuiescenceBuffer(int qply)
        {
            List<MoveCommand> buffer = _quiescenceBuffers[qply];
            buffer.Clear();
            return buffer;
        }

        /// <summary>The private per-ply-from-root main-search move buffer (see the _moveBuffers
        /// field comment for why this is keyed by plyFromRoot rather than depth). Grows the backing
        /// array lazily — mirrors _rootScores/_seeScoreCache's own "never shrink" policy — since a
        /// deep enough chain of Betrayal extensions can in principle push plyFromRoot past the
        /// ctor's maxSupportedDepth-based initial sizing, even though depth itself never exceeds it
        /// (the depth < _maxSupportedDepth guard on the extension itself sees to that).</summary>
        private List<MoveCommand> MoveBuffer(int plyFromRoot)
        {
            if (plyFromRoot >= _moveBuffers.Length)
            {
                int newSize = plyFromRoot + 1;
                Array.Resize(ref _moveBuffers, newSize);
                for (int i = 0; i < _moveBuffers.Length; i++)
                    _moveBuffers[i] ??= new List<MoveCommand>(64);
            }

            List<MoveCommand> buffer = _moveBuffers[plyFromRoot];
            buffer.Clear();
            return buffer;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Telemetry for the most recently completed FindBestMove call (AI-21). Lives on the shared
        /// TranspositionTable rather than a second bag, since every AlphaBetaSearch already owns one
        /// TT reference and the TT's own counters (probe/hit/store/replace) need to live there
        /// anyway. Reset at the top of FindBestMove, so a mid-search read (there isn't one — this is
        /// read after the call returns) would otherwise see a stale previous-call snapshot.
        /// </summary>
        public ref SearchStats Stats => ref _tt.Stats;
#endif

        /// <summary>Ranked root moves from the most recent FindBestMove call, parallel to
        /// <see cref="RootScores"/> by index. Read only after FindBestMove returns and before the
        /// next call reuses the buffer — see MoveSelectionPolicy, the sole external consumer.</summary>
        public IReadOnlyList<MoveCommand> RootMoves => _rootMoves;

        /// <summary>Score of RootMoves[i] at the last FULLY COMPLETED depth (exact where the
        /// bounded candidate re-search ran, an alpha-beta upper bound otherwise — see
        /// FindBestMove's candidateRescoreMarginCp). Only indices [0, RootMoveCount) are valid.</summary>
        public int[] RootScores => _rootScores;

        public int RootMoveCount => _rootMoves.Count;

        /// <summary>Index into RootMoves/RootScores of the search's own best move. Always 0 after a
        /// completed depth — MoveToFront(bestIndexThisDepth) puts the committed best there.</summary>
        public int BestRootIndex { get; private set; }

        /// <summary>
        /// Iterative deepening entry point. Returns the best move for board.CurrentTurn.
        /// Caller runs this on a worker thread against a cloned board (see AsyncAIAgent).
        ///
        /// candidateRescoreMarginCp: when > 0, after the final completed depth, every root move
        /// within this margin of the best (excluding the best itself, already exact by
        /// construction) is re-searched at the same depth with a FULL (-Infinity, +Infinity)
        /// window instead of the tightened alpha-beta window it was found under — this fixes the
        /// "later root moves may only carry an upper-bound score" caveat (see MoveSelectionPolicy)
        /// for the handful of candidates a personality-driven selection might actually pick.
        /// TT-warmed (every node was already visited this search), so it's cheap. Defaults to 0 —
        /// today's exact pre-AI-24 behavior, zero overhead — for callers that don't need it.
        ///
        /// enableInstabilityTimeManagement: off by default so every existing CancellationToken.None
        /// caller (both benchmark suites) stays exactly depth-bound, byte-identical to before this
        /// was added — this flag is what makes that a guarantee of the call site, not a coincidence
        /// of which numbers happen to be passed as the time budget. Real gameplay/tooling callers
        /// (AsyncAIAgent, MobileSearchBenchmarkRunner) pass true: once the soft half of the time
        /// budget elapses, a settled root is allowed to stop early instead of spending the full
        /// budget on a search that's very unlikely to change its mind, and an unsettled root is
        /// allowed to keep going into the soft-to-hard gap instead of stopping the instant soft is
        /// crossed — see the between-depths check below for exactly how "settled" is judged.
        ///
        /// enableAspirationWindows: off by default, same "structural guarantee, not a numeric
        /// coincidence" reasoning as enableInstabilityTimeManagement — every existing
        /// CancellationToken.None caller omits it and is therefore unaffected by construction. When
        /// on, every depth after the first searches a narrow window guessed from the previous
        /// depth's score instead of the full range, re-searching that same depth with the full
        /// window on a fail-low/fail-high before trusting the result. This is an independent,
        /// composable switch from enableInstabilityTimeManagement — either can be on without the
        /// other. Experimental: the literature is genuinely mixed on whether this helps (a
        /// documented case exists of a comparable engine measuring REMOVING aspiration windows as a
        /// net improvement), which is exactly why it ships behind its own flag rather than replacing
        /// the full-window search outright.
        /// </summary>
        public MoveCommand FindBestMove(BoardState board, AISearchSettings settings, CancellationToken ct,
            int candidateRescoreMarginCp = 0, bool enableInstabilityTimeManagement = false,
            bool enableAspirationWindows = false)
        {
            Team rootTeam = board.CurrentTurn;
            Stopwatch stopwatch = enableInstabilityTimeManagement ? Stopwatch.StartNew() : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _tt.Stats.Reset();
#endif
            _tt.NewSearch();
            Array.Clear(_historyScores, 0, _historyScores.Length);

            // Build the root move list ONCE. This is where the agent-level Betrayal policy applies.
            BuildRootMoves(board, rootTeam, settings.BetrayalUsage);
            EnsureRootScoreCapacity(_rootMoves.Count);

            // TT informs root ORDERING ONLY — never a short-circuit. The root list carries the
            // BetrayalUsage.DefendOnly filter and MoveToFront's PV bookkeeping; a TT cutoff here
            // would bypass both. One probe before the depth loop; a packed-move match sorts first.
            // NOTE: this MoveToFront call runs before _rootScores has been populated for THIS
            // search — it permutes stale data left over from the previous call. Harmless: the
            // first completed depth below unconditionally overwrites _rootScores from
            // _rootScoresScratch (in that depth's own index order) before anything reads it.
            uint rootTTMove = 0;
            if (_tt.Probe(board.ZobristHash, out _, out uint probedRootMove, out _, out _))
                rootTTMove = probedRootMove;
            MoveToFrontByPackedMove(rootTTMove);

            MoveCommand bestMove = _rootMoves.Count > 0 ? _rootMoves[0] : default;
            int lastCompletedDepth = 0;

            // Only meaningful when enableInstabilityTimeManagement is on — tracks what the
            // PREVIOUS completed depth found, so the next depth's completion can compare against
            // it. hasPriorCompletedDepth distinguishes "no previous depth yet" from a legitimate
            // score of 0, since a fresh search has nothing to compare its first completed depth to.
            bool hasPriorCompletedDepth = false;
            int previousCompletedScore = 0;
            MoveCommand previousCompletedBestMove = default;

            // Only meaningful when enableAspirationWindows is on — deliberately a SEPARATE pair of
            // locals from the instability-tracking ones above, even though both record "the last
            // completed depth's score": the two flags are independent and composable, so neither
            // one's bookkeeping may depend on whether the other happens to be enabled.
            bool hasPriorAspirationScore = false;
            int priorAspirationScore = 0;

            // Iterative deepening: search depth 1, 2, 3... keeping the best move from the last
            // FULLY COMPLETED depth. If cancelled or over-budget mid-depth, we discard that
            // partial depth and return the previous complete one.
            for (int depth = 1; depth <= settings.MaxDepth; depth++)
            {
                if (ct.IsCancellationRequested) break;

                // Killers are a per-iteration hint (last depth's cutoff-causing quiet moves), not a
                // cross-iteration guarantee — clearing them each time keeps a stale slot from a
                // shallower, since-refuted line from lingering and mis-ordering a deeper one.
                Array.Clear(_killerMoves, 0, _killerMoves.Length);

                MoveCommand bestThisDepth = bestMove;
                int bestIndexThisDepth = 0;
                int bestScore = -Infinity;
                bool completed = true;

                // Aspiration windows: the first attempt at this depth guesses a narrow band around
                // the previous depth's score instead of searching the full range. If that guess
                // turns out wrong (the true score lands ON or PAST the window edge, which alpha-beta
                // reports as a fail-low/fail-high rather than a real value), the whole depth is
                // thrown away and re-searched with the full window before its result is trusted —
                // never trust a bound as if it were an exact score. `widenToFullWindow` starts false
                // (or true outright when the flag is off, or there's no prior score to aspire
                // around, or a bailout condition below decides even a doubled window isn't worth it)
                // so a normal full-window search is always the fallback both features degrade to.
                bool widenToFullWindow = !enableAspirationWindows || !hasPriorAspirationScore;
                int windowMarginCp = AspirationWindowCp;

                while (true)
                {
                    int alpha = widenToFullWindow ? -Infinity : priorAspirationScore - windowMarginCp;
                    int beta = widenToFullWindow ? Infinity : priorAspirationScore + windowMarginCp;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (!widenToFullWindow) _tt.Stats.AspirationWindowAttempts++;
#endif

                    bestThisDepth = bestMove;
                    bestIndexThisDepth = 0;
                    bestScore = -Infinity;
                    completed = true;

                    for (int i = 0; i < _rootMoves.Count; i++)
                    {
                        if (ct.IsCancellationRequested) { completed = false; break; }

                        MoveCommand move = _rootMoves[i];
                        ApplyMoveAndTurn(board, move);

                        // Root moves are always the current player's own choices. An Act or Defection
                        // at the root keeps the SAME player to move, so we recurse without negation.
                        // A root Act that stages a forced Retribution gets the same one-ply extension
                        // an Act found deeper in the tree does (see the move loop below) — root is just
                        // the shallowest possible place for that sequence to start.
                        bool rootGrantsExtension = move.Stage == BetrayalStage.Act && board.PendingBetrayerSquare.HasValue
                            && depth < _maxSupportedDepth;
                        int rootSearchDepth = rootGrantsExtension ? depth : depth - 1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        if (rootGrantsExtension) _tt.Stats.BetrayalExtensions++;
#endif
                        int score = ScoreChild(board, move, rootSearchDepth, 1, alpha, beta, rootTeam, ct, rootGrantsExtension ? 1 : 0);

                        UndoMoveAndTurn(board, move);

                        // Every candidate's score is recorded here (not just the running best) — this
                        // scratch write is unconditional so MoveSelectionPolicy can later rank/bias
                        // among ALL root moves, not only the single winner. Committed into the
                        // externally-visible _rootScores only if this depth completes (below).
                        _rootScoresScratch[i] = score;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestThisDepth = move;
                            bestIndexThisDepth = i;
                        }
                        if (score > alpha) alpha = score;
                    }

                    // A fail-low/fail-high against a narrow window means bestScore is only a bound,
                    // not the true value — the whole depth must be re-searched with the full window.
                    // Cancellation takes priority over a fail-widen retry: an incomplete depth from a
                    // cancelled scan must fall straight through to the completed-gated discard below,
                    // never loop back for another (equally doomed) attempt.
                    if (widenToFullWindow || !completed) break;
                    bool failedLow = bestScore <= priorAspirationScore - windowMarginCp;
                    bool failedHigh = bestScore >= priorAspirationScore + windowMarginCp;
                    if (!failedLow && !failedHigh) break;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.AspirationWindowReSearches++;
#endif
                    widenToFullWindow = true;
                }

                if (completed)
                {
                    bestMove = bestThisDepth;
                    lastCompletedDepth = depth;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.LastCompletedDepth = depth;
#endif

                    // Commit BEFORE MoveToFront, in the pre-permutation index order the scratch
                    // array was just written in — MoveToFront below then shuffles _rootScores in
                    // lockstep with _rootMoves so the two stay aligned as parallel arrays.
                    Array.Copy(_rootScoresScratch, _rootScores, _rootMoves.Count);

                    // Move ordering payoff: put the best move first next iteration for max cutoffs.
                    // Uses the index found above — MoveCommand has no IEquatable, so an IndexOf(move)
                    // lookup here would box through the reflection-based ValueType.Equals fallback.
                    MoveToFront(bestIndexThisDepth);
                    BestRootIndex = 0; // MoveToFront always puts the committed best at index 0

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.AssignNodesAfterDepth(depth, _tt.Stats.NodesVisited + _tt.Stats.QNodesVisited);
#endif

                    // Early exit on forced mate found — no deeper search changes the decision.
                    if (bestScore >= MateScore) break;

                    if (enableAspirationWindows)
                    {
                        hasPriorAspirationScore = true;
                        priorAspirationScore = bestScore;
                    }

                    if (enableInstabilityTimeManagement)
                    {
                        long elapsedMs = stopwatch.ElapsedMilliseconds;

                        // Below the depth floor, the stability signal itself isn't trustworthy yet
                        // (same reasoning AIProfileGuardrails already applies to a shallow search
                        // being asked to vet a reshaped evaluator) — always search on regardless of
                        // elapsed time, exactly like today with the flag off.
                        bool depthDeepEnoughToTrustStability = depth >= AIProfileGuardrails.ShallowSearchDepthThreshold;

                        if (depthDeepEnoughToTrustStability)
                        {
                            bool stable = hasPriorCompletedDepth
                                && IsRootStable(bestScore, previousCompletedScore, bestMove, previousCompletedBestMove);

                            // Settled and past the soft target: further search is unlikely to change
                            // the answer, so stop now rather than spend the rest of the budget.
                            if (stable && elapsedMs >= settings.TimeBudget.SoftMs) break;

                            // Unsettled: allowed to spend into the soft-to-hard gap for one more
                            // depth, but never past the hard ceiling — that ceiling is also what the
                            // external CancelAfter(HardMs) timer backstops independently, so the two
                            // never disagree about what "hard" means.
                            if (!stable && elapsedMs >= settings.TimeBudget.HardMs) break;
                        }

                        hasPriorCompletedDepth = true;
                        previousCompletedScore = bestScore;
                        previousCompletedBestMove = bestMove;
                    }
                }
            }

            RescoreCandidatesWithFullWindow(board, rootTeam, lastCompletedDepth, candidateRescoreMarginCp, ct);

            return bestMove;
        }

        /// <summary>
        /// Un-biases the tightened-alpha-beta-window scores of the handful of root moves close
        /// enough to the best that a personality-driven MoveSelectionPolicy might actually pick —
        /// see FindBestMove's candidateRescoreMarginCp doc comment. No-op when the margin is 0 (the
        /// common case for zero-personality callers) or there's nothing to compare against.
        /// </summary>
        private void RescoreCandidatesWithFullWindow(BoardState board, Team rootTeam, int lastCompletedDepth,
            int candidateRescoreMarginCp, CancellationToken ct)
        {
            if (candidateRescoreMarginCp <= 0 || _rootMoves.Count == 0 || lastCompletedDepth <= 0) return;

            int threshold = _rootScores[0] - candidateRescoreMarginCp; // BestRootIndex == 0
            for (int i = 1; i < _rootMoves.Count; i++) // index 0 is already exact-enough by construction
            {
                // Safe degrade: leave any not-yet-rescored entry at its tightened-window value —
                // still "acceptable by direction" per the ADR's own caveat, never a torn write.
                if (ct.IsCancellationRequested) break;
                if (_rootScores[i] < threshold) continue;

                MoveCommand move = _rootMoves[i];
                ApplyMoveAndTurn(board, move);

                // Mirror FindBestMove's own root extension exactly, so a rescored candidate's depth
                // matches what the original completed-depth search actually explored for it — an
                // Act that stages a Retribution must get the same extra ply here it got the first
                // time, or this "exact" re-search would silently search one ply shallower than the
                // tightened-window pass it's supposed to be correcting.
                bool rootGrantsExtension = move.Stage == BetrayalStage.Act && board.PendingBetrayerSquare.HasValue
                    && lastCompletedDepth < _maxSupportedDepth;
                int rootSearchDepth = rootGrantsExtension ? lastCompletedDepth : lastCompletedDepth - 1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (rootGrantsExtension) _tt.Stats.BetrayalExtensions++;
#endif
                int exactScore = ScoreChild(board, move, rootSearchDepth, 1, -Infinity, Infinity, rootTeam, ct, rootGrantsExtension ? 1 : 0);
                UndoMoveAndTurn(board, move);

                _rootScores[i] = exactScore;
            }
        }

        /// <summary>Grows the root-score buffers to fit a pathological branching root, mirroring
        /// _moveBuffers' own "grown lazily, never freed" policy. A no-op (and therefore zero-GC)
        /// for every position within the initial 128-move capacity.</summary>
        private void EnsureRootScoreCapacity(int requiredCount)
        {
            if (_rootScores.Length >= requiredCount) return;

            int newSize = requiredCount * 2;
            Array.Resize(ref _rootScores, newSize);
            Array.Resize(ref _rootScoresScratch, newSize);
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
        private int Search(BoardState board, int depth, int plyFromRoot, int alpha, int beta, Team perspectiveTeam, CancellationToken ct, bool parentWasNull = false, int extensionsUsedThisLine = 0, bool iirAllowed = true)
        {
            if (ct.IsCancellationRequested) return 0;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _tt.Stats.NodesVisited++;
#endif

            // Terminal / horizon: drop into quiescence so we never evaluate mid-capture or,
            // critically, mid-Retribution (see Quiescence). Quiescence does not probe/store the TT
            // (qsearch entries would thrash the table) and its nodes are deliberately excluded from
            // NodesVisited — that counter tracks the pruned tree the TT/NMP/LMR/PVS multipliers act
            // on, not the quiescence tail, which is a separate, per-design-unbounded-by-depth cost.
            if (depth <= 0)
                return Quiescence(board, alpha, beta, perspectiveTeam, plyFromRoot, MaxQuiescencePly, ct);

            int alphaOriginal = alpha;
            uint ttMove = 0;

            if (_tt.Probe(board.ZobristHash, out int ttScore, out uint ttPackedMove, out int ttDepth, out TTFlag ttFlag))
            {
                // Always harvested for ordering, even on a depth-insufficient hit.
                ttMove = ttPackedMove;

                if (ttDepth >= depth)
                {
                    int s = UnadjustMateScore(ttScore, plyFromRoot);
                    if (ttFlag == TTFlag.Exact) return s;
                    if (ttFlag == TTFlag.LowerBound && s > alpha) alpha = s;
                    if (ttFlag == TTFlag.UpperBound && s < beta) beta = s;
                    if (alpha >= beta) return s;
                }
            }

            // Internal iterative reduction: a node this deep with no TT move at all has never been
            // searched before, so move ordering here is flying blind (no killer/history head start
            // from a prior visit). Rather than order blindly at full cost, do a cheap shallower probe
            // first — its own recursive TT stores will very likely populate an entry for THIS
            // position, handing the real search below a ttMove to order from. Same reasoning as the
            // engine's existing null-move probe: spend a smaller search to buy a better-ordered
            // bigger one. No Betrayal-specific guard needed — this only changes how deep we look
            // before committing to the real search at this exact node, not what moves are legal or
            // how the turn/hash bookkeeping behaves. iirAllowed: false on the probe call itself so a
            // TT-cold line can't cascade into a chain of nested reductions all the way down to the
            // depth floor — this fires at most once per real node, exactly like a single extra ply
            // of lookahead, not a repeating discount applied at every level of the probe.
            if (iirAllowed && ttMove == 0 && depth >= InternalIterativeReductionMinDepth)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _tt.Stats.IirReductions++;
#endif
                Search(board, depth - InternalIterativeReductionAmount, plyFromRoot, alpha, beta, perspectiveTeam, ct, parentWasNull, extensionsUsedThisLine, iirAllowed: false);
                if (_tt.Probe(board.ZobristHash, out _, out uint iirPackedMove, out _, out _))
                    ttMove = iirPackedMove;
            }

            // Shared guard for the whole forward-pruning family (NMP, reverse futility, move-count
            // pruning, frontier futility): every one of them substitutes a cheap static judgment for
            // real search, which is only sound when this node is an ordinary, non-forcing position.
            // PendingBetrayerSquare covers Act-pending, Retribution-pending, AND ForcedSave-pending
            // alike — a pruned-away node mid-sequence would corrupt move generation and the turn/hash
            // bookkeeping exactly like a null move would. Being in check means every legal move is
            // already forced and narrow, so there is nothing "extra" left to prune away safely.
            bool forwardPruningAllowed = !board.PendingBetrayerSquare.HasValue
                && !_engine.IsKingInCheck(board, board.CurrentTurn);

            // Reverse futility pruning (static null-move pruning): if the static eval already beats
            // beta by more than this depth's margin, the opponent would need to find an improvement
            // search says is implausible at this shallow a depth just to bring the score back down to
            // beta — so we trust the static judgment and cut immediately instead of searching.
            if (forwardPruningAllowed
                && depth <= ReverseFutilityMaxDepth
                && beta < MateScore - _maxSupportedDepth)
            {
                int staticEval = _evaluator.Evaluate(board, perspectiveTeam);
                if (staticEval - ReverseFutilityMargin(depth) >= beta)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.ReverseFutilityCutoffs++;
#endif
                    return staticEval;
                }
            }

            // Null Move Pruning — sits after the TT probe, before movegen; the full guard set below
            // must pass before we ever touch the board. PendingBetrayerSquare covers Act-pending,
            // Retribution-pending, AND ForcedSave-pending alike, not just Retribution — a null move
            // mid-sequence would flip CurrentTurn while the domain mandates the SAME player
            // continues, corrupting move generation, the TT hash, and the alpha-beta frame at once.
            if (depth >= NullMoveMinDepth(depth)
                && forwardPruningAllowed
                && !parentWasNull
                && HasNonPawnMaterial(board, board.CurrentTurn)
                && beta < MateScore - _maxSupportedDepth)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _tt.Stats.NullMoveAttempts++;
#endif
                MakeNullMove(board, out int? savedEnPassantFile);
                Team opponent = perspectiveTeam == Team.White ? Team.Black : Team.White;
                int nullScore = -Search(board, depth - 1 - NullMoveReduction(depth), plyFromRoot + 1, -beta, -beta + 1, opponent, ct, parentWasNull: true);
                UndoNullMove(board, savedEnPassantFile);

                if (nullScore >= beta)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.NullMoveCutoffs++;
#endif
                    return beta; // fail-hard cutoff
                }
            }

            List<MoveCommand> moves = MoveBuffer(plyFromRoot);
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

            OrderMoves(board, moves, ttMove, plyFromRoot);

            // LMR eligibility for THIS node is fixed once, before the loop: a pending Betrayer means
            // every child here is part of a forced tactical sequence (same reasoning as the null-move
            // guard above), so nothing at this node may ever be reduced.
            bool nodeAllowsReduction = depth >= 3 && !board.PendingBetrayerSquare.HasValue;

            // Move-count pruning and frontier futility are both members of the forward-pruning
            // family gated by the same shared guard as NMP/reverse-futility above. A node with only
            // one legal move can never actually skip it (best must come from somewhere), so both are
            // additionally gated on moves.Count > 1 here rather than relying on the loop's own
            // "at least one move survives" behavior to hold by accident.
            bool nodeAllowsLateMovePruning = forwardPruningAllowed && depth <= LateMovePruningMaxDepth && moves.Count > 1;
            bool nodeAllowsFrontierFutility = forwardPruningAllowed && depth <= FrontierFutilityMaxDepth && moves.Count > 1;

            // Computed lazily — only the first time either pruning member actually needs it — since
            // most nodes never reach this depth band and the eval call is not free.
            bool staticEvalComputed = false;
            int staticEvalForPruning = 0;
            int quietMovesSearched = 0;

            int best = -Infinity;
            uint bestPackedMove = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                if (ct.IsCancellationRequested) return best;

                MoveCommand move = moves[i];
                bool isQuiet = IsReducibleMove(move, ttMove);

                // Move-count pruning: once enough quiet moves have already been tried at this node
                // without improving alpha, later quiet moves in the (cutoff-ordered) list are so
                // unlikely to be the best that we skip them without searching at all. i > 0 keeps the
                // PV/first move always searched regardless of the counter.
                if (nodeAllowsLateMovePruning && isQuiet && i > 0 && quietMovesSearched >= LateMovePruningThreshold(depth))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.LateMovePrunes++;
#endif
                    continue;
                }

                // Frontier futility pruning: a quiet move whose best-case static outlook still can't
                // reach alpha is skipped, UNLESS it leaves the opponent in check — a forcing move's
                // true value can't be judged from a static eval, exactly like quiescence's own
                // never-stand-pat-mid-sequence rule for a pending Betrayer.
                if (nodeAllowsFrontierFutility && isQuiet && i > 0)
                {
                    if (!staticEvalComputed)
                    {
                        staticEvalForPruning = _evaluator.Evaluate(board, perspectiveTeam);
                        staticEvalComputed = true;
                    }

                    if (staticEvalForPruning + FrontierFutilityMargin(depth) <= alpha)
                    {
                        ApplyMoveAndTurn(board, move);
                        bool givesCheck = _engine.IsKingInCheck(board, board.CurrentTurn);
                        UndoMoveAndTurn(board, move);

                        if (!givesCheck)
                        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            _tt.Stats.FrontierFutilityPrunes++;
#endif
                            continue;
                        }
                    }
                }

                if (isQuiet) quietMovesSearched++;

                bool reduce = nodeAllowsReduction && i >= 2 && isQuiet;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (reduce) _tt.Stats.LmrReductions++;
#endif

                int searchDepth = reduce ? depth - 2 : depth - 1;

                ApplyMoveAndTurn(board, move);

                // Betrayal/Retribution extension: only ever granted for the move that JUST staged
                // the forced sequence (an Act that left a Retribution pending), never for a move
                // played WHILE already inside one — Retribution/DefensiveOverride/Defection moves
                // already get their ply "for free" via the non-flipping ScoreChild path (see its
                // own doc comment), so extending them again on top of that would double-count the
                // same forced sequence. board.PendingBetrayerSquare is read AFTER ApplyMoveAndTurn,
                // so it reflects the position this child is actually about to search, not the
                // parent's.
                // depth < _maxSupportedDepth guards the extension the same way _moveBuffers/
                // _killerMoves are themselves sized to maxSupportedDepth + 1 — without it, a chain
                // of extensions near the ceiling could push searchDepth's recursive Search call past
                // the last valid _moveBuffers index and index out of bounds.
                bool grantExtension = move.Stage == BetrayalStage.Act
                    && board.PendingBetrayerSquare.HasValue
                    && extensionsUsedThisLine < MaxBetrayalExtensionsPerLine
                    && depth < _maxSupportedDepth;
                int childExtensionsUsed = grantExtension ? extensionsUsedThisLine + 1 : extensionsUsedThisLine;
                if (grantExtension)
                {
                    searchDepth++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.BetrayalExtensions++;
#endif
                }

                int score;
                if (i == 0)
                {
                    // PV move (first child, typically the TT/ordering pick): full window. Its score
                    // sets the working alpha every later sibling is scouted against.
                    score = ScoreChild(board, move, searchDepth, plyFromRoot + 1, alpha, beta, perspectiveTeam, ct, childExtensionsUsed);
                }
                else
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _tt.Stats.PvsScouts++;
#endif
                    // Null-window scout — ScoreChild passes (alpha, alpha+1) straight through for a
                    // non-flipping Act/Defection child (same maximizer frame) and negates it to
                    // (-alpha-1, -alpha) for a flipping child. Proves "can this beat alpha?" cheaply;
                    // a fail-low here is a real cutoff regardless of whether depth was reduced.
                    score = ScoreChild(board, move, searchDepth, plyFromRoot + 1, alpha, alpha + 1, perspectiveTeam, ct, childExtensionsUsed);

                    // LMR fail-high — the reduction may have hidden real strength. Re-search at full
                    // depth, STILL null-window, before deciding whether a full-window re-search is
                    // even warranted. reduce is only ever true for a quiet move (see IsReducibleMove),
                    // and grantExtension is only ever true for an Act, so the two never coincide —
                    // "full depth" here is always plain depth - 1, never the extended searchDepth.
                    if (reduce && score > alpha)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        _tt.Stats.LmrReSearches++;
#endif
                        score = ScoreChild(board, move, depth - 1, plyFromRoot + 1, alpha, alpha + 1, perspectiveTeam, ct, childExtensionsUsed);
                    }

                    // PVS fail-high — the null-window scout can only prove "not worse than alpha",
                    // not the true score. Only a genuine alpha<score<beta result needs the full-window
                    // re-search; a score >= beta is already a valid cutoff via the null window alone.
                    // Full, un-reduced depth — reduce and grantExtension are mutually exclusive (see
                    // above), so this is depth - 1 for a reduced quiet move and depth (the extended
                    // searchDepth) for an Act that staged a Retribution; either way it's the same
                    // depth the LMR fail-high branch above would have used had it fired instead.
                    if (score > alpha && score < beta)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        _tt.Stats.PvsReSearches++;
#endif
                        int fullDepth = grantExtension ? depth : depth - 1;
                        score = ScoreChild(board, move, fullDepth, plyFromRoot + 1, alpha, beta, perspectiveTeam, ct, childExtensionsUsed);
                    }
                }

                UndoMoveAndTurn(board, move);

                if (score > best)
                {
                    best = score;
                    bestPackedMove = PackMove(move);
                }
                if (best > alpha) alpha = best;

                // Beta cutoff. Valid across the Retribution sub-phase precisely because the
                // non-flipping plies don't swap the alpha/beta frame — so a cutoff proven inside
                // a betrayal sequence is still a cutoff for the same maximizer that owns this node.
                if (alpha >= beta)
                {
                    RecordQuietCutoff(move, depth, plyFromRoot);
                    break;
                }
            }

            // Never store a cancelled node — its 'best' is a partial, garbage result, not a proof.
            if (!ct.IsCancellationRequested)
            {
                TTFlag flag = best <= alphaOriginal ? TTFlag.UpperBound
                            : best >= beta ? TTFlag.LowerBound
                            : TTFlag.Exact;
                _tt.Store(board.ZobristHash, AdjustMateScore(best, plyFromRoot), bestPackedMove, depth, flag);
            }

            return best;
        }

        /// <summary>
        /// Mate scores are stored ply-from-THIS-node, so a hit at a different plyFromRoot still
        /// reports the correct distance-to-mate from the ROOT. Store subtracts plyFromRoot (moving
        /// the mate closer to root, i.e. a larger magnitude away from the horizon); probe adds it
        /// back. Non-mate scores are unaffected in practice (magnitude stays well under the mate band).
        /// </summary>
        private static int AdjustMateScore(int score, int plyFromRoot)
        {
            if (score >= MateScore - 1000) return score + plyFromRoot;
            if (score <= -(MateScore - 1000)) return score - plyFromRoot;
            return score;
        }

        private static int UnadjustMateScore(int score, int plyFromRoot)
        {
            if (score >= MateScore - 1000) return score - plyFromRoot;
            if (score <= -(MateScore - 1000)) return score + plyFromRoot;
            return score;
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
        /// "I pass; opponent moves" — no seam exists on IChessEngine for this (a null move isn't a
        /// real move), so the search does it directly: pass the turn, toggle the turn-hash, and
        /// clear any en-passant right (EP doesn't survive a passed turn — the file must be un-hashed
        /// here and restored by UndoNullMove, mirroring ApplyZobristMove's own EP toggle pattern).
        /// </summary>
        internal void MakeNullMove(BoardState board, out int? savedEnPassantFile)
        {
            savedEnPassantFile = board.EnPassantFile;
            if (savedEnPassantFile.HasValue)
            {
                board.ToggleEnPassantHash(savedEnPassantFile.Value);
                board.EnPassantFile = null;
            }

            board.ToggleTurnHash();
            board.NextTurn();
        }

        /// <summary>Mirror of <see cref="MakeNullMove"/> — restores EnPassantFile and its hash
        /// contribution, then reverses the turn pass, in the exact opposite order.</summary>
        internal void UndoNullMove(BoardState board, int? savedEnPassantFile)
        {
            board.NextTurn();
            board.ToggleTurnHash();

            if (savedEnPassantFile.HasValue)
            {
                board.EnPassantFile = savedEnPassantFile;
                board.ToggleEnPassantHash(savedEnPassantFile.Value);
            }
        }

        /// <summary>
        /// Zugzwang heuristic (NMP guard 5): side to move needs at least one non-pawn, non-king
        /// piece, else null-move pruning is unsound (a pass can look safe purely because the side
        /// has nothing but pawn/king moves to make, not because the position is actually quiet).
        /// No material-count query exists on IBoardQuery, so this is a cheap piece-index scan —
        /// bounded by a team's piece count (<=16), not the board, so it's search-loop-cheap.
        /// </summary>
        private static bool HasNonPawnMaterial(BoardState board, Team team)
        {
            List<int> indices = board.GetPieceIndices(team);
            for (int i = 0; i < indices.Count; i++)
            {
                int square = indices[i];
                ChessPieceType type = board.GetPiece(square % board.TileCountX, square / board.TileCountX).Type;
                if (type != ChessPieceType.Pawn && type != ChessPieceType.King)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The heart of Betrayal-correct search: decide whether this child flips the turn.
        ///   - Turn FLIPPED (normal move, Retribution, DefensiveOverride): standard negamax —
        ///     negate the child and swap/negate the alpha-beta window.
        ///   - Turn DID NOT FLIP (Act, Defection): SAME player moves again. Recurse WITHOUT
        ///     negation and WITHOUT swapping the window. perspectiveTeam is unchanged.
        /// This is the exact mirror of Core's ApplyZobristMove turn-hash rule.
        /// </summary>
        private int ScoreChild(BoardState board, MoveCommand move, int childDepth, int plyFromRoot,
                               int alpha, int beta, Team perspectiveTeam, CancellationToken ct, int extensionsUsedThisLine = 0)
        {
            if (StageFlipsTurn(move.Stage))
            {
                // Standard negamax step: opponent to move, minimize from our view => negate.
                Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                return -Search(board, childDepth, plyFromRoot, -beta, -alpha, childPerspective, ct, extensionsUsedThisLine: extensionsUsedThisLine);
            }
            else
            {
                // Same player continues (mid-Betrayal). No negation, no window swap, same frame.
                // NOTE: we do NOT decrement depth differently here — an Act followed by a forced
                // Retribution is two plies of one turn; letting each consume a ply keeps the depth
                // budget honest and prevents infinite non-flipping recursion. The extension above
                // is what compensates for that cost on the Act itself; this branch's depth handling
                // is unchanged by it.
                return Search(board, childDepth, plyFromRoot, alpha, beta, perspectiveTeam, ct, extensionsUsedThisLine: extensionsUsedThisLine);
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
        ///
        /// <paramref name="plyFromRoot"/> threads the same root-distance Search already tracks into
        /// quiescence, purely so a TT store from inside qsearch can mate-adjust
        /// exactly like Search's does (see AdjustMateScore/UnadjustMateScore) — ResolveForcedDefection
        /// can return a raw +/-MateScore from inside quiescence (a forced-mate-in-the-sequence), and
        /// storing that un-adjusted would corrupt mate-distance reporting for any future probe at a
        /// different plyFromRoot. It increments by exactly the same amount Search's plyFromRoot would
        /// have, on every recursive step (flipping or not), keeping the two counters in lockstep.
        /// </summary>
        private int Quiescence(BoardState board, int alpha, int beta, Team perspectiveTeam, int plyFromRoot, int qply, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return 0;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _tt.Stats.QNodesVisited++;
#endif

            // --- Betrayal quiescence guard ---
            // A pending Betrayer can leave the board in one of two states an ordinary quiescence node
            // must never treat as "quiet": still on the initiator's team (Retribution owed) or already
            // flipped by a prior Defection that left the initiator's own king in check (ForcedSave
            // owed — see ChessEngine.ResolveDefection's doc comment for why the pending fields stay
            // set across that transition). Both must resolve before we're allowed to stand pat.
            bool pendingBetrayer = board.PendingBetrayerSquare.HasValue && board.BetrayalInitiator.HasValue;
            bool retributionPending = pendingBetrayer &&
                board.GetPiece(board.PendingBetrayerSquare.Value).Team == board.BetrayalInitiator.Value;
            bool forcedSavePending = pendingBetrayer && !retributionPending;

            if (retributionPending)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _tt.Stats.QBetrayalResolutionNodes++;
#endif
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
                    return ResolveForcedDefection(board, alpha, beta, perspectiveTeam, plyFromRoot, qply, ct);

                OrderMoves(board, retribution);
                int bestRetribution = -Infinity;
                for (int i = 0; i < retribution.Count; i++)
                {
                    MoveCommand move = retribution[i];
                    ApplyMoveAndTurn(board, move);
                    // Retribution flips the turn, so negate.
                    Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                    int score = -Quiescence(board, -beta, -alpha, childPerspective, plyFromRoot + 1, qply - 1, ct);
                    UndoMoveAndTurn(board, move);
                    if (score > bestRetribution) bestRetribution = score;
                    if (bestRetribution > alpha) alpha = bestRetribution;
                    if (alpha >= beta) break;
                }
                return bestRetribution;
            }

            if (forcedSavePending)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _tt.Stats.QBetrayalResolutionNodes++;
#endif
                if (qply <= 0)
                    return _evaluator.Evaluate(board, perspectiveTeam);

                // The board already arrived here mid-ForcedSave (a prior ply's Defection left this
                // exact obligation open) — resolve it the same way ResolveForcedDefection's own
                // ForcedSave branch does when it discovers the obligation fresh.
                return PlayForcedSaveMoves(board, alpha, beta, perspectiveTeam, plyFromRoot, qply, ct);
            }

            // --- Standard quiescence ---
            // Qsearch TT probe. Reuses the SAME table Search uses, storing at depth 0 — the existing
            // depth-preferred replacement rule (TranspositionTable.Store) means a depth-0 entry can
            // never evict a Search entry of depth >= 1, so there is no second table and no pollution
            // risk beyond what depth-0-vs-depth-0 entries already tolerate.
            int alphaOriginal = alpha;
            if (_tt.Probe(board.ZobristHash, out int ttScore, out _, out _, out TTFlag ttFlag))
            {
                int s = UnadjustMateScore(ttScore, plyFromRoot);
                if (ttFlag == TTFlag.Exact) return s;
                if (ttFlag == TTFlag.LowerBound && s > alpha) alpha = s;
                if (ttFlag == TTFlag.UpperBound && s < beta) beta = s;
                if (alpha >= beta) return s;
            }

            // Quiescence's cutoff return is fail-soft (fall through, return the tightest 'best'
            // found) rather than fail-hard (return beta immediately), so there is exactly one
            // TT-store site after the loop, mirroring Search's own pattern — duplicating the store
            // at every early-return site would be needless surface area. Fail-soft is the more
            // standard alpha-beta convention (Search itself already returns 'best', not 'beta') and
            // is provably still a valid cutoff (best >= beta here).
            int standPat = _evaluator.Evaluate(board, perspectiveTeam);
            if (standPat >= beta) return standPat;
            if (standPat > alpha) alpha = standPat;

            if (qply <= 0) return alpha; // out of budget: stand pat on the quiet-ish position.

            // Generate captures/promotions/Acts directly rather than the full legal move list —
            // quiescence only ever wants this subset, and full movegen was spending most of its
            // cost legality-checking quiet moves that would just get filtered out below anyway.
            List<MoveCommand> moves = QuiescenceBuffer(qply);
            _engine.GetCapturesAndActsOnly(board, board.CurrentTurn, moves);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _tt.Stats.QMovesGenerated += moves.Count;
#endif
            OrderMoves(board, moves);

            int best = standPat;
            for (int i = 0; i < moves.Count; i++)
            {
                MoveCommand move = moves[i];

                // Bound Act re-expansion to the FIRST quiescence ply only (the horizon's immediate
                // next ply, qply == MaxQuiescencePly). Standing pat mid-sequence stays forbidden
                // everywhere (untouched, sacred — see the betrayerPending branch above); this gate
                // only decides whether quiescence may INITIATE a new Act. Beyond the first ply, an
                // Act line is skipped rather than explored, since an unbounded Act re-fan out of
                // every quiescence node was the single largest driver of qtree size. The horizon
                // still sees one ply of imminent Betrayal threat.
                if (move.Stage == BetrayalStage.Act && qply != MaxQuiescencePly) continue;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _tt.Stats.QMovesSearched++;
                if (move.Stage == BetrayalStage.Act) _tt.Stats.QActExpansions++;
#endif

                // Delta pruning: even winning this capture outright can't raise alpha, so it's not
                // worth exploring. Margin covers promotion potential + evaluator noise so a genuine
                // improving line is never mis-pruned. Act is exempt — it isn't a material capture,
                // and the Betrayal-critical rule above requires we never skip resolving a pending
                // sequence, so pruning Act here would risk standing pat mid-Retribution.
                if (move.IsCapture && move.Stage != BetrayalStage.Act)
                {
                    int optimisticGain = CapturedPieceValue(move.CapturedType) + DeltaPruningMargin;
                    if (standPat + optimisticGain <= alpha) continue;

                    // Static-exchange prune: delta pruning above only catches a capture whose best
                    // case can't help even if it wins outright — this catches the opposite case, a
                    // capture that LOOKS like material gain (it passed delta pruning) but actually
                    // loses material once every recapture on the square is accounted for. Only
                    // trusted where StaticExchangeEvaluation.IsApplicable holds; on a square where a
                    // Betrayal Act just staged a pending Retribution, the exchange isn't a normal
                    // alternating trade, so this prune stays off there exactly like ordering's own
                    // SEE nudge does.
                    if (StaticExchangeEvaluation.IsApplicable(board, move)
                        && StaticExchangeEvaluation.Evaluate(board, move) < 0)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        _tt.Stats.SeeQuiescencePrunes++;
#endif
                        continue;
                    }
                }

                ApplyMoveAndTurn(board, move);
                // Recurse within quiescence (not back into Search) so the qply budget carries through
                // capture/Act chains — an Act keeps the same side to move and leaves a Betrayer
                // pending, so the same-perspective recurse re-enters the guard above; a capture flips
                // the turn and negates like a normal ply. Mirrors ScoreChild's flip rule.
                int score;
                if (StageFlipsTurn(move.Stage))
                {
                    Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                    score = -Quiescence(board, -beta, -alpha, childPerspective, plyFromRoot + 1, qply - 1, ct);
                }
                else
                {
                    score = Quiescence(board, alpha, beta, perspectiveTeam, plyFromRoot + 1, qply - 1, ct);
                }
                UndoMoveAndTurn(board, move);

                if (score > best) best = score;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }

            // Qsearch TT store — single store site after the loop, mirroring Search's own pattern
            // exactly. Always at depth 0, packedMove 0 (qsearch doesn't do the ordering-hint
            // harvesting Search's TT-move does). No cancellation guard needed here the way Search
            // has one: Quiescence's only cancellation check is the top-of-function early-out, so
            // reaching this line means the node completed normally.
            TTFlag flag = best <= alphaOriginal ? TTFlag.UpperBound
                        : best >= beta ? TTFlag.LowerBound
                        : TTFlag.Exact;
            _tt.Store(board.ZobristHash, AdjustMateScore(best, plyFromRoot), packedMove: 0, depth: 0, flag);
            return best;
        }

        /// <summary>
        /// Test seam: run a full quiescence evaluation from <paramref name="board"/> for
        /// <paramref name="perspectiveTeam"/>, using the standard open window and ply budget. Lets
        /// SearchCorrectnessTests drive the Betrayal forced-Defection/ForcedSave branches directly on
        /// a crafted pending-Betrayer position, rather than having to engineer a full search tree that
        /// happens to hit the horizon exactly on that state. Not part of the search's own control flow.
        /// </summary>
        internal int RunQuiescenceForTest(BoardState board, Team perspectiveTeam, CancellationToken ct) =>
            Quiescence(board, -Infinity, Infinity, perspectiveTeam, plyFromRoot: 0, MaxQuiescencePly, ct);

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
                                           Team perspectiveTeam, int plyFromRoot, int qply, CancellationToken ct)
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
                score = PlayForcedSaveMoves(board, alpha, beta, perspectiveTeam, plyFromRoot, qply, ct);
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
                score = -Quiescence(board, -beta, -alpha, childPerspective, plyFromRoot + 1, qply - 1, ct);

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
        /// Plays out the mandatory king-save obligation on a board that's already ForcedSave-pending
        /// — whether that obligation was just discovered this instant (ResolveForcedDefection, right
        /// after the Defection that created it) or the board arrived here already carrying it from a
        /// prior ply (Quiescence's own top-level ForcedSavePending branch). Same side to move, no
        /// turn flip on the way in; a chosen DefensiveOverride move (or the absence of any legal one)
        /// is what finally resolves the sequence.
        /// </summary>
        private int PlayForcedSaveMoves(BoardState board, int alpha, int beta,
                                        Team perspectiveTeam, int plyFromRoot, int qply, CancellationToken ct)
        {
            List<MoveCommand> saves = QuiescenceBuffer(qply);
            _engine.GetForcedSaveMoves(board, board.CurrentTurn, saves);

            if (saves.Count == 0)
            {
                // No legal king-save after the self-check: this side is mated inside the Betrayal
                // sequence. Score it as a mate against the side to move, in perspective.
                return board.CurrentTurn == perspectiveTeam ? -MateScore : MateScore;
            }

            OrderMoves(board, saves);
            int best = -Infinity;
            for (int i = 0; i < saves.Count; i++)
            {
                MoveCommand save = saves[i];
                ApplyMoveAndTurn(board, save);
                // DefensiveOverride flips the turn, so negate and swap the window.
                Team childPerspective = perspectiveTeam == Team.White ? Team.Black : Team.White;
                int childScore = -Quiescence(board, -beta, -alpha, childPerspective, plyFromRoot + 1, qply - 1, ct);
                UndoMoveAndTurn(board, save);
                if (childScore > best) best = childScore;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }
            return best;
        }

        /// <summary>
        /// MVV-LVA-ish ordering + Act-first. Good ordering is what makes alpha-beta cut ~b^(d/2)
        /// instead of b^d. Betrayal Act moves are searched early because they're high-variance and
        /// most likely to cause cutoffs (or refutations) — we want them near the front. No
        /// plyFromRoot to key killers against at this call site (quiescence has no ply-indexed
        /// killer table of its own), so quiet moves fall back to history only.
        /// </summary>
        private void OrderMoves(BoardState board, List<MoveCommand> moves) => OrderMoves(board, moves, 0, plyFromRoot: -1);

        /// <summary>
        /// MVV-LVA-ish ordering + Act-first, with the TT move (if any) sorted to the very front.
        /// Good ordering is what makes alpha-beta cut ~b^(d/2) instead of b^d — a TT-move hit from a
        /// prior iteration/turn is the single best predictor of the true best move at a node.
        /// plyFromRoot: -1 opts out of the killer lookup (used by quiescence, which has no
        /// ply-indexed killer table of its own).
        ///
        /// A capture's exact tier position within the winning/losing capture bands comes from a
        /// real static-exchange read (see EnsureSeeScoreCache/SeeOrderBonus below) whenever
        /// StaticExchangeEvaluation.IsApplicable allows it — the coarse piece-rank difference
        /// (MVV-LVA) that used to be the whole story is now only the fallback for a Betrayal-pending
        /// square, where SEE's alternating-recapture assumption doesn't hold.
        /// </summary>
        private void OrderMoves(BoardState board, List<MoveCommand> moves, uint ttMove, int plyFromRoot)
        {
            EnsureSeeScoreCache(moves.Count);
            for (int i = 0; i < moves.Count; i++)
                _seeScoreCache[i] = ComputeSeeOrderBonus(board, moves[i]);

            // Simple insertion-sort by a cheap score key; move lists are small (<~45), so this is
            // faster than allocating a comparer/delegate and avoids GC. The SEE cache is index-
            // aligned to the ORIGINAL move order and must move in lockstep with each swap below —
            // it's the same "score sits in a parallel array, shuffled together with its move"
            // discipline MoveToFront already uses for _rootScores/_rootMoves.
            for (int i = 1; i < moves.Count; i++)
            {
                MoveCommand key = moves[i];
                int keySee = _seeScoreCache[i];
                int keyScore = OrderScore(key, ttMove, plyFromRoot, keySee);
                int j = i - 1;
                while (j >= 0 && OrderScore(moves[j], ttMove, plyFromRoot, _seeScoreCache[j]) < keyScore)
                {
                    moves[j + 1] = moves[j];
                    _seeScoreCache[j + 1] = _seeScoreCache[j];
                    j--;
                }
                moves[j + 1] = key;
                _seeScoreCache[j + 1] = keySee;
            }
        }

        /// <summary>Grows the SEE ordering cache to fit a pathological move-list size, mirroring
        /// EnsureRootScoreCapacity's own "grown lazily, never freed" policy.</summary>
        private void EnsureSeeScoreCache(int requiredCount)
        {
            if (_seeScoreCache.Length >= requiredCount) return;
            Array.Resize(ref _seeScoreCache, requiredCount * 2);
        }

        // SEE's raw centipawn output (up to the low tens of thousands for a king-adjacent trade)
        // is far larger than the gap between OrderScoreCore's tier bands — adding it in unscaled
        // would risk a deeply-losing SEE capture outscoring a promo or even a quiet move's tier,
        // which is not what this lever is for: SEE is only supposed to refine ordering WITHIN
        // whichever tier MVV-LVA already placed the capture in, not override the tier system
        // itself. Clamping to this range keeps the nudge strictly smaller than the smallest gap
        // between two adjacent tiers (Tier 2's equal-capture/promo band down to Tier 3's Act band
        // is a 9,500-point gap; ±500 leaves comfortable headroom on both sides).
        private const int SeeOrderBonusClamp = 500;

        /// <summary>
        /// A small ordering nudge derived from a real static-exchange read, computed exactly once
        /// per move per OrderMoves call (see the cache above) rather than per comparison. Zero for
        /// every non-capture, and zero whenever StaticExchangeEvaluation.IsApplicable says the
        /// alternating-recapture assumption doesn't hold here (a Betrayal-pending square) — those
        /// moves fall back to OrderScoreCore's plain MVV-LVA rank-difference entirely, exactly as
        /// before this lever existed. Clamped (see SeeOrderBonusClamp) so it can only reorder
        /// moves within their existing MVV-LVA tier, never move a capture into a different one.
        /// </summary>
        private static int ComputeSeeOrderBonus(BoardState board, MoveCommand m)
        {
            if (!m.IsCapture || !StaticExchangeEvaluation.IsApplicable(board, m)) return 0;

            int see = StaticExchangeEvaluation.Evaluate(board, m);
            if (see > SeeOrderBonusClamp) return SeeOrderBonusClamp;
            if (see < -SeeOrderBonusClamp) return -SeeOrderBonusClamp;
            return see;
        }

        /// <summary>
        /// LMR exemption predicate, keyed on move SEMANTICS rather than sort
        /// position: a capture, a promotion, the TT/PV move, or any Betrayal-stage move (Act,
        /// Retribution, DefensiveOverride, Defection) is never reduced. The per-node depth/pending-
        /// Betrayer/index gates live in Search's loop, since those need node-level state this
        /// predicate doesn't have.
        /// </summary>
        internal static bool IsReducibleMove(MoveCommand m, uint ttMove) =>
            !m.IsCapture && !m.IsPromotion && m.Stage == BetrayalStage.None && PackMove(m) != ttMove;

        /// <summary>
        /// Concrete tier bands, ignoring killer/history (see the 3-arg overload below for the
        /// version Search actually orders with). Kept as its own static entry point since
        /// MoveOrderingTierTests pins the tier bands in isolation, independent of any particular
        /// search's accumulated killer/history state.
        /// </summary>
        internal static int OrderScore(MoveCommand m, uint ttMove) => OrderScoreCore(m, ttMove, out _);

        /// <summary>
        /// Concrete tier bands, plus a real static-exchange nudge on captures where one is
        /// available. Ordering only — never changes which move wins the node, only how fast
        /// alpha-beta gets there. Act is Tier 3 (below captures/promo, above quiets): demoted from
        /// the old flat +5000 so a cheaper cutoff gets a chance to fire before the search pays
        /// Retribution's branching cost by exploring an Act first.
        ///
        /// seeBonus is added on top of the coarse MVV-LVA tier score for a capture, refining the
        /// ordering WITHIN the winning/losing-capture bands with an actual exchange result instead
        /// of just the piece-rank difference — e.g. two different winning captures that MVV-LVA
        /// would rank identically can still be told apart by how much material each one really
        /// nets once recaptures are accounted for. It never moves a capture OUT of its MVV-LVA tier
        /// (a small nudge added to a Tier-1 score still sorts above every Tier-2 score, since the
        /// tier gap is always far larger than any single exchange's plausible centipawn swing).
        ///
        /// The quiet band (Tier 4, previously a flat 0) now carries a killer/history sub-score so
        /// quiet moves that have caused cutoffs before get tried before ones that never have —
        /// still strictly below every capture/promo/Act tier above it, so this only reorders WITHIN
        /// the quiet band, never promotes a quiet move ahead of a tactical one.
        /// </summary>
        private int OrderScore(MoveCommand m, uint ttMove, int plyFromRoot, int seeBonus)
        {
            int tierScore = OrderScoreCore(m, ttMove, out bool isQuiet);
            if (isQuiet) return QuietMoveOrderScore(m, plyFromRoot);

            // Only the winning/equal-capture bands (Tier 1/2, both >= 20,000) get the SEE nudge —
            // a losing capture's tier sits too close to the Act/quiet bands below it (as little as
            // a few points of headroom) for even a small nudge to stay safely inside it, so those
            // keep the plain MVV-LVA rank-difference score exactly as before this lever existed.
            return tierScore >= 20_000 ? tierScore + seeBonus : tierScore;
        }

        /// <summary>Shared tier-band logic for both OrderScore overloads. isQuiet reports whether
        /// the move fell all the way through to the quiet band, so the ply-aware caller knows to
        /// layer a killer/history sub-score on top instead of using the (always 0) tier value.</summary>
        private static int OrderScoreCore(MoveCommand m, uint ttMove, out bool isQuiet)
        {
            isQuiet = false;
            if (ttMove != 0 && PackMove(m) == ttMove) return 100_000;  // Tier 0: TT/PV move

            int capturedRank = PieceRank(m.CapturedType);
            int pieceRank = PieceRank(m.PieceType);

            if (m.IsCapture && capturedRank > pieceRank)
                return 30_000 + capturedRank * 10 - pieceRank;         // Tier 1: winning capture

            if (m.IsPromotion)
                return 20_500;                                        // Tier 2: promo (>= equal capture)

            if (m.IsCapture && capturedRank == pieceRank)
                return 20_000;                                        // Tier 2: equal capture

            if (m.Stage == BetrayalStage.Act)
                return 10_000;                                        // Tier 3: explore betrayals

            if (m.IsCapture)
                return capturedRank * 10 - pieceRank;                  // Tier 4: losing capture

            isQuiet = true;
            return 0;                                                  // Tier 4: quiet
        }

        /// <summary>
        /// Killer bonus (this ply's remembered cutoff moves) plus history bonus (this piece/square
        /// pair's accumulated cutoff weight). Both are clamped so their sum stays well under the
        /// Act tier band above — a losing capture already scores as low as 1 in the existing tier
        /// bands, so this doesn't try to outrank captures in general, only the tier immediately
        /// above the shared quiet/losing-capture region. Only meaningful for Stage == None moves —
        /// history is never recorded for a Betrayal-stage move (see the update site in Search), so
        /// this always resolves to 0 for one and the killer check below simply won't match either.
        /// </summary>
        private int QuietMoveOrderScore(MoveCommand m, int plyFromRoot)
        {
            uint packed = PackMove(m);
            int killerBonus = 0;
            if (plyFromRoot >= 0 && plyFromRoot < _killerMoves.GetLength(0))
            {
                if (packed == _killerMoves[plyFromRoot, 0]) killerBonus = 9000;
                else if (packed == _killerMoves[plyFromRoot, 1]) killerBonus = 8000;
            }

            // Already clamped to 999 at the write site (RecordQuietCutoff) — combined with
            // killerBonus's 9000 ceiling, the sum can never approach the Act tier at 10_000.
            int historyBonus = m.Stage == BetrayalStage.None
                ? _historyScores[(int)m.PieceType, SquareIndex(m.EndPosition)]
                : 0;

            return killerBonus + historyBonus;
        }

        private static int SquareIndex(Vector2Int square) => square.y * 8 + square.x;

        /// <summary>
        /// Called once per beta cutoff, right where Search's move loop breaks. Only a QUIET move
        /// (not a capture, not a promotion, not any Betrayal-stage move) updates history/killers —
        /// a capture or Act already sorts ahead of the whole quiet band via OrderScore's tier
        /// bands, so remembering it here would never change its own ordering and would only risk
        /// crowding out genuinely useful quiet-move memory.
        /// </summary>
        /// <summary>Test seam: exercises the exact same update RecordQuietCutoff performs from
        /// inside Search's move loop, without needing to engineer a full search tree that happens
        /// to produce a cutoff at a specific ply. Not part of the search's own control flow.</summary>
        internal void RecordQuietCutoffForTest(MoveCommand move, int depth, int plyFromRoot) =>
            RecordQuietCutoff(move, depth, plyFromRoot);

        /// <summary>Test seam: reads back the ordering score OrderMoves would have used for this
        /// move at this ply, including any killer/history bonus and (when a board is supplied) any
        /// static-exchange nudge recorded so far. A null board mirrors the pre-SEE behavior (no
        /// nudge) — most existing callers only care about killer/history tier placement and don't
        /// have a board handy.</summary>
        internal int OrderScoreForTest(MoveCommand move, uint ttMove, int plyFromRoot, BoardState board = null) =>
            OrderScore(move, ttMove, plyFromRoot, board == null ? 0 : ComputeSeeOrderBonus(board, move));

        private void RecordQuietCutoff(MoveCommand move, int depth, int plyFromRoot)
        {
            if (move.IsCapture || move.IsPromotion || move.Stage != BetrayalStage.None) return;

            // Depth-weighted so a cutoff found deep in the tree (a stronger, more expensive proof)
            // outweighs one found shallow. Clamped well under QuietMoveOrderScore's killer bonus
            // (9000/8000) so history can nudge ordering among non-killer quiet moves but never adds
            // up to enough to compete with an actual killer-slot match, let alone the Act tier above it.
            ref int score = ref _historyScores[(int)move.PieceType, SquareIndex(move.EndPosition)];
            score = Math.Min(score + depth * depth, 999);

            if (plyFromRoot < 0 || plyFromRoot >= _killerMoves.GetLength(0)) return;

            uint packed = PackMove(move);
            if (packed == _killerMoves[plyFromRoot, 0]) return; // already the top killer here

            _killerMoves[plyFromRoot, 1] = _killerMoves[plyFromRoot, 0];
            _killerMoves[plyFromRoot, 0] = packed;
        }

        /// <summary>
        /// Packs the fields OrderScore's TT-move comparison needs into 19 bits: From(6) | To(6) |
        /// PromotedTo(4) | Stage(3). Never rehydrated back into a MoveCommand — a caller only ever
        /// matches this against the packed form of a freshly generated legal move, so a
        /// stale/collided value can mis-order a node (or miss an opening-book entry) but can never
        /// inject an illegal move. Also used by the opening book compiler, which stores this same
        /// packing alongside each position's Zobrist hash.
        /// </summary>
        internal static uint PackMove(MoveCommand m)
        {
            uint from = (uint)(m.StartPosition.y * 8 + m.StartPosition.x) & 0x3F;
            uint to = (uint)(m.EndPosition.y * 8 + m.EndPosition.x) & 0x3F;
            uint promo = (uint)m.PromotedTo & 0xF;
            uint stage = (uint)m.Stage & 0x7;
            return from | (to << 6) | (promo << 12) | (stage << 16);
        }

        /// <summary>True once two consecutive completed depths agree closely enough that another
        /// depth is unlikely to change the answer — same best move AND a score within
        /// StabilityThresholdCp of each other. Neither signal alone is trustworthy on its own (see
        /// the constant's own doc comment), so both are required.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRootStable(int currentScore, int previousScore, MoveCommand currentBest, MoveCommand previousBest)
        {
            bool sameBestMove = PackMove(currentBest) == PackMove(previousBest);
            int scoreDelta = currentScore - previousScore;
            if (scoreDelta < 0) scoreDelta = -scoreDelta;
            return sameBestMove && scoreDelta <= StabilityThresholdCp;
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

        // Quiescence delta-pruning margin, in the evaluator's centipawn scale (BetrayalAwareEvaluator:
        // P=100..Q=975). Covers promotion upside on a capturing pawn push plus general evaluator
        // noise, so a capture that's only borderline-hopeless is never wrongly pruned before it's
        // tried. This is the standard "even best case can't help" quiescence cut — it only skips
        // moves whose absolute best-case outcome still can't reach alpha, so it can never change
        // which move quiescence ultimately reports as best, only how many hopeless captures it
        // bothers exploring on the way there.
        private const int DeltaPruningMargin = 200;

        private static int CapturedPieceValue(ChessPieceType t) => t switch
        {
            ChessPieceType.Queen => 975,
            ChessPieceType.Rook => 500,
            ChessPieceType.Bishop => 325,
            ChessPieceType.Knight => 320,
            ChessPieceType.Pawn => 100,
            _ => 0
        };

        /// <summary>
        /// Moves _rootMoves[index] to the front, shifting [0, index) up by one — and permutes
        /// _rootScores identically in lockstep, so _rootScores[i] always stays the committed score
        /// for _rootMoves[i] (see FindBestMove's completed-gated commit). Safe to call before
        /// _rootScores has been populated for the current search (see the pre-depth-loop TT-hint
        /// call site) — it just shuffles stale data that gets fully overwritten before first read.
        /// </summary>
        private void MoveToFront(int index)
        {
            if (index <= 0) return;

            MoveCommand move = _rootMoves[index];
            _rootMoves.RemoveAt(index);
            _rootMoves.Insert(0, move);

            int score = _rootScores[index];
            Array.Copy(_rootScores, 0, _rootScores, 1, index);
            _rootScores[0] = score;
        }

        /// <summary>Root-only TT ordering hint (see FindBestMove) — finds the root move matching the
        /// probed packed move, if any, and moves it to the front. A miss (0 or no match) is a no-op.</summary>
        private void MoveToFrontByPackedMove(uint packedMove)
        {
            if (packedMove == 0) return;

            for (int i = 0; i < _rootMoves.Count; i++)
            {
                if (PackMove(_rootMoves[i]) == packedMove)
                {
                    MoveToFront(i);
                    return;
                }
            }
        }
    }
}
