using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Tooling.Benchmark;
using ChessTheBetrayal.Tooling.Agreement;
using ChessTheBetrayal.Tooling.Match;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.BookImport
{
    /// <summary>
    /// Correctness guards for the opening-book measurement seam. Fast, always run.
    ///
    /// The one that matters most is the first: giving MatchSimulator no books must play precisely
    /// the game it played before the seam existed. Every strength number this project has recorded
    /// came through that path, so if adding the hook perturbed it by even one move, every
    /// comparison against a previous run would silently become invalid.
    /// </summary>
    [TestFixture]
    public class OpeningBookSimulatorSeamTests
    {
        private const int TestPlyCap = 30;

        private static AIProfile ShallowTier(string id) => new AIProfile(
            id, maxDepth: 2, timeBudget: new AITimeBudget(500, 750), blunderRate: 0f,
            blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0,
            useOpeningBook: true, openingBookDepthPlies: 0);

        private static OpeningBookAsset BookFrom(string sourceText)
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile(sourceText);
            var asset = ScriptableObject.CreateInstance<OpeningBookAsset>();
            asset.SetEntries(keys, packedMoves, weights, schemeVersion);
            return asset;
        }

        private static BoardState Start() => OpeningBookCompiler.CreateStandardStartingPosition();

        [Test]
        public void PlayGameWithBooks_GivenNoBooks_PlaysExactlyTheGamePlayGameDoes()
        {
            // The regression guard for every benchmark number already on disk.
            var simulator = new MatchSimulator(MatchTimeControl.Uncapped);
            AIProfile tier = ShallowTier("t");

            MatchResult withoutSeam = simulator.PlayGame(
                Start(), tier, tier, rngSeedWhite: 11, rngSeedBlack: 22, plyCap: TestPlyCap);
            MatchResult throughSeam = simulator.PlayGameWithBooks(
                Start(), tier, tier, whiteBook: null, blackBook: null,
                rngSeedWhite: 11, rngSeedBlack: 22, plyCap: TestPlyCap);

            Assert.That(throughSeam.Outcome, Is.EqualTo(withoutSeam.Outcome),
                "Passing no books must leave the existing tournament path untouched.");
            Assert.That(throughSeam.PlyCount, Is.EqualTo(withoutSeam.PlyCount),
                "Passing no books changed the length of the game, so the seam is not inert.");
        }

        [Test]
        public void PlayGameWithBooks_GivenABook_ActuallyPlaysItsTheory()
        {
            // Without this, a book that was silently never consulted would produce a clean 50%
            // result and read as "the book makes no difference" rather than "the book never ran".
            OpeningBookAsset book = BookFrom("e2e4 e7e5 g1f3 b8c6 f1c4 f8c5");
            var simulator = new MatchSimulator(MatchTimeControl.Uncapped);
            AIProfile tier = ShallowTier("t");

            try
            {
                MatchResult booked = simulator.PlayGameWithBooks(
                    Start(), tier, tier, whiteBook: book, blackBook: null,
                    rngSeedWhite: 11, rngSeedBlack: 22, plyCap: TestPlyCap);
                MatchResult unbooked = simulator.PlayGameWithBooks(
                    Start(), tier, tier, whiteBook: null, blackBook: null,
                    rngSeedWhite: 11, rngSeedBlack: 22, plyCap: TestPlyCap);

                // These tiers are deterministic (no dials), so the only thing that can make the two
                // games differ at all is White answering from the book.
                Assert.That(
                    booked.Outcome != unbooked.Outcome || booked.PlyCount != unbooked.PlyCount,
                    Is.True,
                    "Handing White a book changed nothing about the game, so the book was never consulted.");
            }
            finally
            {
                Object.DestroyImmediate(book);
            }
        }

        [Test]
        public void PlayGameWithBooks_WhenTheTierAllowanceIsZeroPlies_TheBookIsNeverUsed()
        {
            // Proves the per-tier allowance is honoured on this path too, so a measured result is
            // about the tier's real repertoire rather than the whole book regardless of settings.
            OpeningBookAsset book = BookFrom("e2e4 e7e5 g1f3 b8c6 f1c4 f8c5");
            var simulator = new MatchSimulator(MatchTimeControl.Uncapped);

            var noRepertoire = new AIProfile(
                "capped", maxDepth: 2, timeBudget: new AITimeBudget(500, 750), blunderRate: 0f,
                blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0,
                useOpeningBook: false, openingBookDepthPlies: 0);

            try
            {
                MatchResult withBook = simulator.PlayGameWithBooks(
                    Start(), noRepertoire, noRepertoire, whiteBook: book, blackBook: null,
                    rngSeedWhite: 11, rngSeedBlack: 22, plyCap: TestPlyCap);
                MatchResult withoutBook = simulator.PlayGameWithBooks(
                    Start(), noRepertoire, noRepertoire, whiteBook: null, blackBook: null,
                    rngSeedWhite: 11, rngSeedBlack: 22, plyCap: TestPlyCap);

                Assert.That(withBook.Outcome, Is.EqualTo(withoutBook.Outcome));
                Assert.That(withBook.PlyCount, Is.EqualTo(withoutBook.PlyCount),
                    "A tier that does not use the book still played differently when handed one.");
            }
            finally
            {
                Object.DestroyImmediate(book);
            }
        }
    }

    /// <summary>
    /// The two long measurements of what the opening book is worth, per difficulty tier. Both are
    /// explicit: they play real games or run deep searches and take hours, so they are run
    /// deliberately, never as part of a normal test pass.
    ///
    /// Read them together. The match run says HOW MUCH the book is worth to a tier; the agreement
    /// run says WHY — whether the book is picking better opening moves than that tier finds for
    /// itself, or simply picking the same ones.
    /// </summary>
    [TestFixture]
    public class OpeningBookImpactTests
    {
        private const string CompiledAssetPath = OpeningBookBuilder.DefaultAssetPath;
        private const int RunSeed = 20260713;

        private static OpeningBookAsset LoadShippedBook()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OpeningBookAsset>(CompiledAssetPath);
            Assert.That(asset, Is.Not.Null, $"No compiled opening book found at '{CompiledAssetPath}'.");
            return asset;
        }

        private static string FormatMatchResults(IReadOnlyList<BookImpactResult> results, int gamesPerTier)
        {
            var report = new StringBuilder();
            report.AppendLine($"Book vs no book, same tier both sides, {gamesPerTier} games per tier, from the standard start.");
            report.AppendLine();
            report.AppendLine("| Tier | Book plies | Games | Book W/L/D | Score | 95% CI | Draws | Decisive share | Elo | Verdict |");
            report.AppendLine("|---|---:|---:|---|---:|---:|---:|---:|---:|---|");

            foreach (BookImpactResult r in results)
            {
                string verdict = r.BookHelpedConclusively ? "book helps"
                    : r.BookHurtConclusively ? "BOOK HURTS"
                    : "cannot tell";
                string plies = r.BookDepthPlies == 0 ? "all" : r.BookDepthPlies.ToString();

                report.AppendLine(
                    $"| {r.ProfileId} | {plies} | {r.Games} | {r.BookWins}/{r.NoBookWins}/{r.Draws} | " +
                    $"{r.BookScore * 100:F1}% | +/-{r.Margin95 * 100:F1}% | {r.DrawRate * 100:F0}% | " +
                    $"{r.DecisiveWinShare * 100:F1}% (n={r.DecisiveGames}) | " +
                    $"{r.EloGain:+0;-0} [{r.EloLowerBound:+0;-0}, {r.EloUpperBound:+0;-0}] | {verdict} |");
            }

            return report.ToString();
        }

        private static string FormatAgreementResults(IReadOnlyList<BookAgreementResult> results)
        {
            var report = new StringBuilder();
            report.AppendLine("| Tier | Book plies | Positions | Book agrees | Tier agrees | Gain | Move changed | Tier cut short |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (BookAgreementResult r in results)
            {
                string plies = r.BookDepthPlies == 0 ? "all" : r.BookDepthPlies.ToString();
                report.AppendLine(
                    $"| {r.ProfileId} | {plies} | {r.Positions} | {r.BookAgreement * 100:F1}% | " +
                    $"{r.TierAgreement * 100:F1}% | {r.AgreementGain * 100:+0.0;-0.0} pts | " +
                    $"{r.MoveChangedRate * 100:F0}% | {r.TierCutShort} |");
            }

            return report.ToString();
        }

        /// <summary>
        /// A short pass over every tier, to confirm the harness produces sane numbers before the
        /// long one is worth starting. Its intervals are far too wide to conclude anything from —
        /// that is what the full run below is for.
        /// </summary>
        [Test]
        [Explicit("Plays 240 real games — run on demand.")]
        [Timeout(3 * 60 * 60 * 1000)]
        public void BookImpact_Scan_FortyGamesPerTier()
        {
            RunMatchMeasurement(AIProfileTable.BuiltIn, gamesPerTier: 40, label: "scan");
        }

        /// <summary>
        /// The measurement to quote. 200 games a tier puts the 95% interval near +/-7 points, which
        /// is tight enough to separate a real book effect from noise on the tiers where the effect
        /// is large. It will still likely be inconclusive on the deepest tiers, where two copies of
        /// the same engine draw most of their games — that is a property of the question, not a
        /// fault in the run, and the decisive-share column is there for exactly that case.
        /// </summary>
        [Test]
        [Explicit("Plays 1200 real games and takes hours — run on demand.")]
        [Timeout(8 * 60 * 60 * 1000)]
        public void BookImpact_Full_TwoHundredGamesPerTier()
        {
            RunMatchMeasurement(AIProfileTable.BuiltIn, gamesPerTier: 200, label: "full");
        }

        /// <summary>
        /// The two tiers whose measured effect is large enough for more games to settle it.
        ///
        /// A scan at forty games a tier put every tier's interval across even, but the sizes
        /// differed enormously: normal and aggressive sat far enough from even that a couple of
        /// hundred games decides them, while easy, hard and impossible would need somewhere between
        /// seven hundred and fifteen hundred games EACH, and extreme landed on exactly even, where
        /// no sample size can help. Playing the tiers that cannot be resolved costs hours and
        /// returns the word "inconclusive" that arithmetic already predicted, so this run spends
        /// its games only where they can change what we know.
        ///
        /// aggressive is the reason this exists. It scored below even, and it is the one tier
        /// carrying a substantially reshaped evaluator — if neutral theory really is steering it
        /// into positions its own judgement dislikes, that is worth knowing rather than assuming.
        /// </summary>
        [Test]
        [Explicit("Plays 400 real games — run on demand.")]
        [Timeout(4 * 60 * 60 * 1000)]
        public void BookImpact_Targeted_NormalAndAggressive()
        {
            var roster = new List<AIProfile>();
            foreach (AIProfile tier in AIProfileTable.BuiltIn)
            {
                if (tier.Id == "normal" || tier.Id == "aggressive") roster.Add(tier);
            }

            Assert.That(roster.Count, Is.EqualTo(2), "Expected both 'normal' and 'aggressive' in the roster.");

            RunMatchMeasurement(roster, gamesPerTier: 200, label: "targeted");
        }

        private static void RunMatchMeasurement(IReadOnlyList<AIProfile> roster, int gamesPerTier, string label)
        {
            OpeningBookAsset book = LoadShippedBook();
            var progress = new TestContextProgressSink($"Book impact ({label})");

            IReadOnlyList<BookImpactResult> results = OpeningBookImpactRunner.RunAll(
                book, roster, gamesPerTier, RunSeed, progress);

            TestContext.Out.WriteLine(FormatMatchResults(results, gamesPerTier));

            foreach (BookImpactResult r in results)
            {
                Assert.That(r.Games, Is.EqualTo(gamesPerTier));
                Assert.That(r.BookWins + r.NoBookWins + r.Draws, Is.EqualTo(gamesPerTier),
                    $"'{r.ProfileId}' lost games somewhere in the tally.");
            }
        }

        /// <summary>
        /// Descriptive only — this does NOT show whether the book plays good moves, and its output
        /// must not be quoted as if it did. The deep reference shares the tiers' evaluator and not
        /// the book's, so the book scores lower than every tier by construction. See
        /// OpeningBookAgreementRunner for the full explanation and the measurement that does work.
        ///
        /// Kept and still run because two of its columns are real observations: how many sampled
        /// book positions have a depth-stable answer at all, and how often each tier runs out of
        /// clock before reaching its ceiling in the opening.
        /// </summary>
        [Test]
        [Explicit("Runs deep reference searches over sampled book positions — run on demand.")]
        [Timeout(4 * 60 * 60 * 1000)]
        public void BookAgreement_AgainstADeeperSearch_PerTier()
        {
            const int WantedPositions = 40;

            OpeningBookAsset book = LoadShippedBook();
            var engine = new ChessEngineAdapter();

            List<BoardState> candidates = OpeningBookAgreementRunner.SampleBookPositions(
                book, engine, wanted: WantedPositions, seed: RunSeed, maxWalkPlies: 12);

            Assert.That(candidates.Count, Is.GreaterThan(0), "No book positions could be sampled.");

            // A position whose deep answer changes with depth reports ply parity rather than
            // judgement, so it is excluded deliberately instead of being folded into the headline.
            AIProfile neutral = AIProfileTable.BuiltIn[5];
            var oracle = new ReferenceMoveOracle(neutral);

            var stable = new List<BoardState>(candidates.Count);
            foreach (BoardState candidate in candidates)
            {
                if (oracle.IsStableAcrossDepths(candidate)) stable.Add(candidate);
            }

            TestContext.Out.WriteLine(
                $"Sampled {candidates.Count} book positions; {stable.Count} have a depth-stable " +
                $"reference answer and are used below ({candidates.Count - stable.Count} excluded).");

            Assert.That(stable.Count, Is.GreaterThan(0),
                "Every sampled book position was depth-unstable, so no agreement figure can be trusted.");

            List<BookAgreementResult> results = OpeningBookAgreementRunner.Run(
                book, AIProfileTable.BuiltIn, oracle, stable);

            TestContext.Out.WriteLine(FormatAgreementResults(results));
        }
    }
}
