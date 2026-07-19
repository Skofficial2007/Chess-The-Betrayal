namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Reports live progress out of a tournament/benchmark run — the fix for the exact failure
    /// mode that made this tooling unusable: a run stalled for 30+ minutes was indistinguishable
    /// from a run genuinely still working, because nothing printed anything until the whole thing
    /// finished. Every entry point that can run long (BenchmarkMenu's batch commands, the explicit
    /// strength-ordering suite, the interactive window) should report through one of these instead
    /// of staying silent.
    /// </summary>
    public interface ITournamentProgress
    {
        /// <summary>Called once per completed game. current/total are 1-based/total game counts —
        /// current == total on the last call.</summary>
        void ReportGameCompleted(int current, int total);
    }

    /// <summary>Reports nothing. The default for call sites that don't care — explicit, not
    /// implicit null-checking scattered through the run loop.</summary>
    public sealed class NullTournamentProgress : ITournamentProgress
    {
        public static readonly NullTournamentProgress Instance = new NullTournamentProgress();
        public void ReportGameCompleted(int current, int total) { }
    }
}
