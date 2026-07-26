using System;
using System.Collections.Generic;
using ChessTheBetrayal.AI;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// The fixed list of games a corpus generation run will play, laid out entirely up front — same
    /// reproducibility contract TournamentSession uses: given the same runSeed and profile/position
    /// choices, this always produces the identical game list in the identical order, so a corpus
    /// build is deterministic regardless of how many worker threads later play it in parallel (the
    /// PLAYING order varies with scheduling; the game LIST and each game's seeds never do).
    ///
    /// Deliberately its own small type rather than reusing TournamentSession: a corpus build has no
    /// pairings, tallies, or tier accumulators to track — it only needs "play these games, sample
    /// quiet positions from each, label by outcome," which is a strict subset of what a benchmark
    /// tournament tracks. Building on TournamentSession would mean carrying win-rate bookkeeping a
    /// corpus generator never uses.
    /// </summary>
    public sealed class TexelCorpusGenerationPlan
    {
        public readonly struct PlannedGame
        {
            public readonly int GameIndex;
            public readonly int PositionIndex;
            public readonly AIProfile White;
            public readonly AIProfile Black;
            public readonly int SeedWhite;
            public readonly int SeedBlack;

            public PlannedGame(int gameIndex, int positionIndex, AIProfile white, AIProfile black, int seedWhite, int seedBlack)
            {
                GameIndex = gameIndex;
                PositionIndex = positionIndex;
                White = white;
                Black = black;
                SeedWhite = seedWhite;
                SeedBlack = seedBlack;
            }
        }

        public IReadOnlyList<PlannedGame> Games { get; }

        private TexelCorpusGenerationPlan(IReadOnlyList<PlannedGame> games)
        {
            Games = games;
        }

        /// <summary>
        /// Builds the game list: every profile in <paramref name="profiles"/> plays every curated
        /// position against itself, repeated <paramref name="gamesPerPosition"/> times to accumulate
        /// more samples from profiles whose search has any nondeterminism (blunder rolls, tie-break
        /// picks) — a deterministic zero-dial profile just produces duplicate games in that case,
        /// which is harmless, not wasted: the caller controls gamesPerPosition and can set it to 1
        /// for such profiles. There is no separate White/Black color swap to play, unlike a
        /// cross-tier tournament pairing (see TournamentSession) — the same profile is on both sides
        /// regardless of which color plays first, so swapping colors here would just replay the same
        /// matchup a second time for no new information.
        ///
        /// A profile plays against ITSELF (not against a different tier) because a Texel corpus needs
        /// positions labelled by realistic self-play outcomes at a coherent strength level, not
        /// cross-tier mismatches — mirrors the ADR's own framing of self-play as the source of a
        /// tuning corpus.
        /// </summary>
        public static TexelCorpusGenerationPlan Build(
            int runSeed, IReadOnlyList<AIProfile> profiles, int positionCount, int gamesPerPosition)
        {
            if (profiles == null || profiles.Count == 0)
                throw new ArgumentException("At least one profile is required.", nameof(profiles));
            if (positionCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(positionCount), positionCount, "Must be positive.");
            if (positionCount > CuratedPositionSuite.Count)
                throw new ArgumentOutOfRangeException(nameof(positionCount), positionCount,
                    $"Only {CuratedPositionSuite.Count} curated positions exist.");
            if (gamesPerPosition <= 0)
                throw new ArgumentOutOfRangeException(nameof(gamesPerPosition), gamesPerPosition, "Must be positive.");

            var games = new List<PlannedGame>(profiles.Count * positionCount * gamesPerPosition);
            int gameIndex = 0;

            // pairIndex here is purely a seeding-stream discriminator (mirrors TournamentSeeding's
            // own parameter, which was designed around pairings) — one per profile, since each
            // profile playing itself is this plan's only "pairing."
            for (int pairIndex = 0; pairIndex < profiles.Count; pairIndex++)
            {
                AIProfile profile = profiles[pairIndex];

                for (int positionIndex = 0; positionIndex < positionCount; positionIndex++)
                {
                    for (int repeat = 0; repeat < gamesPerPosition; repeat++)
                    {
                        int seedWhiteAsWhite = TournamentSeeding.DeriveSeed(runSeed, positionIndex, pairIndex, gameIndex, side: 0);
                        int seedBlackAsBlack = TournamentSeeding.DeriveSeed(runSeed, positionIndex, pairIndex, gameIndex, side: 1);
                        games.Add(new PlannedGame(gameIndex, positionIndex, profile, profile, seedWhiteAsWhite, seedBlackAsBlack));
                        gameIndex++;
                    }
                }
            }

            return new TexelCorpusGenerationPlan(games);
        }
    }
}
