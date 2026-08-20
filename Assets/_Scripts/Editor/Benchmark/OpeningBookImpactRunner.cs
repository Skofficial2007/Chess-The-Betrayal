using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>What playing from the opening book was worth to one difficulty tier.</summary>
    public sealed class BookImpactResult
    {
        public string ProfileId;
        public int BookDepthPlies;
        public int Games;
        public int BookWins;
        public int NoBookWins;
        public int Draws;

        /// <summary>Score from the book side's point of view: a win counts one, a draw counts a
        /// half. 50% means the book made no difference at all.</summary>
        public float BookScore;

        /// <summary>Half-width of the 95% interval around <see cref="BookScore"/>. The result only
        /// says something when this interval sits entirely above or below 50%.</summary>
        public float Margin95;

        public int DecisiveGames => BookWins + NoBookWins;

        /// <summary>
        /// The same score computed over decided games only. Two copies of one tier draw a great
        /// deal, and every draw pulls the headline score toward 50% whether or not the book helped,
        /// so a real effect can hide behind the draw rate. This is the number that still moves when
        /// that happens — at the cost of a smaller sample, hence its own interval.
        /// </summary>
        public float DecisiveWinShare => DecisiveGames == 0 ? 0.5f : (float)BookWins / DecisiveGames;

        public float DecisiveMargin95 => TournamentStatistics.WinRateMargin95(DecisiveGames);

        public float DrawRate => Games == 0 ? 0f : (float)Draws / Games;

        /// <summary>
        /// The score expressed as a rating difference, which is the unit an engine's opening book is
        /// normally discussed in. Undefined at a clean sweep in either direction (no finite rating
        /// gap produces certainty), so those saturate rather than returning infinity.
        /// </summary>
        public double EloGain => ScoreToElo(BookScore);
        public double EloLowerBound => ScoreToElo(Math.Max(0.001f, BookScore - Margin95));
        public double EloUpperBound => ScoreToElo(Math.Min(0.999f, BookScore + Margin95));

        /// <summary>True only when the whole interval clears 50% — the honest bar for claiming the
        /// book helped this tier at all, as opposed to landing above even by chance.</summary>
        public bool BookHelpedConclusively => BookScore - Margin95 > 0.5f;

        /// <summary>True only when the whole interval sits below 50%, which would mean the book is
        /// actively costing this tier games and is worth investigating rather than shipping.</summary>
        public bool BookHurtConclusively => BookScore + Margin95 < 0.5f;

        private static double ScoreToElo(float score)
        {
            if (score <= 0.001f) return -800;
            if (score >= 0.999f) return 800;
            return -400.0 * Math.Log10(1.0 / score - 1.0);
        }
    }

    /// <summary>
    /// Measures what the opening book is actually worth to each difficulty tier, by playing every
    /// tier against a copy of itself where only one side is allowed to open a book.
    ///
    /// This exists because the strength benchmark cannot answer the question: it builds the search
    /// directly and never constructs the agent that owns the book, so a book change is invisible to
    /// every tournament mode. Rather than teach the tournament about books — which would have both
    /// sides reciting the same theory out of curated positions that are themselves already
    /// openings, and would make every result recorded so far incomparable — this plays its own
    /// games from the standard starting position, where the book applies from move one and the only
    /// difference between the two sides is whether they may consult it.
    ///
    /// KNOWN LIMIT, and it caps what any number here can mean: this engine has no chess clock, by
    /// design. Each move is bounded on its own, so time a side saves by answering instantly from
    /// memory cannot be spent later. The largest practical benefit a book gives a real engine is
    /// therefore worth exactly nothing here, and what remains measurable is only whether the book's
    /// MOVE is better than the move that tier would have searched out. Read the results as a floor
    /// on the book's value, never as the whole of it.
    /// </summary>
    public static class OpeningBookImpactRunner
    {
        /// <summary>
        /// Plays gamesPerTier games for every profile in roster and returns one result per tier.
        /// Each tier plays only itself, so nothing but the book differs between the two sides.
        /// </summary>
        public static IReadOnlyList<BookImpactResult> RunAll(
            OpeningBookAsset book,
            IReadOnlyList<AIProfile> roster,
            int gamesPerTier,
            int runSeed,
            ITournamentProgress progress = null,
            int plyCap = MatchSimulator.DefaultPlyCap,
            int maxDegreeOfParallelism = -1,
            CancellationToken cancellationToken = default)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (gamesPerTier <= 0) throw new ArgumentOutOfRangeException(nameof(gamesPerTier));

            progress ??= NullTournamentProgress.Instance;
            if (maxDegreeOfParallelism <= 0)
                maxDegreeOfParallelism = ParallelTournamentExecutor.DefaultMaxDegreeOfParallelism;

            int totalGames = roster.Count * gamesPerTier;
            var outcomes = new MatchOutcome[totalGames];
            var bookWasWhite = new bool[totalGames];
            int completed = 0;

            // One simulator per worker thread: MatchSimulator's own doc comment is explicit that a
            // single instance must not be shared across threads.
            using (var threadLocalSimulator = new ThreadLocal<MatchSimulator>(
                () => new MatchSimulator(MatchTimeControl.ProductionBudget), trackAllValues: true))
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                };

                Parallel.For(0, totalGames, options, index =>
                {
                    int tierIndex = index / gamesPerTier;
                    int gameIndex = index % gamesPerTier;
                    AIProfile tier = roster[tierIndex];

                    // The book side swaps colour every game so the first-move advantage lands on
                    // each side equally often. Without this the measurement would report the value
                    // of moving first alongside the value of the book and never separate them.
                    bool bookPlaysWhite = (gameIndex % 2) == 0;
                    bookWasWhite[index] = bookPlaysWhite;

                    // Every book line starts from the real opening position. Starting anywhere else
                    // would hand the no-book side an opening it did not have to find, and would ask
                    // the book about a game that is already several moves old.
                    BoardState start = StandardStartingPosition();

                    int whiteSeed = TournamentSeeding.DeriveSeed(runSeed, 0, tierIndex, gameIndex, 0);
                    int blackSeed = TournamentSeeding.DeriveSeed(runSeed, 0, tierIndex, gameIndex, 1);

                    MatchResult result = threadLocalSimulator.Value.PlayGameWithBooks(
                        start, tier, tier,
                        whiteBook: bookPlaysWhite ? book : null,
                        blackBook: bookPlaysWhite ? null : book,
                        rngSeedWhite: whiteSeed, rngSeedBlack: blackSeed, plyCap: plyCap);

                    outcomes[index] = result.Outcome;

                    int done = Interlocked.Increment(ref completed);
                    progress.ReportGameCompleted(done, totalGames);
                });
            }

            var results = new List<BookImpactResult>(roster.Count);
            for (int tierIndex = 0; tierIndex < roster.Count; tierIndex++)
            {
                var tally = new BookImpactResult
                {
                    ProfileId = roster[tierIndex].Id,
                    BookDepthPlies = roster[tierIndex].OpeningBookDepthPlies,
                    Games = gamesPerTier
                };

                for (int gameIndex = 0; gameIndex < gamesPerTier; gameIndex++)
                {
                    int index = tierIndex * gamesPerTier + gameIndex;
                    MatchOutcome outcome = outcomes[index];

                    if (outcome == MatchOutcome.Draw) { tally.Draws++; continue; }

                    bool whiteWon = outcome == MatchOutcome.WhiteWon;
                    if (whiteWon == bookWasWhite[index]) tally.BookWins++;
                    else tally.NoBookWins++;
                }

                tally.BookScore = (tally.BookWins + 0.5f * tally.Draws) / gamesPerTier;
                tally.Margin95 = TournamentStatistics.WinRateMargin95(gamesPerTier);
                results.Add(tally);
            }

            return results;
        }

        /// <summary>
        /// The standard chess opening position with a consistent Zobrist hash and Betrayal available.
        /// Shared rather than rebuilt: this runner compares play with and without the book, and the
        /// book is looked up by position hash, so a private copy of the starting board that drifted by
        /// even one flag would report the book as having no effect at all.
        /// </summary>
        private static BoardState StandardStartingPosition() =>
            StandardChessPosition.Create(betrayalRightAvailable: true);
    }
}
