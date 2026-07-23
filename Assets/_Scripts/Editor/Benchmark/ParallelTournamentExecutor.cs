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
        /// progress reports raw completion count as games finish across all workers (fires from a
        /// worker thread, out of order, so it must itself be safe to call concurrently — both
        /// shipped sinks are) — separate from the session's own OnGameCompleted (fires from the
        /// caller's thread, in order) so a progress bar/log can show live movement without a
        /// caller needing to reason about thread affinity. Defaults to reporting nothing.
        /// runWriter, when supplied, receives each game the moment IT finishes, from whichever
        /// worker thread that is — not batched into the fold loop below — so a run killed mid-way
        /// still has every game that had already finished durably on disk, in whatever order they
        /// completed rather than original game order (TournamentRunWriter's own reader tolerates
        /// that; every record carries its own game index). See TournamentRunWriter's own doc
        /// comment for why writing from a worker thread never blocks the search itself.
        /// watchdog, when supplied, is fed every progress report and its Token
        /// is the one actually passed to the parallel loop — construct it with cancellationToken
        /// as its own externalToken so a caller-initiated cancel and a stall both flow through the
        /// same path (see TournamentWatchdog's own doc comment). When a watchdog is supplied, the
        /// cancellationToken parameter here is ignored in favor of watchdog.Token.
        /// </summary>
        public static void RunRemainingGames(
            TournamentSession session,
            int maxDegreeOfParallelism = -1,
            CancellationToken cancellationToken = default,
            ITournamentProgress progress = null,
            TournamentRunWriter runWriter = null,
            TournamentWatchdog watchdog = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (maxDegreeOfParallelism <= 0) maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism;
            progress ??= NullTournamentProgress.Instance;
            CancellationToken effectiveToken = watchdog?.Token ?? cancellationToken;

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
                    CancellationToken = effectiveToken
                };

                try
                {
                    Parallel.For(0, totalPending, options, offset =>
                    {
                        MatchSimulator simulator = threadLocalSimulator.Value;
                        TournamentGameRecord record = session.PlayOneGame(simulator, startIndex + offset);
                        results[offset] = record;

                        // Persisted the moment THIS game finishes, from whichever worker thread got
                        // here — not folded in at the end with everything else. WriteGame only
                        // enqueues onto a BlockingCollection (safe for concurrent producers by
                        // design) and returns immediately, so this never blocks the search. This is
                        // the whole reason a runWriter exists: a run killed partway must leave every
                        // game that already finished durably on disk, not just whatever the fold
                        // loop below had gotten to.
                        runWriter?.WriteGame(ToRunRecord(record, startIndex + offset));

                        int completed = Interlocked.Increment(ref completedSoFar);
                        progress.ReportGameCompleted(completed, totalPending);
                        watchdog?.ReportProgress(completed);
                    });
                }
                catch (OperationCanceledException)
                {
                    // Expected when the caller cancels mid-run — fall through and apply whatever
                    // finished before the cancellation was observed, same as a partial sequential
                    // run leaves a valid (just incomplete) report.
                }

                // Fold every game that actually finished into the session, in original game order.
                // A cancelled run leaves holes (default(TournamentGameRecord) is null for this
                // reference type — Parallel.For's own array-write is the only writer per slot), but
                // those holes are not confined to the tail: workers race ahead of each other, so game
                // 9 can finish before game 3 gets cancelled out from under a slower worker. Skipping
                // past a hole instead of stopping at it keeps every game that did complete, rather
                // than silently discarding real results the moment the first gap appears. Only the
                // session fold happens here now — ApplyCompletedGame mutates shared tallies/tier
                // accumulators with no locking of its own, so it stays serialized on this one
                // caller thread exactly as before; persistence already happened above, per-game.
                for (int i = 0; i < results.Length; i++)
                {
                    if (results[i] == null) continue;
                    session.ApplyCompletedGame(results[i]);
                }
            }
        }

        /// <summary>Converts a played game into the flat shape TournamentRunWriter persists.
        /// Subject/opponent are re-derived from White/Black + SubjectIsWhite the same way
        /// TournamentSession.ApplyCompletedGame does, so the run file and the in-memory report
        /// always agree on which side is "subject."</summary>
        private static TournamentRunRecord ToRunRecord(TournamentGameRecord record, int gameIndex)
        {
            string subjectId = record.SubjectIsWhite ? record.WhiteId : record.BlackId;
            string opponentId = record.SubjectIsWhite ? record.BlackId : record.WhiteId;
            double elapsedMs = record.Result.WhiteStats.TotalElapsedMs + record.Result.BlackStats.TotalElapsedMs;

            return new TournamentRunRecord(gameIndex, record.PairIndex, subjectId, opponentId,
                record.SubjectIsWhite, record.PositionIndex, record.Result.Result.Outcome,
                record.Result.Result.PlyCount, record.Result.Result.ReachedPlyCap, elapsedMs);
        }
    }
}
