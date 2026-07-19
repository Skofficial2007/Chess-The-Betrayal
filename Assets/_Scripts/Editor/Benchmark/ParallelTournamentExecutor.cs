using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Plays a TournamentSession's remaining games across several worker threads instead of one at
    /// a time on the caller's own thread — the lever that turns a benchmark/tournament run's cost
    /// from "sum of every game's search time" into "sum of every game's search time divided by
    /// worker count." Each game is an independent simulated match (its own board clone, its own
    /// searches, its own RNG streams), so there is no shared mutable state between them to
    /// synchronize — this is embarrassingly parallel work, not a algorithm needing a lock.
    ///
    /// Determinism is preserved exactly: games are PLAYED out of order across threads (whichever
    /// worker gets to game 7 first plays it first), but every result is folded into the session's
    /// tallies/tier accumulators sequentially, in the session's own original game order, on the
    /// thread that calls RunRemainingGames. A parallel run's BenchmarkReport is therefore
    /// byte-identical to a sequential session.RunNextGame() loop's report at the same seed —
    /// ParallelTournamentExecutorTests pins exactly this.
    ///
    /// Not Burst/Jobs: game-level parallelism here needs managed object graphs (BoardState, List
    /// buffers, interface dispatch) that C# Jobs forbid outright, and Phase 4's own profiling
    /// showed this search is branch-bound rather than throughput-bound — Burst doesn't attack that
    /// bottleneck. Plain .NET tasks give near-linear scaling on this genuinely parallel workload
    /// with zero engine changes, which is what actually moves the wall-clock number.
    /// </summary>
    public static class ParallelTournamentExecutor
    {
        /// <summary>Leaves a couple of cores free for the OS/editor itself, same reasoning a build
        /// pipeline or test runner uses — pegging every core stalls everything else on the
        /// machine, including the editor UI thread driving this run. Clamped to at least 1 so a
        /// dual-core machine still gets real (if minimal) parallelism instead of throwing.</summary>
        public static int DefaultMaxDegreeOfParallelism =>
            Math.Max(1, Math.Min(16, Environment.ProcessorCount - 2));

        /// <summary>
        /// Plays every game the session has not yet played, across up to maxDegreeOfParallelism
        /// worker threads, then folds each result into the session in original order — see this
        /// type's own doc comment for why that ordering is what keeps the result deterministic.
        /// Blocks the calling thread until every game is played or cancellationToken fires; a
        /// cancellation stops launching NEW games but does not abort games already in flight (an
        /// AlphaBetaSearch mid-recursion has no cooperative cancellation point to hand a token to
        /// today — it currently reads a token only at the top of FindBestMove's depth loop and, for
        /// MatchTimeControl.ProductionBudget games, at that game's own hard time budget).
        /// onGamePlayed reports raw completion count as games finish across all workers (fires from
        /// a worker thread, out of order) — separate from the session's own OnGameCompleted (fires
        /// from the caller's thread, in order) so a progress bar can show live movement without a
        /// caller needing to reason about thread affinity.
        /// </summary>
        public static void RunRemainingGames(
            TournamentSession session,
            int maxDegreeOfParallelism = -1,
            CancellationToken cancellationToken = default,
            Action<int, int> onGamePlayed = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (maxDegreeOfParallelism <= 0) maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism;

            int totalPending = session.PendingGameCount;
            if (totalPending == 0) return;

            // Every worker gets its OWN MatchSimulator (its own pooled transposition tables, its
            // own AlphaBetaSearch/MoveSelectionPolicy instances get built fresh per game inside
            // PlayOneGame) rather than sharing across threads — MatchSimulator's own doc comment
            // is explicit that one instance is not thread-safe. ThreadLocal with trackAllValues so
            // every simulator this run ever created can be disposed-equivalent (GC'd) once the run
            // ends, rather than leaking one per pool thread indefinitely across many runs.
            MatchTimeControl timeControl = session.TimeControl;
            using (var threadLocalSimulator = new ThreadLocal<MatchSimulator>(() => new MatchSimulator(timeControl), trackAllValues: true))
            {
                var results = new TournamentGameRecord[totalPending];
                int startIndex = session.GamesCompleted;
                int completedSoFar = 0;

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                };

                try
                {
                    Parallel.For(0, totalPending, options, offset =>
                    {
                        MatchSimulator simulator = threadLocalSimulator.Value;
                        results[offset] = session.PlayOneGame(simulator, startIndex + offset);

                        int completed = Interlocked.Increment(ref completedSoFar);
                        onGamePlayed?.Invoke(completed, totalPending);
                    });
                }
                catch (OperationCanceledException)
                {
                    // Expected when the caller cancels mid-run — fall through and apply whatever
                    // finished before the cancellation was observed, same as a partial sequential
                    // run leaves a valid (just incomplete) report.
                }

                // Fold results into the session sequentially, in original game order, skipping any
                // slot a cancelled run never reached (default(TournamentGameRecord) is null for
                // this reference type — Parallel.For's own array-write is the only writer per
                // slot, so a null here unambiguously means "cancelled before this one ran").
                for (int i = 0; i < results.Length; i++)
                {
                    if (results[i] == null) break;
                    session.ApplyCompletedGame(results[i]);
                }
            }
        }
    }
}
