using System;
using System.Collections.Generic;
using ChessTheBetrayal.AI.Positions;

namespace ChessTheBetrayal.AI.DeviceBenchmark
{
    /// <summary>
    /// Which cells a run covers, and how long it can take at worst.
    ///
    /// The worst case is knowable in advance and is the same on every device, which is the whole
    /// reason this type exists. Each search is stopped by a wall-clock timer rather than by
    /// finishing its work, so a slower phone does not spend longer on a search — it reaches a
    /// shallower depth in the same milliseconds. Adding up every search's own hard budget therefore
    /// gives a ceiling that holds on the fastest desktop and the slowest phone alike, and a run can
    /// honestly promise its duration before it starts. A weak device trends toward that ceiling
    /// (fewer of its searches finish early enough to stop on their own) but never past it.
    /// </summary>
    public sealed class BenchmarkPlan
    {
        /// <summary>
        /// The default a tester's build runs. Small enough to actually get finished on a phone
        /// someone is holding, which matters more than breadth: a thorough run nobody completes
        /// measures nothing at all.
        ///
        /// Cold single searches only, no play-forward. A search from a cold start — empty
        /// transposition table, no move ordering carried in from a previous ply — is the slowest,
        /// least favourable case, and the per-move promise is exactly about that case. Playing
        /// several moves forward reuses a warm table and so runs easier; it belongs in the
        /// exhaustive run, not in the one measuring whether the promise holds.
        /// </summary>
        public static BenchmarkPlan Tester() => new BenchmarkPlan(
            name: "Quick Run",
            positionIndices: TesterPositionIndices(),
            repeatCount: 2,
            includePlayForward: false,
            mainThreadControlPositions: 1,
            mainThreadControlRepeats: 1,
            profiles: AIProfileTable.BuiltIn);

        /// <summary>
        /// Every position, every repeat, both a cold search and a play-forward, on both thread
        /// contexts. Hours long — for a machine you own and can leave running, never for a build
        /// handed to someone else.
        /// </summary>
        public static BenchmarkPlan Exhaustive() => new BenchmarkPlan(
            name: "Exhaustive Run",
            positionIndices: AllPositionIndices(),
            repeatCount: MobileSearchBenchmarkRunner.DefaultRepeatCount,
            includePlayForward: true,
            mainThreadControlPositions: MobileSearchBenchmarkRunner.PositionCount,
            mainThreadControlRepeats: MobileSearchBenchmarkRunner.DefaultRepeatCount,
            profiles: AIProfileTable.BuiltIn);

        /// <summary>
        /// How many cold searches this run repeats at the impossible tier. Public so a test can pin
        /// the arithmetic behind <see cref="EstimatedWorstCase"/> rather than re-deriving it.
        /// </summary>
        public const int ThermalSearchCount = 200;

        /// <summary>
        /// A sustained-load probe, not a breadth probe: the same stable midgame position searched
        /// cold 200 times in a row, at the impossible tier alone, worker-thread only. Real thermal
        /// throttling shows up as depth quietly dropping ten minutes into a run, and nothing about
        /// varying the position or sweeping every tier would make that easier to see — it would only
        /// blend the one signal this run exists to isolate across unrelated tiers and openings.
        /// Skips the main-thread control for the same reason: production always dispatches a search
        /// onto a worker, so that is the only context whose sustained behaviour matters here.
        /// </summary>
        public static BenchmarkPlan Thermal() => new BenchmarkPlan(
            name: "Long Run (sustained load)",
            positionIndices: new[] { CuratedOpeningLines.Count },
            repeatCount: ThermalSearchCount,
            includePlayForward: false,
            mainThreadControlPositions: 0,
            mainThreadControlRepeats: 0,
            profiles: new[] { ImpossibleProfile() });

        private static AIProfile ImpossibleProfile()
        {
            foreach (AIProfile profile in AIProfileTable.BuiltIn)
                if (profile.Id == "impossible") return profile;

            throw new InvalidOperationException("AIProfileTable.BuiltIn has no 'impossible' tier.");
        }

        private BenchmarkPlan(string name, IReadOnlyList<int> positionIndices, int repeatCount,
            bool includePlayForward, int mainThreadControlPositions, int mainThreadControlRepeats,
            IReadOnlyList<AIProfile> profiles)
        {
            Name = name;
            PositionIndices = positionIndices;
            RepeatCount = repeatCount;
            IncludePlayForward = includePlayForward;
            MainThreadControlPositions = mainThreadControlPositions;
            MainThreadControlRepeats = mainThreadControlRepeats;
            Profiles = profiles;
        }

        /// <summary>
        /// What this run is, in the words the button that starts it uses. A report that only counts
        /// its cells describes 200 repeats of one position exactly the way it would describe a
        /// matrix of 200 different ones, and whoever reads the file later has no way to tell which
        /// they were sent.
        /// </summary>
        public string Name { get; }

        /// <summary>Positions this run covers, by the index MobileSearchBenchmarkRunner uses.</summary>
        public IReadOnlyList<int> PositionIndices { get; }

        /// <summary>The tiers this run sweeps. <see cref="Tester"/> and <see cref="Exhaustive"/>
        /// cover every built-in tier; <see cref="Thermal"/> narrows this to one, since sweeping the
        /// rest would only dilute the sustained-load signal it exists to isolate.</summary>
        public IReadOnlyList<AIProfile> Profiles { get; }

        public int RepeatCount { get; }

        /// <summary>Whether each cell also plays several moves forward after its cold search.</summary>
        public bool IncludePlayForward { get; }

