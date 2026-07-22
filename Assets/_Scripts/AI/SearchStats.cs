namespace ChessTheBetrayal.AI
{
    /// <summary>Why iterative deepening stopped where it did. The default, Unset, only ever shows up
    /// if telemetry is compiled out or a search somehow returns without taking any of the loop's exit
    /// paths — every real search sets this to one of the other four before returning.</summary>
    public enum SearchStopReason
    {
        Unset = 0,

        /// <summary>The position was judged settled (the instability-management logic decided
        /// further depth was unlikely to change the answer) and the soft time budget had already
        /// elapsed, so the search stopped early rather than spend the rest of the budget.</summary>
        SettledEarly,

        /// <summary>The external cancellation token fired, or the search's own instability-management
        /// logic hit the hard time budget without the position ever settling — time ran out before
        /// the position was decided.</summary>
        Budget,

        /// <summary>A forced mate was found; no deeper search can change that decision.</summary>
        MateFound,

        /// <summary>Iterative deepening completed every depth up to and including MaxDepth without
        /// being stopped by any of the above — the tier's configured ceiling, not the clock, is what
        /// ended the search.</summary>
        Ceiling
    }

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

        // Why a node that reached the null-move decision point did NOT get an attempt. Mutually
        // exclusive with each other and with NullMoveAttempts — a node is counted against the FIRST
        // reason it fails, so these five plus NullMoveAttempts should always sum to the number of
        // nodes that reached this decision point at all. Exists to tell apart "null-move pruning
        // isn't being tried here" from "it's being tried and failing," which a bare attempt/cutoff
        // count can't distinguish.
        public long NullMoveSkippedByDepth;       // too shallow for this depth's minimum
        public long NullMoveSkippedByGuard;       // a pending Betrayer or being in check
        public long NullMoveSkippedByParentNull;  // the parent ply was itself a null move
        public long NullMoveSkippedByMaterial;    // side to move has no non-pawn material (zugzwang risk)
        public long NullMoveSkippedByBeta;        // beta is inside the mate-score range
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

        // How often a beta cutoff is won by the first move tried at a node, versus a later one —
        // the direct signal for whether move ordering is doing its job. A healthy search wins the
        // large majority of cutoffs on the first try; a falling rate at deeper nodes means ordering
        // is losing its grip as the tree grows.
        public long BetaCutoffs;
        public long FirstMoveBetaCutoffs;

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
        public long QMovesSearched;             // of those, how many survived the Act re-expansion gate (counted before the delta-prune check, not after)
        public long SeeQuiescencePrunes;        // qsearch captures skipped for having a losing static-exchange result

        /// <summary>The deepest iterative-deepening depth FindBestMove fully completed before
        /// returning — via a soft-time-budget cancellation, MaxDepth being reached, or an early
        /// mate-found exit. Lets a caller distinguish "the search finished on its own" from "it
        /// got cut off partway" without changing FindBestMove's return type, and is the only way
        /// to compare search throughput across two runs that both hit the same wall-clock budget
        /// cap (e.g. two devices both capped at a profile's TimeBudget) — the elapsed time
        /// alone is identical in that case, but the depth reached is not.</summary>
        public int LastCompletedDepth;

        /// <summary>Why FindBestMove stopped where it did. A budget-capped run and a settled-early
        /// run can land on the same LastCompletedDepth for entirely different reasons — one ran out
        /// of time, the other decided further search wouldn't change the answer — and those two
        /// cases mean opposite things for whether a deeper MaxDepth would ever actually get used.
        /// Written once per search, at whichever exit the loop actually takes.</summary>
        public SearchStopReason StopReason;

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

        // Where the search's time actually goes, broken out by the four sections that dominate a node:
        // scoring a position, generating its moves, the transposition-table probe/store, and the
        // quiescence tail. Timing every single call would cost more than it measures at hundreds of
        // thousands of nodes per move, so each section is timed on only a fraction of its calls (see
        // SectionSampleInterval) and scaled back up. The raw ticks and the sampled-call count are both
        // kept so the estimate can be scaled honestly: estimated section ms = ticks scaled by
        // (total calls / sampled calls). "Ticks" are Stopwatch timestamp ticks, not milliseconds.
        public long EvalSampledTicks;
        public long EvalSamples;        // how many eval calls were actually timed
        public long EvalCalls;          // how many eval calls happened in total
        public long MoveGenSampledTicks;
        public long MoveGenSamples;
        public long MoveGenCalls;
        public long TTSampledTicks;
        public long TTSamples;
        public long TTCalls;
        public long QuiescenceSampledTicks;
        public long QuiescenceSamples;
        public long QuiescenceCalls;

        /// <summary>One in this many calls to a section is actually timed. A power of two so the
        /// "should I sample this one?" test is a cheap bitmask against the call counter rather than a
        /// modulo. Large enough that the timing overhead is a rounding error even on the deepest
        /// searches, small enough that hundreds of thousands of nodes still yield thousands of
        /// samples per section.</summary>
        public const long SectionSampleInterval = 1024;
        private const long SectionSampleMask = SectionSampleInterval - 1;

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

        /// <summary>Share of beta cutoffs won by the first move tried at a node, in 0..1. Returns 0
        /// when no cutoff has happened yet rather than dividing by zero.</summary>
        public double FirstMoveCutoffRate() => BetaCutoffs <= 0 ? 0.0 : (double)FirstMoveBetaCutoffs / BetaCutoffs;

        // --- Section timing: sample gates and recorders ---
        // Each section calls its ShouldSample* once per call (which counts the call and returns true
        // on the sampled fraction), takes a start timestamp only when it returned true, runs the
        // section, then hands the elapsed ticks to the matching Record*. Splitting "decide + count"
        // from "record ticks" keeps the timed span to exactly the section itself — the sampling
        // arithmetic sits outside the two timestamp reads.

        public bool ShouldSampleEval() => (EvalCalls++ & SectionSampleMask) == 0;
        public void RecordEvalTicks(long ticks) { EvalSampledTicks += ticks; EvalSamples++; }

        public bool ShouldSampleMoveGen() => (MoveGenCalls++ & SectionSampleMask) == 0;
        public void RecordMoveGenTicks(long ticks) { MoveGenSampledTicks += ticks; MoveGenSamples++; }

        public bool ShouldSampleTT() => (TTCalls++ & SectionSampleMask) == 0;
        public void RecordTTTicks(long ticks) { TTSampledTicks += ticks; TTSamples++; }

        public bool ShouldSampleQuiescence() => (QuiescenceCalls++ & SectionSampleMask) == 0;
        public void RecordQuiescenceTicks(long ticks) { QuiescenceSampledTicks += ticks; QuiescenceSamples++; }

        /// <summary>Scales a section's sampled ticks back up to an estimated total across all its
        /// calls, converted to milliseconds. Only a fraction of calls were timed, so the sampled ticks
        /// are multiplied by (total calls / sampled calls). Returns 0 when nothing was sampled.</summary>
        public double EstimatedSectionMs(long sampledTicks, long samples, long calls)
        {
            if (samples <= 0 || calls <= 0) return 0.0;
            double scaledTicks = (double)sampledTicks * calls / samples;
            return scaledTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
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
            $"depth={LastCompletedDepth} stopReason={StopReason} nodes={NodesVisited} tt(probe={TTProbes} hit={TTHits} emptyMiss={TTEmptyMisses} verifyMiss={TTVerificationMisses} store={TTStores} replace={TTReplacements}) " +
            $"null(try={NullMoveAttempts} cut={NullMoveCutoffs} skip(depth={NullMoveSkippedByDepth} guard={NullMoveSkippedByGuard} parentNull={NullMoveSkippedByParentNull} material={NullMoveSkippedByMaterial} beta={NullMoveSkippedByBeta})) lmr(reduce={LmrReductions} research={LmrReSearches}) pvs(scout={PvsScouts} research={PvsReSearches}) " +
            $"fwdPrune(rfp={ReverseFutilityCutoffs} lmp={LateMovePrunes} ffp={FrontierFutilityPrunes}) betrayalExt={BetrayalExtensions} forcedDefection={ForcedDefectionResolutions} iir={IirReductions} " +
            $"cutoff(total={BetaCutoffs} firstMove={FirstMoveBetaCutoffs} rate={FirstMoveCutoffRate():F3}) " +
            $"aspiration(attempt={AspirationWindowAttempts} research={AspirationWindowReSearches}) " +
            $"q(nodes={QNodesVisited} betrayalRes={QBetrayalResolutionNodes} actExp={QActExpansions} gen={QMovesGenerated} searched={QMovesSearched} seePrune={SeeQuiescencePrunes}) " +
            $"depthCurve(d1={NodesAfterDepth1} d2={NodesAfterDepth2} d3={NodesAfterDepth3} d4={NodesAfterDepth4} d5={NodesAfterDepth5} d6={NodesAfterDepth6} d7={NodesAfterDepth7} d8={NodesAfterDepth8} d9={NodesAfterDepth9} d10={NodesAfterDepth10} d11={NodesAfterDepth11} d12={NodesAfterDepth12}) " +
            $"msCurve(d1={ElapsedMsAfterDepth1} d2={ElapsedMsAfterDepth2} d3={ElapsedMsAfterDepth3} d4={ElapsedMsAfterDepth4} d5={ElapsedMsAfterDepth5} d6={ElapsedMsAfterDepth6} d7={ElapsedMsAfterDepth7} d8={ElapsedMsAfterDepth8} d9={ElapsedMsAfterDepth9} d10={ElapsedMsAfterDepth10} d11={ElapsedMsAfterDepth11} d12={ElapsedMsAfterDepth12}) " +
            $"ebf(d7={EffectiveBranchingFactor(7):F2} d8={EffectiveBranchingFactor(8):F2} d9={EffectiveBranchingFactor(9):F2} d10={EffectiveBranchingFactor(10):F2} d11={EffectiveBranchingFactor(11):F2} d12={EffectiveBranchingFactor(12):F2}) " +
            $"sectionMs~1/{SectionSampleInterval}(eval={EstimatedSectionMs(EvalSampledTicks, EvalSamples, EvalCalls):F1} " +
            $"movegen={EstimatedSectionMs(MoveGenSampledTicks, MoveGenSamples, MoveGenCalls):F1} " +
            $"tt={EstimatedSectionMs(TTSampledTicks, TTSamples, TTCalls):F1} " +
            $"quiescence={EstimatedSectionMs(QuiescenceSampledTicks, QuiescenceSamples, QuiescenceCalls):F1})";
    }
}
