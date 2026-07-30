using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// Unity-free benchmark logic: for one (position, tier, repeat) cell, times a single cold search
    /// and a several-ply play-forward from the same position. Deliberately has no dependency on
    /// MonoBehaviour/UnityEngine.Debug/OnGUI — those are presentation concerns owned by
    /// DeviceSearchBenchmark, which drives this class from a coroutine so results can render as
    /// they complete and iterates positions/tiers/repeats so an interrupted run still yields
    /// complete cross-tier coverage on however many positions finished. Keeping the two separate
    /// means this runner could be exercised from an EditMode test with no scene/Play-mode
    /// dependency, which it now is.
    ///
    /// Every search here is built the same way real gameplay builds one (production-sized
    /// transposition table, the profile's real evaluator weighting, the profile's rescore margin)
    /// so a device number means the same thing a match would have cost on that device.
    ///
    /// TEMPORARY diagnostic tool, not shipped gameplay code — delete this whole folder once real
    /// device throughput has been measured across enough devices and a mobile-tier perf plan
    /// exists. Because of that, this class allocates freely (StringBuilder-free plain strings, a
    /// List&lt;MoveCommand&gt; per run) rather than avoiding allocation the way the actual
    /// gameplay/search hot path (AlphaBetaSearch, TranspositionTable) has to — a benchmark run
    /// happens once per device visit, not sixty times a second, so it is not that hot path.
    /// </summary>
    public sealed class MobileSearchBenchmarkRunner
    {
        private const double ThresholdSeconds = 6.0;
        private const int DefaultPlyCount = 4;
        public const int DefaultRepeatCount = 3;

        // Matches AsyncAIAgent's production sizing (~16 MB). AlphaBetaSearch's own default
        // (log2Size: 16, ~1 MB) exists for lightweight callers that don't care about move-ordering
        // quality — a real match never uses it, so measuring against it understates how deep a
        // device actually searches.
        internal const int ProductionTranspositionTableLog2Size = 20;

        /// <summary>How many distinct positions a cell index can address: every curated opening
        /// line plus the two hand-placed positions in DepthWallPositions.</summary>
        public static int PositionCount => CuratedOpeningLines.Count + 2;

        /// <summary>One line of benchmark output, already formatted — the caller (a MonoBehaviour,
        /// a test, a console app) decides where it goes (Debug.Log, a scrolling label, stdout).</summary>
        public event Action<string> OnLine;

        public void EmitStartBanner() => Emit("Device search benchmark starting...");

        public void EmitCompletionBanner()
        {
            EmitDeviceInfo();

            // A single, unmistakable, greppable line — `adb logcat | grep BENCHMARK_RUN_COMPLETE`
            // (or eyeballing the on-screen log) tells you unambiguously that every cell ran to
            // completion, as opposed to the app having merely gone idle/frozen/crashed silently.
            Emit(CompletionMarker);
        }

        public const string CompletionMarker = "===BENCHMARK_RUN_COMPLETE===";

        /// <summary>
        /// Runs one (position, tier, repeat) cell: a single cold search plus a short play-forward
        /// from the same starting position, both freshly built. The caller supplies the already
        /// fully-resolved profile (guardrails applied) and is responsible for looping over
        /// positions/tiers/repeats and yielding between cells if it wants incremental UI updates
        /// (see DeviceSearchBenchmark.RunAll's coroutine wrapper).
        /// </summary>
        public void RunCell(int positionIndex, AIProfile profile, int repeatIndex)
        {
            string positionName = PositionName(positionIndex);
            RunSingleMove(profile, positionName, BuildPosition(positionIndex), repeatIndex);
            RunMultiMove(profile, positionName, BuildPosition(positionIndex), repeatIndex);
        }

        /// <summary>The label a cell's output lines use for this position: the curated opening's
        /// own move sequence, or one of the two hand-placed position names.</summary>
        internal static string PositionName(int positionIndex)
        {
            if (positionIndex < CuratedOpeningLines.Count)
                return CuratedOpeningLines.Line(positionIndex);

            return positionIndex == CuratedOpeningLines.Count ? "quiet-midgame" : "semi-open-midgame";
        }

        internal static BoardState BuildPosition(int positionIndex)
        {
            if (positionIndex < CuratedOpeningLines.Count)
                return CuratedOpeningLines.BuildPosition(positionIndex);

            return positionIndex == CuratedOpeningLines.Count
                ? DepthWallPositions.QuietMidgame()
                : DepthWallPositions.SemiOpenMidgame();
        }

        /// <summary>
        /// Builds a search wired the same way AsyncAIAgent builds its production one: the
        /// production-sized transposition table and the profile's real evaluator weighting, rather
        /// than the smaller table and the identity evaluator every profile got before this. Both
        /// call sites below need the identical shape, so it exists once here rather than twice.
        /// </summary>
        internal static AlphaBetaSearch BuildSearch(IChessEngine engine, AIProfile profile) =>
            new AlphaBetaSearch(engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile)),
                transpositionTable: new TranspositionTable(log2Size: ProductionTranspositionTableLog2Size));

        /// <summary>
        /// Full for the single cold search: nothing here gets applied to a board afterwards, so
        /// there is no play-forward loop that could choke on a staged Act move — this is the one
        /// place the benchmark can safely measure the setting a player actually gets by default
        /// (BetrayalUsage.Full; DefendOnly is the opt-in "disable AI Betrayal" toggle).
        /// </summary>
        internal static AISearchSettings SingleMoveSettingsFor(AIProfile profile) =>
            new AISearchSettings(profile.MaxDepth, profile.TimeBudget, BetrayalUsage.Full);

        /// <summary>
        /// DefendOnly, not Full: with Full, the search kept choosing a Betrayal Act as its root
        /// move on some positions (a piece betraying its own side), which opens the Retribution
        /// sub-sequence — a game state this simple play-forward runner doesn't model, so the
        /// multi-move loop misread the position as ended one ply later. DefendOnly strips Act
        /// from the ROOT ONLY; the search tree underneath still explores Betrayal branches at full
        /// cost (see BetrayalUsage's own doc comment), so the measured search work stays
        /// representative while the played-out line stays ordinary chess this simple loop can
        /// follow.
        /// </summary>
        internal static AISearchSettings MultiMoveSettingsFor(AIProfile profile) =>
            new AISearchSettings(profile.MaxDepth, profile.TimeBudget, BetrayalUsage.DefendOnly);

        /// <summary>
        /// Runs one search under the same wall-clock cap real gameplay uses: AsyncAIAgent arms
        /// CancelAfter(HardMs) on every request, and iterative deepening returns the best move
        /// from the last fully completed depth when that fires. Passing CancellationToken.None
        /// instead (an earlier version of this tool did) lets a deep tier like "impossible" run
        /// unbounded — timing a configuration that can never occur in a real match. Budget-capped
        /// timings are what players actually experience.
        ///
        /// candidateRescoreMarginCp matches production: a SettledEarly search still runs a rescore
        /// pass after the depth loop finishes, and skipping that pass (as an earlier version of
        /// this tool did) under-measures wall time on exactly the stop reason that dominates once
        /// the search settles well before its ceiling.
        /// </summary>
        private static MoveCommand TimedSearch(AlphaBetaSearch search, BoardState board,
            AISearchSettings settings, int candidateRescoreMarginCp, out SearchTiming timing)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(settings.TimeBudget.HardMs);

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best = search.FindBestMove(board, settings, cts.Token, candidateRescoreMarginCp,
                enableInstabilityTimeManagement: true);
            stopwatch.Stop();

            timing = new SearchTiming(stopwatch.Elapsed.TotalSeconds, cts.IsCancellationRequested,
                search.LastCompletedDepth);
            return best;
        }

        private void RunSingleMove(AIProfile profile, string positionName, BoardState board, int repeatIndex)
        {
            var engine = new ChessEngineAdapter();
            var search = BuildSearch(engine, profile);
            AISearchSettings settings = SingleMoveSettingsFor(profile);
            int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

            MoveCommand best = TimedSearch(search, board, settings, rescoreMargin, out SearchTiming timing);

            Emit($"[{profile.Id}] {positionName} single-move rep{repeatIndex + 1} depth {profile.MaxDepth}: {FormatTiming(timing)}, best={best} — {Verdict(timing.Seconds)}");
        }

        private void RunMultiMove(AIProfile profile, string positionName, BoardState board, int repeatIndex,
            int plyCount = DefaultPlyCount)
        {
            var engine = new ChessEngineAdapter();
            var search = BuildSearch(engine, profile);
            AISearchSettings settings = MultiMoveSettingsFor(profile);
            int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);
            var legalMoves = new List<MoveCommand>();

            for (int ply = 0; ply < plyCount; ply++)
            {
                // A profile's own blunder rate or a forced sequence can walk this position into
                // checkmate/stalemate before plyCount is reached — FindBestMove would then return
                // an empty default MoveCommand near-instantly (no root moves to search), which
                // used to print a bogus "0.00s PASS" and then feed that no-op move into ApplyMove
                // on the next iteration. Stop cleanly instead of reporting fake data.
                legalMoves.Clear();
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
                if (legalMoves.Count == 0)
                {
                    Emit($"[{profile.Id}] {positionName} multi-move rep{repeatIndex + 1} ply {ply + 1}/{plyCount}: game ended (checkmate/stalemate) — stopping early.");
                    break;
                }

                MoveCommand best = TimedSearch(search, board, settings, rescoreMargin, out SearchTiming timing);

                Emit($"[{profile.Id}] {positionName} multi-move rep{repeatIndex + 1} ply {ply + 1}/{plyCount}: {FormatTiming(timing)} — {Verdict(timing.Seconds)}");

                // DefendOnly means the search never hands us an Act at the root, so this simple
                // apply-and-flip loop can't wander into a Retribution sub-sequence it doesn't
                // model. If a staged move ever DOES appear here, that's a policy bug worth
                // surfacing loudly rather than silently corrupting the rest of the run.
                if (best.Stage != BetrayalStage.None)
                {
                    Emit($"[{profile.Id}] {positionName} multi-move rep{repeatIndex + 1} ply {ply + 1}/{plyCount}: UNEXPECTED staged move ({best.Stage}) under DefendOnly — aborting this cell.");
                    break;
                }

                Team mover = board.CurrentTurn;
                engine.ApplyMove(board, best);
                if (AlphaBetaSearch.StageFlipsTurn(best.Stage))
                    board.CurrentTurn = mover == Team.White ? Team.Black : Team.White;
            }
        }

        private static string FormatTiming(SearchTiming timing)
        {
            string cappedNote = timing.BudgetCapped ? " [budget-capped]" : "";
            string depthNote = timing.DepthReached > 0 ? $" (reached depth {timing.DepthReached})" : "";
            return $"{timing.Seconds:F2}s{cappedNote}{depthNote}";
        }

        private static string Verdict(double seconds) =>
            seconds < ThresholdSeconds ? "PASS (<6s)" : "FAIL (>=6s)";

        private void Emit(string line) => OnLine?.Invoke(line);

        /// <summary>
        /// Dumps every SystemInfo field useful for correlating a timing result with the actual
        /// hardware it ran on. Unity has no direct "chipset name" API — graphicsDeviceName is the
        /// closest honest proxy (a Mali GPU implies MediaTek/Exynos/Unisoc, an Adreno GPU implies
        /// Snapdragon), so it's included alongside deviceModel/processorType rather than guessed.
        /// Reads UnityEngine.SystemInfo/Screen directly (the one place this "Unity-free" class
        /// isn't) since there is no non-Unity way to learn this — kept isolated to this single
        /// method so the rest of the class stays trivially testable outside Play mode.
        /// </summary>
        private void EmitDeviceInfo()
        {
            Emit("--- Device info ---");
            Emit($"Device model: {UnityEngine.SystemInfo.deviceModel}");
            Emit($"Device name: {UnityEngine.SystemInfo.deviceName}");
            Emit($"Device unique ID: {UnityEngine.SystemInfo.deviceUniqueIdentifier}");
            Emit($"OS: {UnityEngine.SystemInfo.operatingSystem}");
            Emit($"CPU: {UnityEngine.SystemInfo.processorType} ({UnityEngine.SystemInfo.processorCount} cores, {UnityEngine.SystemInfo.processorFrequency}MHz)");
            Emit($"GPU (chipset proxy): {UnityEngine.SystemInfo.graphicsDeviceName} [{UnityEngine.SystemInfo.graphicsDeviceVendor}], API {UnityEngine.SystemInfo.graphicsDeviceType}");
            Emit($"RAM: {UnityEngine.SystemInfo.systemMemorySize}MB system, {UnityEngine.SystemInfo.graphicsMemorySize}MB graphics");
            Emit($"Battery: {UnityEngine.SystemInfo.batteryLevel * 100f:F0}% ({UnityEngine.SystemInfo.batteryStatus})");
            Emit($"Screen: {UnityEngine.Screen.width}x{UnityEngine.Screen.height} @ {UnityEngine.Screen.dpi}dpi");
        }

        /// <summary>One search's outcome: elapsed wall-clock time, whether the soft time budget cut
        /// it off before MaxDepth, and the deepest iterative-deepening depth it fully completed —
        /// the only field that still distinguishes two runs which both hit the same budget cap
        /// (their elapsed seconds are then identical by construction, but the depth reached is
        /// not).</summary>
        private readonly struct SearchTiming
        {
            public readonly double Seconds;
            public readonly bool BudgetCapped;
            public readonly int DepthReached;

            public SearchTiming(double seconds, bool budgetCapped, int depthReached)
            {
                Seconds = seconds;
                BudgetCapped = budgetCapped;
                DepthReached = depthReached;
            }
        }
    }
}
