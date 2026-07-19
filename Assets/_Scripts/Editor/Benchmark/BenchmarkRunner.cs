using System.Collections.Generic;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Batch entry point over TournamentSession: drains a whole tournament in one blocking call and
    /// returns the report — the shape a menu command or a CI -executeMethod invocation wants. The
    /// interactive tournament window drives the same TournamentSession one game per editor tick
    /// instead; both paths share the session (and its seeding) verbatim, so they play the same
    /// pairings from the same positions with the same RNG streams. See TournamentSession's own
    /// doc comment for the one caveat time-budgeted searches put on exact outcome reproduction.
    ///
    /// Dev/editor-only. Nothing in Core, AI, or the shipped player build ever references this —
    /// it's the same category of tool as the opening-book compiler, not a player-facing feature.
    /// </summary>
    public static class BenchmarkRunner
    {
        public static BenchmarkReport RunAll(int runSeed, BenchmarkMode mode) =>
            RunAll(runSeed, mode, AIProfileTable.BuiltIn, MatchSimulator.DefaultPlyCap);

        /// <summary>
        /// Same tournament shape as the two-argument overload, but resolves profile ids against
        /// <paramref name="roster"/> instead of the shipped built-in table, and caps each game at
        /// <paramref name="plyCap"/> plies. Exists so tests can exercise the tournament contract
        /// (pairing count, reproducibility, report shape) against fast, shallow fixture profiles
        /// with a tight ply cap, without paying the real tournament's search-depth-and-length cost
        /// — the real tournament (against AIProfileTable.BuiltIn, full ply cap) is only ever run
        /// from the menu/batchmode/window entry points.
        /// </summary>
        public static BenchmarkReport RunAll(int runSeed, BenchmarkMode mode,
            IReadOnlyList<AIProfile> roster, int plyCap = MatchSimulator.DefaultPlyCap)
        {
            TournamentSession session = mode == BenchmarkMode.Quick
                ? TournamentSession.CreateQuick(runSeed, roster, plyCap)
                : TournamentSession.CreateFull(runSeed, roster, plyCap);

            while (session.RunNextGame()) { }

            return session.BuildReport();
        }
    }
}
