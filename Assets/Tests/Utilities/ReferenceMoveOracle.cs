using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI;
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
        /// clears every one of them with room to spare rather than by a single ply.
        /// </summary>
        public const int DefaultReferenceDepth = 12;

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

            var engine = new ChessEngineAdapter();
            var search = new AlphaBetaSearch(engine,
                new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(_referenceProfile)));
            var settings = new AISearchSettings(ReferenceDepth, _referenceProfile.TimeBudget, BetrayalUsage.Full);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            MoveCommand move = search.FindBestMove(board.CloneForSnapshot(), settings, CancellationToken.None);
            stopwatch.Stop();

            int scoreCp = search.RootMoveCount > 0 ? search.RootScores[search.BestRootIndex] : 0;

            var answer = new ReferenceMove(hash, scheme, move, scoreCp, ReferenceDepth,
                stopwatch.Elapsed.TotalMilliseconds);
            _cache[hash] = answer;
            return answer;
        }
    }
}
