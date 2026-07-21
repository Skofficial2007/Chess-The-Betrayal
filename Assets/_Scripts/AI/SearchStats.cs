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

        // Main-search nodes whose pending Retribution had no legal executioner, so the Betrayer's
        // forced Defection was resolved in-line and the search continued through it (see
        // AlphaBetaSearch.ResolveForcedDefectionInSearch). Quiescence's own resolutions are counted
        // separately under QBetrayalResolutionNodes below.
        public long ForcedDefectionResolutions;

        public long IirReductions; // nodes given a shallower probe search to find an ordering move before the real search

        // Aspiration windows (experimental) — depths searched with a narrow guessed window, and how
        // many of those had to be thrown away and re-searched with the full window because the
        // guess was wrong (a fail-low or fail-high against the narrow bound).
        public long AspirationWindowAttempts;
        public long AspirationWindowReSearches;

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
        /// cap (e.g. two devices both capped at a profile's TimeBudget) — the elapsed time
        /// alone is identical in that case, but the depth reached is not.</summary>
        public int LastCompletedDepth;

        // Per-depth cumulative node count (main + quiescence) at the moment each iterative-deepening
        // depth FULLY completes — the effective-branching-factor curve. Tracked through depth 12 so
        // the cost of the deepest tiers' 7->8->9 transition is visible rather than dropped: those
        // three tiers all bottom out around the same effective depth under the real move budget, and
        // seeing exactly how the tree grows across that band is the whole point of this curve.
        public long NodesAfterDepth1;
        public long NodesAfterDepth2;
        public long NodesAfterDepth3;
        public long NodesAfterDepth4;
        public long NodesAfterDepth5;
        public long NodesAfterDepth6;
        public long NodesAfterDepth7;
        public long NodesAfterDepth8;
        public long NodesAfterDepth9;
        public long NodesAfterDepth10;
        public long NodesAfterDepth11;
        public long NodesAfterDepth12;

        // Wall-clock time elapsed since the search began, sampled at the moment each depth completes —
        // the companion to the node curve above. Node counts alone can't tell you whether a depth is
        // expensive because it visits more positions or because each position costs more to score; the
        // two curves side by side separate those. Cumulative from the start of the search, so a single
        // stopwatch read per completed depth captures it with no per-node cost.
        public long ElapsedMsAfterDepth1;
        public long ElapsedMsAfterDepth2;
        public long ElapsedMsAfterDepth3;
        public long ElapsedMsAfterDepth4;
        public long ElapsedMsAfterDepth5;
        public long ElapsedMsAfterDepth6;
        public long ElapsedMsAfterDepth7;
        public long ElapsedMsAfterDepth8;
        public long ElapsedMsAfterDepth9;
        public long ElapsedMsAfterDepth10;
        public long ElapsedMsAfterDepth11;
        public long ElapsedMsAfterDepth12;

        public void Reset()
        {
            this = default;
        }

        /// <summary>Records the running node total (NodesVisited + QNodesVisited) at the moment a
        /// depth in 1..12 fully completes. Depths beyond 12 are ignored rather than throwing, since a
        /// search can legitimately be configured deeper than the curve tracks; the curve just stops
        /// reporting past its last slot instead of failing the search.</summary>
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
                case 8: NodesAfterDepth8 = totalNodes; break;
                case 9: NodesAfterDepth9 = totalNodes; break;
                case 10: NodesAfterDepth10 = totalNodes; break;
                case 11: NodesAfterDepth11 = totalNodes; break;
                case 12: NodesAfterDepth12 = totalNodes; break;
            }
        }

        /// <summary>The cumulative node total recorded when the given depth completed, or 0 for a
        /// depth outside 1..12 or one the search never reached.</summary>
        public long NodesAfterDepth(int depth)
        {
            switch (depth)
            {
                case 1: return NodesAfterDepth1;
                case 2: return NodesAfterDepth2;
                case 3: return NodesAfterDepth3;
                case 4: return NodesAfterDepth4;
                case 5: return NodesAfterDepth5;
                case 6: return NodesAfterDepth6;
                case 7: return NodesAfterDepth7;
                case 8: return NodesAfterDepth8;
                case 9: return NodesAfterDepth9;
                case 10: return NodesAfterDepth10;
                case 11: return NodesAfterDepth11;
                case 12: return NodesAfterDepth12;
                default: return 0;
            }
        }

        /// <summary>How many times the tree grew going from the previous depth to this one — the
        /// cumulative node count at <paramref name="depth"/> divided by the count at depth-1. A rough
        /// effective branching factor: the lower it is, the better move ordering and pruning are
        /// keeping the search from fanning out. Returns 0 when either depth wasn't reached (so there's
        /// nothing to compare), which callers read as "not measurable here" rather than a real ratio.</summary>
        public double EffectiveBranchingFactor(int depth)
        {
            if (depth < 2) return 0.0;
            long previous = NodesAfterDepth(depth - 1);
            long current = NodesAfterDepth(depth);
            if (previous <= 0 || current <= 0) return 0.0;
            return (double)current / previous;
        }

        /// <summary>Records the cumulative wall-clock milliseconds elapsed at the moment a depth in
        /// 1..12 fully completes. Same ceiling and out-of-range handling as the node curve above.</summary>
        public void AssignElapsedMsAfterDepth(int depth, long elapsedMs)
        {
            switch (depth)
            {
                case 1: ElapsedMsAfterDepth1 = elapsedMs; break;
                case 2: ElapsedMsAfterDepth2 = elapsedMs; break;
                case 3: ElapsedMsAfterDepth3 = elapsedMs; break;
                case 4: ElapsedMsAfterDepth4 = elapsedMs; break;
                case 5: ElapsedMsAfterDepth5 = elapsedMs; break;
                case 6: ElapsedMsAfterDepth6 = elapsedMs; break;
                case 7: ElapsedMsAfterDepth7 = elapsedMs; break;
                case 8: ElapsedMsAfterDepth8 = elapsedMs; break;
                case 9: ElapsedMsAfterDepth9 = elapsedMs; break;
                case 10: ElapsedMsAfterDepth10 = elapsedMs; break;
                case 11: ElapsedMsAfterDepth11 = elapsedMs; break;
                case 12: ElapsedMsAfterDepth12 = elapsedMs; break;
            }
        }

        public override string ToString() =>
            $"depth={LastCompletedDepth} nodes={NodesVisited} tt(probe={TTProbes} hit={TTHits} emptyMiss={TTEmptyMisses} verifyMiss={TTVerificationMisses} store={TTStores} replace={TTReplacements}) " +
            $"null(try={NullMoveAttempts} cut={NullMoveCutoffs}) lmr(reduce={LmrReductions} research={LmrReSearches}) pvs(scout={PvsScouts} research={PvsReSearches}) " +
            $"fwdPrune(rfp={ReverseFutilityCutoffs} lmp={LateMovePrunes} ffp={FrontierFutilityPrunes}) betrayalExt={BetrayalExtensions} forcedDefection={ForcedDefectionResolutions} iir={IirReductions} " +
            $"aspiration(attempt={AspirationWindowAttempts} research={AspirationWindowReSearches}) " +
            $"q(nodes={QNodesVisited} betrayalRes={QBetrayalResolutionNodes} actExp={QActExpansions} gen={QMovesGenerated} searched={QMovesSearched} seePrune={SeeQuiescencePrunes}) " +
            $"depthCurve(d1={NodesAfterDepth1} d2={NodesAfterDepth2} d3={NodesAfterDepth3} d4={NodesAfterDepth4} d5={NodesAfterDepth5} d6={NodesAfterDepth6} d7={NodesAfterDepth7} d8={NodesAfterDepth8} d9={NodesAfterDepth9} d10={NodesAfterDepth10} d11={NodesAfterDepth11} d12={NodesAfterDepth12}) " +
            $"msCurve(d1={ElapsedMsAfterDepth1} d2={ElapsedMsAfterDepth2} d3={ElapsedMsAfterDepth3} d4={ElapsedMsAfterDepth4} d5={ElapsedMsAfterDepth5} d6={ElapsedMsAfterDepth6} d7={ElapsedMsAfterDepth7} d8={ElapsedMsAfterDepth8} d9={ElapsedMsAfterDepth9} d10={ElapsedMsAfterDepth10} d11={ElapsedMsAfterDepth11} d12={ElapsedMsAfterDepth12}) " +
            $"ebf(d7={EffectiveBranchingFactor(7):F2} d8={EffectiveBranchingFactor(8):F2} d9={EffectiveBranchingFactor(9):F2} d10={EffectiveBranchingFactor(10):F2} d11={EffectiveBranchingFactor(11):F2} d12={EffectiveBranchingFactor(12):F2})";
    }
}
