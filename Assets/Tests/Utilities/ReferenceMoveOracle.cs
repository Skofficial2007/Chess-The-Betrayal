using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Answers "what move would a much deeper search play here?" and remembers the answers.
    ///
    /// This is the reference half of an agreement measurement. Scoring a change by playing games
    /// works badly between two near-identical engines: most games are drawn, and a draw says
    /// nothing about which side was better, so hundreds of games buy very little information.
    /// Asking a fixed set of positions instead yields one clear answer per position and can never
    /// come back "drawn". The comparison is meaningful rather than circular because this reference
    /// searches far deeper than the profile being measured, so it stands in for the conclusion more
    /// thinking would reach — which is exactly the quantity at issue.
    ///
    /// Deliberately not written to the allocation-free standard the search itself holds: this is
    /// development tooling that runs a handful of times, and contorting its shape to avoid
    /// allocations would buy nothing and cost clarity.
    /// </summary>
    public sealed class ReferenceMoveOracle
    {
        /// <summary>
        /// The reference must be deeper than any profile it will ever judge, otherwise it is just a
        /// peer opinion rather than an oracle. The deepest configured profile ceiling is 9, so this
        /// clears every one of them rather than matching them.
        ///
        /// Measured cost on ordinary midgame positions climbs roughly two and a half times per ply:
        /// about 14s per position at depth 9, 40s at 10, 93s at 11 and 227s at 12. Depth 10 is the
        /// chosen balance — clear of every profile ceiling, and cheap enough that a full set can be
        /// answered in minutes rather than hours. Going deeper is affordable for a one-off
        /// investigation, which is why this is a default rather than a fixed value.
        /// </summary>
        public const int DefaultReferenceDepth = 10;

        private readonly Dictionary<ulong, ReferenceMove> _cache = new Dictionary<ulong, ReferenceMove>();
        private readonly AIProfile _referenceProfile;

        public int ReferenceDepth { get; }

        /// <summary>Positions answered from cache rather than by searching. Lets a caller confirm
        /// the cache is actually being exercised instead of silently missing every time.</summary>
        public int CacheHits { get; private set; }

        /// <summary>Positions that required a fresh deep search.</summary>
        public int CacheMisses { get; private set; }

        /// <param name="referenceProfile">Supplies the evaluator the reference judges with. Its own
        /// depth and time budget are deliberately ignored — see Answer.</param>
        public ReferenceMoveOracle(AIProfile referenceProfile, int referenceDepth = DefaultReferenceDepth)
        {
            _referenceProfile = referenceProfile;
            ReferenceDepth = referenceDepth;
        }

        /// <summary>
        /// Whether this position's answer actually holds still, or merely happens to be whatever the
        /// chosen depth landed on.
        ///
        /// Some positions genuinely alternate their best move between one ply and the next — a
        /// long-known effect of the last ply belonging to one side or the other — and on those, the
        /// reference's answer is a fact about the depth that was picked rather than about the
        /// position. Measuring agreement against such a position is measuring nothing useful, so a
        /// caller can ask here and exclude it instead of quietly folding parity noise into the
        /// headline number.
        ///
        /// Answers the question by searching the two plies below the reference depth and requiring
        /// all three to name the same move. Expensive, which is why it is a separate deliberate call
        /// rather than something Answer does automatically.
        /// </summary>
        public bool IsStableAcrossDepths(BoardState board)
        {
            ReferenceMove atReferenceDepth = Answer(board);

            for (int depth = ReferenceDepth - 2; depth < ReferenceDepth; depth++)
            {
                MoveCommand shallower = SearchToDepth(board, depth);
                if (shallower.StartPosition != atReferenceDepth.Move.StartPosition
                    || shallower.EndPosition != atReferenceDepth.Move.EndPosition
                    || shallower.Stage != atReferenceDepth.Move.Stage)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The deep search's move for this position, computed once and reused thereafter.
        ///
        /// The search is bound by DEPTH alone: no cancellation token that can fire, no clock, and
        /// neither of the search's optional time-management or aspiration behaviours enabled. That
        /// combination is what makes the answer a property of the position rather than of how busy
        /// the machine happened to be — a reference that changes when the machine is loaded cannot
        /// serve as a fixed point to measure anything else against.
        ///
        /// The board is not modified: the search is given its own copy, since searching mutates the
        /// board it is handed and callers reuse theirs.
        /// </summary>
        public ReferenceMove Answer(BoardState board)
        {
            ulong hash = board.ZobristHash;
            ulong scheme = BoardState.ZobristSchemeVersion;

            if (_cache.TryGetValue(hash, out ReferenceMove cached))
            {
                // A hit whose scheme no longer matches is a stale answer about a position that hash
                // no longer identifies. Drop it and re-derive rather than reporting agreement
                // against an answer to some other question entirely.
                if (cached.IsValidFor(hash, scheme))
                {
                    CacheHits++;
                    return cached;
                }

                _cache.Remove(hash);
            }

            CacheMisses++;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            MoveCommand move = SearchToDepth(board, ReferenceDepth, out int scoreCp);
            stopwatch.Stop();

            var answer = new ReferenceMove(hash, scheme, move, scoreCp, ReferenceDepth,
                stopwatch.Elapsed.TotalMilliseconds);
            _cache[hash] = answer;
            return answer;
        }

        private MoveCommand SearchToDepth(BoardState board, int depth) =>
            SearchToDepth(board, depth, out _);

        /// <summary>
        /// One depth-bound search with the reference evaluator. Deliberately builds a fresh search
        /// each call so no transposition table carries over between depths — a shared table would
        /// let a shallower answer leak into a deeper one, and the whole point here is that each
        /// depth reports its own independent conclusion.
        ///
        /// The board is cloned because searching mutates the board it is handed.
        /// </summary>
        private MoveCommand SearchToDepth(BoardState board, int depth, out int scoreCp)
        {
            var engine = new ChessEngineAdapter();
            var search = new AlphaBetaSearch(engine,
                new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(_referenceProfile)));
            var settings = new AISearchSettings(depth, _referenceProfile.TimeBudget, BetrayalUsage.Full);

            MoveCommand move = search.FindBestMove(board.CloneForSnapshot(), settings, CancellationToken.None);
            scoreCp = search.RootMoveCount > 0 ? search.RootScores[search.BestRootIndex] : 0;
            return move;
        }
    }
}
