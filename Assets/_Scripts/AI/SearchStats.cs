namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Search telemetry. Plain counters, no allocation, reset once per FindBestMove — exists purely
    /// to measure each pruning mechanism's node-count impact on fixed regression positions.
    /// Conditional-compiled: every counter increment lives behind the editor/development-build
    /// guard, so a release build pays zero cost (not even a branch) for tracking it. See
    /// AlphaBetaSearch/TranspositionTable's #if sites.
    /// </summary>
    public struct SearchStats
    {
        public long NodesVisited;
        public long TTProbes;
        public long TTHits;
        public long TTEmptyMisses;        // slot had never been written (both lanes zero)
        public long TTVerificationMisses; // occupied slot whose KeyLane/DataLane XOR didn't match —
                                           // an index collision or a torn write; the XOR check can't
                                           // tell which apart, so they're counted together
        public long TTStores;
        public long TTReplacements;       // a Store that overwrote an existing, non-empty slot
        public long NullMoveAttempts;
        public long NullMoveCutoffs;
        public long LmrReductions;
        public long LmrReSearches;    // reduced move failed high and was re-searched at full depth
        public long PvsScouts;
        public long PvsReSearches;    // null-window scout landed inside (alpha, beta) and was re-searched

        // Forward-pruning family — each counts a node/move it actually skipped, not an
        // attempt, mirroring NullMoveCutoffs' own "only the successful case" convention.
        public long ReverseFutilityCutoffs;
        public long LateMovePrunes;
        public long FrontierFutilityPrunes;

        public long BetrayalExtensions; // Acts granted a search extension for staging a forced Retribution

        public long IirReductions; // nodes given a shallower probe search to find an ordering move before the real search

        // Quiescence node-count breakdown. NodesVisited above counts ONLY main-search (Search)
        // nodes; Quiescence() increments nothing there by design. These fields fill that gap so the
        // qtree's size and shape can be measured directly instead of inferred from wall-clock/
        // NodesVisited arithmetic.
        public long QNodesVisited;             // every Quiescence() entry — the qtree/main-tree split
        public long QBetrayalResolutionNodes;   // qnodes spent inside the betrayerPending forced-sequence branch
        public long QActExpansions;             // qsearch-loop moves surviving the filter with Stage == Act
        public long QMovesGenerated;            // total moves returned by the captures/Acts generator per standard qnode
        public long QMovesSearched;             // of those, how many survived the delta-prune filter
        public long SeeQuiescencePrunes;        // qsearch captures skipped for having a losing static-exchange result

        /// <summary>The deepest iterative-deepening depth FindBestMove fully completed before
        /// returning — via a soft-time-budget cancellation, MaxDepth being reached, or an early
        /// mate-found exit. Lets a caller distinguish "the search finished on its own" from "it
        /// got cut off partway" without changing FindBestMove's return type, and is the only way
        /// to compare search throughput across two runs that both hit the same wall-clock budget
        /// cap (e.g. two devices both capped at a profile's SoftTimeBudgetMs) — the elapsed time
        /// alone is identical in that case, but the depth reached is not.</summary>
        public int LastCompletedDepth;

        // Per-depth cumulative node count (main + quiescence) at the moment each iterative-deepening
        // depth FULLY completes — the effective-branching-factor curve. Index 0 unused (depths are 1-7).
        public long NodesAfterDepth1;
        public long NodesAfterDepth2;
        public long NodesAfterDepth3;
        public long NodesAfterDepth4;
        public long NodesAfterDepth5;
        public long NodesAfterDepth6;
        public long NodesAfterDepth7;

        public void Reset()
        {
            this = default;
        }

        /// <summary>Records the running node total (NodesVisited + QNodesVisited) at the moment a
        /// depth in 1..7 fully completes. Depths beyond 7 are not tracked (the benchmark this feeds
        /// is fixed to depth 7) — silently ignored rather than throwing, since search settings can
        /// legitimately go deeper than 7 outside the benchmark.</summary>
        public void AssignNodesAfterDepth(int depth, long totalNodes)
        {
            switch (depth)
            {
                case 1: NodesAfterDepth1 = totalNodes; break;
                case 2: NodesAfterDepth2 = totalNodes; break;
                case 3: NodesAfterDepth3 = totalNodes; break;
                case 4: NodesAfterDepth4 = totalNodes; break;
                case 5: NodesAfterDepth5 = totalNodes; break;
                case 6: NodesAfterDepth6 = totalNodes; break;
                case 7: NodesAfterDepth7 = totalNodes; break;
            }
        }

        public override string ToString() =>
            $"depth={LastCompletedDepth} nodes={NodesVisited} tt(probe={TTProbes} hit={TTHits} emptyMiss={TTEmptyMisses} verifyMiss={TTVerificationMisses} store={TTStores} replace={TTReplacements}) " +
            $"null(try={NullMoveAttempts} cut={NullMoveCutoffs}) lmr(reduce={LmrReductions} research={LmrReSearches}) pvs(scout={PvsScouts} research={PvsReSearches}) " +
            $"fwdPrune(rfp={ReverseFutilityCutoffs} lmp={LateMovePrunes} ffp={FrontierFutilityPrunes}) betrayalExt={BetrayalExtensions} iir={IirReductions} " +
            $"q(nodes={QNodesVisited} betrayalRes={QBetrayalResolutionNodes} actExp={QActExpansions} gen={QMovesGenerated} searched={QMovesSearched} seePrune={SeeQuiescencePrunes}) " +
            $"depthCurve(d1={NodesAfterDepth1} d2={NodesAfterDepth2} d3={NodesAfterDepth3} d4={NodesAfterDepth4} d5={NodesAfterDepth5} d6={NodesAfterDepth6} d7={NodesAfterDepth7})";
    }
}