        /// <summary>
        /// How many positions also get run on the calling thread as a control. Production always
        /// searches on a worker, so the worker pass is the real measurement; the main-thread pass
        /// exists only to show whether a given device's scheduler treats the two differently, and a
        /// couple of cells answer that as well as a full second matrix would.
        /// </summary>
        public int MainThreadControlPositions { get; }

        public int MainThreadControlRepeats { get; }

        /// <summary>Whether this run measures the main thread at all. A plan that doesn't should
        /// not be reported against it — an absence only says something where something was
        /// expected.</summary>
        public bool HasMainThreadControl => ControlPositionCount > 0 && MainThreadControlRepeats > 0;

        /// <summary>
        /// Whether a per-minute depth trend from this run can be read as heat. Only a run that
        /// repeats one position at one tier can: every sample then did identical work, so a change
        /// across minutes is a change in the device. Vary either and each minute contains whatever
        /// cells happened to fall in it, and the trend tracks the running order instead — the
        /// tester plan's curve climbs a ply and a half between its first two minutes purely because
        /// the deeper-searching positions come second, which reads as a phone speeding up as it
        /// warms.
        /// </summary>
        public bool CurveTracksSustainedLoad => PositionIndices.Count == 1 && Profiles.Count == 1;

        /// <summary>
        /// The run's shape in one line. A cell count on its own cannot separate breadth from
        /// repetition, and the two answer entirely different questions.
        /// </summary>
        public string Describe()
        {
            string positions = PositionIndices.Count == 1 ? "1 position" : $"{PositionIndices.Count} positions";
            string tiers = Profiles.Count == 1 ? $"the {Profiles[0].Id} tier alone" : $"{Profiles.Count} tiers";
            string repeats = RepeatCount == 1 ? "once each" : $"{RepeatCount} times each";
            string playForward = IncludePlayForward ? ", cold search plus play-forward" : ", cold search only";
            string control = HasMainThreadControl
                ? $", plus a main-thread control on {ControlPositionCount} of them"
                : ", worker thread only";

            return $"Plan: {Name} - {positions} x {tiers}, {repeats}{playForward}{control}";
        }

        /// <summary>Worker-thread cells plus main-thread control cells.</summary>
        public int TotalCells =>
            (PositionIndices.Count * Profiles.Count * RepeatCount)
            + (ControlPositionCount * Profiles.Count * MainThreadControlRepeats);

        /// <summary>
        /// Every cell this run covers, in the order it runs them: the whole worker-thread pass
        /// first, then the smaller main-thread control. Position outermost, tier next, repeat
        /// innermost, so a run interrupted partway through still has complete cross-tier coverage
        /// on however many positions finished rather than one tier finishing everything.
        ///
        /// Exists so the on-device screen and the desktop recording harness cannot describe a run
        /// differently — they walk this, rather than each writing out the same three nested loops.
        /// Yields the profile's id rather than the profile itself because the caller is the one
        /// that resolves it through the provider a real match uses.
        /// </summary>
        public IEnumerable<BenchmarkCell> Cells()
        {
            foreach (int positionIndex in PositionIndices)
                foreach (AIProfile profile in Profiles)
                    for (int repeatIndex = 0; repeatIndex < RepeatCount; repeatIndex++)
                        yield return new BenchmarkCell(positionIndex, profile.Id, repeatIndex, onWorkerThread: true);

            for (int i = 0; i < ControlPositionCount; i++)
                foreach (AIProfile profile in Profiles)
                    for (int repeatIndex = 0; repeatIndex < MainThreadControlRepeats; repeatIndex++)
                        yield return new BenchmarkCell(PositionIndices[i], profile.Id, repeatIndex, onWorkerThread: false);
        }

        // Asking for more control positions than the run actually covers would count cells that
        // can never happen, so the requested number is capped at what is there. Both the cell
        // count and the duration estimate read this rather than the raw field, so neither can
        // disagree with the cells that actually run.
        private int ControlPositionCount => Math.Min(MainThreadControlPositions, PositionIndices.Count);

        /// <summary>
        /// The longest this run can take on any device — every search burning its whole budget and
        /// none of them stopping early. Safe to quote to whoever is about to sit through it.
        /// </summary>
        public TimeSpan EstimatedWorstCase
        {
            get
            {
                long budgetSumMs = 0;
                foreach (AIProfile profile in Profiles)
                    budgetSumMs += profile.TimeBudget.HardMs;

                // A cell is one cold search, plus one search per played-forward ply when the plan
                // asks for them.
                int searchesPerCell = IncludePlayForward ? 1 + MobileSearchBenchmarkRunner.DefaultPlyCount : 1;

                long workerMs = (long)PositionIndices.Count * RepeatCount * budgetSumMs * searchesPerCell;
                long controlMs = (long)ControlPositionCount * MainThreadControlRepeats * budgetSumMs * searchesPerCell;

                return TimeSpan.FromMilliseconds(workerMs + controlMs);
            }
        }

        /// <summary>
        /// Both hand-placed depth probes, plus two curated openings taken from opposite ends of the
        /// set. The hand-placed pair is built to grow expensive with depth; the openings are drawn
        /// from the positions a real game's budget was actually measured running short on, and
        /// taking one from each end avoids reading four neighbouring lines of the same opening as
        /// though they were four independent samples.
        /// </summary>
        private static IReadOnlyList<int> TesterPositionIndices() => new[]
        {
            0,
            CuratedOpeningLines.Count / 2,
            CuratedOpeningLines.Count,
            CuratedOpeningLines.Count + 1,
        };

        private static IReadOnlyList<int> AllPositionIndices()
        {
            var indices = new int[MobileSearchBenchmarkRunner.PositionCount];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            return indices;
        }
    }
}
