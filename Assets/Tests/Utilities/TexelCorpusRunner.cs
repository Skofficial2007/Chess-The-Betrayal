using System;
using System.Threading;
using System.Threading.Tasks;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Plays every game in a TexelCorpusGenerationPlan, sampling quiet positions from each into a
    /// shared TexelCorpusWriter. Mirrors ParallelTournamentExecutor's own model exactly, for the same
    /// reason: each game is fully independent (its own board clone, its own searches, its own RNG
    /// streams, and now its own TexelCorpusSampler), so there is no shared mutable state between
    /// games to synchronize — only the ONE writer is shared, and it is already safe for concurrent
    /// producers by construction (see TexelCorpusWriter's own doc comment).
    /// </summary>
    public static class TexelCorpusRunner
    {
        /// <summary>Same reasoning as ParallelTournamentExecutor.DefaultMaxDegreeOfParallelism —
        /// leaves headroom for the OS/editor rather than pegging every core.</summary>
        public static int DefaultMaxDegreeOfParallelism =>
            Math.Max(1, Math.Min(16, Environment.ProcessorCount - 2));

        /// <summary>
        /// Plays every game in <paramref name="plan"/> across up to <paramref name="maxDegreeOfParallelism"/>
        /// worker threads, sampling quiet positions into <paramref name="writer"/> as each game
        /// finishes. Blocks the calling thread until every game is played. onGameCompleted, when
        /// supplied, fires once per finished game (current count, total) — from whichever worker
        /// thread just finished, so it must itself be safe to call concurrently.
        /// </summary>
        public static void Run(
            TexelCorpusGenerationPlan plan, TexelCorpusWriter writer,
            int maxDegreeOfParallelism = -1, CancellationToken cancellationToken = default,
            Action<int, int> onGameCompleted = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (maxDegreeOfParallelism <= 0) maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism;

            int total = plan.Games.Count;
            if (total == 0) return;

            int completedSoFar = 0;
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };

            try
            {
                Parallel.For(0, total, options, i =>
                {
                    TexelCorpusGenerationPlan.PlannedGame game = plan.Games[i];

                    // Fresh per game, same reasoning MatchSimulator's own doc comment gives for why
                    // one instance is never shared across threads — and a fresh sampler alongside it
                    // means this game's buffered positions can never mix with another game's, without
                    // any locking on the buffer itself.
                    var sampler = new TexelCorpusSampler(writer);
                    var simulator = new MatchSimulator(sampler: sampler);

                    BoardState position = CuratedPositionSuite.Build(game.PositionIndex);
                    simulator.PlayGame(position, game.White, game.Black, game.SeedWhite, game.SeedBlack);

                    int completed = Interlocked.Increment(ref completedSoFar);
                    onGameCompleted?.Invoke(completed, total);
                });
            }
            catch (OperationCanceledException)
            {
                // Expected on caller-initiated cancellation — every game that finished before the
                // cancel was observed already reached the writer, same partial-progress guarantee
                // ParallelTournamentExecutor gives.
            }
        }
    }
}
