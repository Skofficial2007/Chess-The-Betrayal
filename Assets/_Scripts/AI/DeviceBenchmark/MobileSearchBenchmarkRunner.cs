using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.AI.Agent;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// Unity-free benchmark logic: for one (position, tier, repeat) cell, times a single cold search
    /// and a several-ply play-forward from the same position. Deliberately has no dependency on
    /// MonoBehaviour or UnityEngine.Debug — those are presentation concerns owned by
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
    /// A diagnostic tool, not shipped gameplay code, so it allocates freely (plain string
    /// concatenation, a List&lt;MoveCommand&gt; per run) rather than avoiding allocation the way
    /// AlphaBetaSearch and the transposition table have to. A benchmark cell runs once every few
    /// seconds at most, never inside the search loop itself, so an allocation here was never on a
    /// path anyone could feel.
    /// </summary>
    public sealed class MobileSearchBenchmarkRunner
    {
        internal const int DefaultPlyCount = 4;
        public const int DefaultRepeatCount = 3;

        // Production always searches on a thread-pool worker (AsyncAIAgent's Task.Run) while the
        // main thread renders; a benchmark that only ever measures the main thread never actually
        // learns whether that hop costs anything on a given device's scheduler. Both a real cell
        // and a summary line carry one of these two labels so the two thread contexts are never
        // silently averaged together.
        internal const string MainThreadLabel = "main-thread";
        internal const string WorkerThreadLabel = "worker-thread";

        // Keyed by (profile id, thread label), populated as cells run. Kept alongside the results
        // themselves (rather than requiring the caller to collect every emitted line) so a
        // per-tier summary can be produced at any point in a run — complete or not — without
        // re-parsing text. RunCell and RunCellOnWorkerThread are always awaited/sequenced by the
        // caller rather than run concurrently (see RunCellOnWorkerThread's doc comment), so this
        // dictionary never needs its own lock.
        private readonly Dictionary<(string ProfileId, string ThreadLabel), List<SearchTiming>> _timingsByKey =
            new Dictionary<(string, string), List<SearchTiming>>();

        // Started when the runner is built, which happens once per run (see
        // DeviceSearchBenchmark.RunAll) — so "elapsed since this started" means the same thing as
        // "elapsed since the run started" without the runner needing to be told when the run began.
        // Stamping every timing against it is what lets EmitThermalBuckets group samples by which
        // minute of the run they landed in.
        private readonly Stopwatch _runElapsed = Stopwatch.StartNew();

        // Read from the agent rather than copied, so the two cannot drift apart the way a second
        // literal would. AlphaBetaSearch's own default (log2Size: 16, ~1 MB) exists for lightweight
        // callers that do not care about move-ordering quality — a real match never uses it, so
        // measuring against it understates how deep a device actually searches.
        internal const int ProductionTranspositionTableLog2Size =
            AsyncAIAgent.ProductionTranspositionTableLog2Size;

        /// <summary>How many distinct positions a cell index can address: every curated opening
        /// line plus the two hand-placed positions in DepthWallPositions.</summary>
        public static int PositionCount => CuratedOpeningLines.Count + 2;

        /// <summary>One line of benchmark output, already formatted — the caller (a MonoBehaviour,
        /// a test, a console app) decides where it goes (Debug.Log, a scrolling label, stdout).</summary>
        public event Action<string> OnLine;

        /// <summary>A single, unmistakable, greppable line — `adb logcat | grep
        /// BENCHMARK_RUN_COMPLETE` (or eyeballing the on-screen log) tells you unambiguously that
        /// every cell ran to completion, as opposed to the app having merely gone idle, frozen or
        /// crashed silently. Just a string: device info and the start/completion narration around
        /// it are DeviceSearchBenchmark's job, the one place in this feature that legitimately
        /// touches Unity APIs.</summary>
        public const string CompletionMarker = "===BENCHMARK_RUN_COMPLETE===";

        /// <summary>
        /// Runs one (position, tier, repeat) cell on the calling thread: a single cold search plus
        /// a short play-forward from the same starting position, both freshly built. This is the
        /// main-thread control -- the same cell run through RunCellOnWorkerThread is the one that
        /// matches how a real match actually dispatches a search. The caller supplies the already
        /// fully-resolved profile (guardrails applied) and is responsible for looping over
        /// positions/tiers/repeats and yielding between cells if it wants incremental UI updates
        /// (see DeviceSearchBenchmark.RunAll's coroutine wrapper).
        /// </summary>
        public void RunCell(int positionIndex, AIProfile profile, int repeatIndex, bool includePlayForward = true) =>
            RunCellCore(positionIndex, profile, repeatIndex, MainThreadLabel, includePlayForward);

        /// <summary>
        /// Runs the same (position, tier, repeat) cell AsyncAIAgent's own way: dispatched onto a
        /// thread-pool worker via Task.Run, never the calling thread. Returns the Task rather than
        /// blocking so a coroutine caller can wait on it (poll IsCompleted across yields) without
        /// stalling Unity's main thread while it runs. The caller must let this complete before
        /// starting another cell against the same runner instance -- nothing here is guarded for
        /// concurrent access, because a real match never runs two searches against the same
        /// runner at once either.
        /// </summary>
        public Task RunCellOnWorkerThread(int positionIndex, AIProfile profile, int repeatIndex,
            bool includePlayForward = true) =>
            Task.Run(() => RunCellCore(positionIndex, profile, repeatIndex, WorkerThreadLabel, includePlayForward));

        private void RunCellCore(int positionIndex, AIProfile profile, int repeatIndex, string threadLabel,
            bool includePlayForward)
        {
            string positionName = PositionName(positionIndex);
            RunSingleMove(profile, positionName, BuildPosition(positionIndex), repeatIndex, threadLabel);

            if (includePlayForward)
                RunMultiMove(profile, positionName, BuildPosition(positionIndex), repeatIndex, threadLabel);
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
            new AlphaBetaSearch(engine, EvaluatorFor(profile),
                transpositionTable: new TranspositionTable(log2Size: ProductionTranspositionTableLog2Size));

        /// <summary>
        /// The evaluator a profile actually plays with, weighted by its own dials. Split out so it
        /// can be scored directly: every tier used to be measured against the identity evaluator,
        /// which made an aggressive tier's numbers describe a player nobody faces.
        /// </summary>
        internal static IPositionEvaluator EvaluatorFor(AIProfile profile) =>
            new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(profile));

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
        private MoveCommand TimedSearch(AlphaBetaSearch search, BoardState board,
            AISearchSettings settings, int candidateRescoreMarginCp, out SearchTiming timing)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(settings.TimeBudget.HardMs);

            var stopwatch = Stopwatch.StartNew();
            MoveCommand best = search.FindBestMove(board, settings, cts.Token, candidateRescoreMarginCp,
                enableInstabilityTimeManagement: true);
            stopwatch.Stop();

            timing = new SearchTiming(stopwatch.Elapsed.TotalSeconds, cts.IsCancellationRequested,
                search.LastCompletedDepth, settings.TimeBudget.HardMs, _runElapsed.Elapsed.TotalMilliseconds);
            return best;
        }

        internal void RecordTiming(string profileId, string threadLabel, SearchTiming timing)
        {
            var key = (profileId, threadLabel);
            if (!_timingsByKey.TryGetValue(key, out List<SearchTiming> samples))
            {
                samples = new List<SearchTiming>();
                _timingsByKey[key] = samples;
            }

            samples.Add(timing);
        }

        /// <summary>
        /// Builds two lines per tier, one per thread context, so a main-thread run and a
        /// worker-thread run on the same tier can be read side by side rather than blended into one
        /// number: how many samples each has, the worst/mean/min elapsed time, the worst overshoot
        /// past its own budget, and the worst/mean depth reached. A combination with zero samples
        /// still gets a line saying so, rather than being left out — an absent line reads as
        /// "nothing to report" when it might just mean the run was interrupted before reaching it,
        /// which is exactly the distinction this line exists to make clear.
        ///
        /// Which tiers to account for has to be passed in, because reporting absence only says
        /// something against the tiers a run was actually meant to cover. Enumerating every built-in
        /// tier regardless put eleven "no samples recorded" lines above the single real one on the
        /// first single-tier run that reached a device, which reads at a glance like a run that
        /// failed rather than one that was never asked to sweep. The same reasoning applies to the
        /// main-thread control, which some plans deliberately skip: includeMainThreadControl is
        /// false for those, so they stop reporting an absence nobody was owed.
        ///
        /// Safe to call at any point, since it only ever reports on whatever has been recorded so
        /// far. Also emits each line the usual way (for adb logcat / a plain console listener);
        /// returning them too lets a caller assembling a structured report (see BenchmarkReport)
        /// place the summary as its own section rather than re-parsing the general line stream.
        /// </summary>
        public IReadOnlyList<string> EmitTierSummaries(IReadOnlyList<AIProfile> profiles,
            bool includeMainThreadControl = true)
        {
            var lines = new List<string>();

            foreach (AIProfile profile in profiles)
            {
                if (includeMainThreadControl)
                    lines.Add(EmitTierSummaryLine(profile, MainThreadLabel));

                lines.Add(EmitTierSummaryLine(profile, WorkerThreadLabel));
            }

            return lines;
        }

        private string EmitTierSummaryLine(AIProfile profile, string threadLabel)
        {
            if (!_timingsByKey.TryGetValue((profile.Id, threadLabel), out List<SearchTiming> samples) || samples.Count == 0)
            {
                string noSamplesLine = $"[{profile.Id} {threadLabel}] no samples recorded.";
                Emit(noSamplesLine);
                return noSamplesLine;
            }

            double worstSeconds = samples[0].Seconds;
            double minSeconds = samples[0].Seconds;
            double sumSeconds = 0;
            int worstDepth = samples[0].DepthReached;
            int sumDepth = 0;
            int worstOvershootMs = samples[0].OvershootMsRounded;

            foreach (SearchTiming timing in samples)
            {
                if (timing.Seconds > worstSeconds) worstSeconds = timing.Seconds;
                if (timing.Seconds < minSeconds) minSeconds = timing.Seconds;
                sumSeconds += timing.Seconds;

                if (timing.DepthReached < worstDepth) worstDepth = timing.DepthReached;
                sumDepth += timing.DepthReached;

                if (timing.OvershootMsRounded > worstOvershootMs) worstOvershootMs = timing.OvershootMsRounded;
            }

            double meanSeconds = sumSeconds / samples.Count;
            double meanDepth = (double)sumDepth / samples.Count;
            string overshootNote = worstOvershootMs > 0 ? $"+{worstOvershootMs}ms" : "none";

            string line = $"[{profile.Id} {threadLabel}] {samples.Count} samples: elapsed worst {worstSeconds:F2}s mean {meanSeconds:F2}s min {minSeconds:F2}s "
                + $"(budget {profile.TimeBudget.HardMs}ms, worst overshoot {overshootNote}); depth worst {worstDepth} mean {meanDepth:F1}";
            Emit(line);
            return line;
        }

        private const int ThermalBucketMs = 60_000;

        /// <summary>
        /// One line per minute of wall-clock elapsed since this runner started, for every
        /// (tier, thread) combination that actually recorded a sample — how many searches landed
        /// in that minute and how deep they reached. EmitTierSummaries answers "how deep does this
        /// tier get" as a single worst/mean number for the whole run; that cannot show a depth that
        /// holds for the first few minutes and then quietly drops as a device heats up, only a value
        /// grouped by when each sample happened can. Unlike EmitTierSummaries there is no fixed
        /// universe of buckets to report absence against, so a combination with no samples at all is
        /// skipped rather than getting a placeholder line — a run that never reached minute 4 simply
        /// has no minute-4 line, which already reads as "nothing happened then."
        /// </summary>
        public IReadOnlyList<string> EmitThermalBuckets()
        {
            var lines = new List<string>();

            var keys = new List<(string ProfileId, string ThreadLabel)>(_timingsByKey.Keys);
            keys.Sort((a, b) =>
            {
                int byProfile = string.Compare(a.ProfileId, b.ProfileId, StringComparison.Ordinal);
                return byProfile != 0 ? byProfile : string.Compare(a.ThreadLabel, b.ThreadLabel, StringComparison.Ordinal);
            });

            foreach ((string profileId, string threadLabel) in keys)
            {
                List<SearchTiming> samples = _timingsByKey[(profileId, threadLabel)];

                var byMinute = new SortedDictionary<int, List<SearchTiming>>();
                foreach (SearchTiming timing in samples)
                {
                    int minute = (int)(timing.ElapsedSinceRunStartMs / ThermalBucketMs);
                    if (!byMinute.TryGetValue(minute, out List<SearchTiming> bucket))
                    {
                        bucket = new List<SearchTiming>();
                        byMinute[minute] = bucket;
                    }

                    bucket.Add(timing);
                }

                foreach (KeyValuePair<int, List<SearchTiming>> entry in byMinute)
                    lines.Add(EmitThermalBucketLine(profileId, threadLabel, entry.Key, entry.Value));
            }

            return lines;
        }

        private string EmitThermalBucketLine(string profileId, string threadLabel, int minute, List<SearchTiming> samples)
        {
            double sumSeconds = 0;
            int worstDepth = samples[0].DepthReached;
            int sumDepth = 0;

            foreach (SearchTiming timing in samples)
            {
                sumSeconds += timing.Seconds;
                if (timing.DepthReached < worstDepth) worstDepth = timing.DepthReached;
                sumDepth += timing.DepthReached;
            }

            double meanSeconds = sumSeconds / samples.Count;
            double meanDepth = (double)sumDepth / samples.Count;

            string line = $"[{profileId} {threadLabel}] minute {minute}: {samples.Count} samples, elapsed mean {meanSeconds:F2}s; "
                + $"depth worst {worstDepth} mean {meanDepth:F1}";
            Emit(line);
            return line;
        }

        private void RunSingleMove(AIProfile profile, string positionName, BoardState board, int repeatIndex,
            string threadLabel)
        {
            var engine = new ChessEngineAdapter();
            var search = BuildSearch(engine, profile);
            AISearchSettings settings = SingleMoveSettingsFor(profile);
            int rescoreMargin = profile.RescoreMarginCp;

            MoveCommand best = TimedSearch(search, board, settings, rescoreMargin, out SearchTiming timing);
            RecordTiming(profile.Id, threadLabel, timing);

            Emit($"[{profile.Id} {threadLabel}] {positionName} single-move rep{repeatIndex + 1} depth {profile.MaxDepth}: {FormatTiming(timing)}, best={best} - {BudgetNote(timing)}");
        }

        private void RunMultiMove(AIProfile profile, string positionName, BoardState board, int repeatIndex,
            string threadLabel, int plyCount = DefaultPlyCount)
        {
            var engine = new ChessEngineAdapter();
            var search = BuildSearch(engine, profile);
            AISearchSettings settings = MultiMoveSettingsFor(profile);
            int rescoreMargin = profile.RescoreMarginCp;
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
                    Emit($"[{profile.Id} {threadLabel}] {positionName} multi-move rep{repeatIndex + 1} ply {ply + 1}/{plyCount}: game ended (checkmate/stalemate) - stopping early.");
                    break;
                }

                MoveCommand best = TimedSearch(search, board, settings, rescoreMargin, out SearchTiming timing);
                RecordTiming(profile.Id, threadLabel, timing);

                Emit($"[{profile.Id} {threadLabel}] {positionName} multi-move rep{repeatIndex + 1} ply {ply + 1}/{plyCount}: {FormatTiming(timing)} - {BudgetNote(timing)}");

                // DefendOnly means the search never hands us an Act at the root, so this simple
                // apply-and-flip loop can't wander into a Retribution sub-sequence it doesn't
                // model. If a staged move ever DOES appear here, that's a policy bug worth
                // surfacing loudly rather than silently corrupting the rest of the run.
                if (best.Stage != BetrayalStage.None)
                {
                    Emit($"[{profile.Id} {threadLabel}] {positionName} multi-move rep{repeatIndex + 1} ply {ply + 1}/{plyCount}: UNEXPECTED staged move ({best.Stage}) under DefendOnly - aborting this cell.");
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

        /// <summary>
        /// How one search sat against its own tier's budget, not some fixed number — a six-second
        /// ceiling means nothing when every tier is already cut off at three seconds or less.
        ///
        /// States the overshoot as a quantity and leaves the judging to the per-tier summary. The
        /// timer that cancels a search has a resolution of its own — a few milliseconds on a phone,
        /// around fifteen on Windows — so a line-by-line pass/fail word would be putting a property
        /// of the clock in front of a reader as a failure of the search, on nearly every line of a
        /// long run. The magnitude is still printed in full, and a device that misses by 2 ms and
        /// one that misses by 2 seconds still read nothing alike.
        /// </summary>
        internal static string BudgetNote(SearchTiming timing) =>
            timing.OvershootMsRounded > 0
                ? $"+{timing.OvershootMsRounded}ms past budget ({timing.HardMs}ms)"
                : $"within budget ({timing.HardMs}ms)";

        private void Emit(string line) => OnLine?.Invoke(line);

        /// <summary>One search's outcome: elapsed wall-clock time, whether the soft time budget cut
        /// it off before MaxDepth, the deepest iterative-deepening depth it fully completed — the
        /// only field that still distinguishes two runs which both hit the same budget cap (their
        /// elapsed seconds are then identical by construction, but the depth reached is not) — and
        /// the tier's own hard budget in milliseconds, which is what OvershootMs measures against.
        /// ElapsedSinceRunStartMs defaults to zero because most callers (every existing test) have no
        /// stake in when during a run a sample landed; only EmitThermalBuckets reads it, and it is
        /// only ever non-zero when a real run stamps it via TimedSearch.
        /// </summary>
        internal readonly struct SearchTiming
        {
            public readonly double Seconds;
            public readonly bool BudgetCapped;
            public readonly int DepthReached;
            public readonly int HardMs;
            public readonly double ElapsedSinceRunStartMs;

            public SearchTiming(double seconds, bool budgetCapped, int depthReached, int hardMs,
                double elapsedSinceRunStartMs = 0)
            {
                Seconds = seconds;
                BudgetCapped = budgetCapped;
                DepthReached = depthReached;
                HardMs = hardMs;
                ElapsedSinceRunStartMs = elapsedSinceRunStartMs;
            }

            /// <summary>How far past this search's own budget the elapsed time actually landed.
            /// Zero or negative means it finished inside the budget; the search only checks for
            /// cancellation at node boundaries, so a positive value is how late that check came,
            /// not a sign the cancellation itself was late to fire.</summary>
            public double OvershootMs => (Seconds * 1000.0) - HardMs;

            /// <summary>
            /// The overshoot as a report prints it. Everything that decides whether a search went
            /// past its budget reads this rather than <see cref="OvershootMs"/>, so the question is
            /// settled by the same number the reader is shown. A search landing 0.3 ms late is past
            /// its budget by any exact measure and renders as "0ms" at the precision a report uses,
            /// and a page full of lines claiming an overshoot of zero teaches whoever reads it to
            /// stop believing the lines that mean something.
            /// </summary>
            public int OvershootMsRounded => (int)Math.Round(OvershootMs, MidpointRounding.AwayFromZero);
        }
    }
}
