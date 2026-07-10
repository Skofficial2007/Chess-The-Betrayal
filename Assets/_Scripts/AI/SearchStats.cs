namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Search telemetry (ADR Sec "Step 3", AI-21). Plain counters, no allocation, reset once per
    /// FindBestMove — exists purely to prove each pruning mechanism's node-count multiplier against
    /// the Depth-7 Definition of Done on fixed regression positions. Conditional-compiled: every
    /// counter increment lives behind AI_SEARCH_TELEMETRY, so a release build pays zero cost (not
    /// even a branch) for tracking it. See AlphaBetaSearch/TranspositionTable's #if sites.
    /// </summary>
    public struct SearchStats
    {
        public long NodesVisited;
        public long TTProbes;
        public long TTHits;
        public long TTEmptyMisses;        // slot had never been written (both lanes zero)
        public long TTVerificationMisses; // occupied slot whose KeyLane/DataLane XOR didn't match —
                                           // an index collision or a torn write; the XOR check can't
                                           // tell which, so (per the ADR) they're counted together
        public long TTStores;
        public long TTReplacements;       // a Store that overwrote an existing, non-empty slot
        public long NullMoveAttempts;
        public long NullMoveCutoffs;
        public long LmrReductions;
        public long LmrReSearches;    // reduced move failed high and was re-searched at full depth
        public long PvsScouts;
        public long PvsReSearches;    // null-window scout landed inside (alpha, beta) and was re-searched

        public void Reset()
        {
            this = default;
        }

        public override string ToString() =>
            $"nodes={NodesVisited} tt(probe={TTProbes} hit={TTHits} emptyMiss={TTEmptyMisses} verifyMiss={TTVerificationMisses} store={TTStores} replace={TTReplacements}) " +
            $"null(try={NullMoveAttempts} cut={NullMoveCutoffs}) lmr(reduce={LmrReductions} research={LmrReSearches}) pvs(scout={PvsScouts} research={PvsReSearches})";
    }
}
