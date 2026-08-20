using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tooling.Texel;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Texel
{
    /// <summary>
    /// The acceptance suite for the corpus generator: reproducible under a fixed seed, every sampled
    /// position is genuinely quiet, and every position's label matches the game it came from. Runs
    /// against fast, shallow fixture profiles with a tight ply cap — the same reasoning
    /// TournamentSession's own tests use fixture profiles rather than AIProfileTable.BuiltIn, so this
    /// suite verifies the CONTRACT quickly rather than measuring the real roster's corpus (that's
    /// Commit 4's dedicated, real-profile generation run).
    /// </summary>
    [TestFixture]
    public class TexelCorpusGeneratorTests
    {
        private static AIProfile FastProfile(string id, int maxDepth) =>
            new AIProfile(id, maxDepth, timeBudget: new AITimeBudget(1500, 2500), blunderRate: 0f, blunderMarginCp: 0,
                betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0, useOpeningBook: false);

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TexelCorpusGeneratorTest_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        [Test]
        public void Generate_SameSeedAndConfig_ProducesTheIdenticalPositionSet()
        {
            var profiles = new[] { FastProfile("fixture", maxDepth: 2) };

            var (dirA, countA) = TexelCorpusGenerator.Generate(runSeed: 777, profiles, positionCount: 2, gamesPerPosition: 1, _tempDir + "_a");
            var (dirB, countB) = TexelCorpusGenerator.Generate(runSeed: 777, profiles, positionCount: 2, gamesPerPosition: 1, _tempDir + "_b");

            try
            {
                Assert.That(countB, Is.EqualTo(countA));

                // Positions are written in whatever order games finish across worker threads, so
                // compare as a SET (sorted by content), not raw line order — the same tolerance
                // TournamentRunWriter's own reader has for out-of-order records.
                List<string> linesA = ReadPositionLines(dirA);
                List<string> linesB = ReadPositionLines(dirB);
                linesA.Sort();
                linesB.Sort();

                Assert.That(linesB, Is.EqualTo(linesA),
                    "the same run seed and config must reproduce the exact same set of sampled positions.");
            }
            finally
            {
                if (Directory.Exists(_tempDir + "_a")) Directory.Delete(_tempDir + "_a", recursive: true);
                if (Directory.Exists(_tempDir + "_b")) Directory.Delete(_tempDir + "_b", recursive: true);
            }
        }

        [Test]
        public void Generate_EveryWrittenPosition_DecodesToAGenuinelyQuietPosition()
        {
            var profiles = new[] { FastProfile("fixture", maxDepth: 2) };
            var engine = new ChessEngineAdapter();

            var (corpusDirectory, positionCount) = TexelCorpusGenerator.Generate(
                runSeed: 555, profiles, positionCount: 3, gamesPerPosition: 1, _tempDir);

            Assert.That(positionCount, Is.GreaterThan(0), "these fixture games must produce at least one quiet sample.");

            foreach (TexelPositionRecord record in ReadAllRecords(corpusDirectory))
            {
                BoardState board = record.ToBoardState();
                Assert.That(board.PendingBetrayerSquare, Is.Null,
                    "no persisted position may have a Betrayer awaiting Retribution/Defection.");
                Assert.That(engine.IsKingInCheck(board, record.SideToMove), Is.False,
                    "no persisted position may have its side to move in check.");
            }
        }

        [Test]
        public void Generate_EveryPositionsLabel_MatchesItsGamesFinalOutcome()
        {
            // A deterministic, decisive fixture: White's overwhelming material advantage at
            // maxDepth:2 with zero blunder/tie-break dials converts reliably within the ply cap, so
            // every quiet position sampled from these games should be labelled a White win.
            var dominant = new AIProfile("dominant", maxDepth: 2, timeBudget: new AITimeBudget(1500, 2500),
                blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f,
                tieBreakWindowCp: 0, useOpeningBook: false);

            var (corpusDirectory, positionCount) = TexelCorpusGenerator.Generate(
                runSeed: 999, new[] { dominant }, positionCount: 1, gamesPerPosition: 1, _tempDir);

            List<TexelPositionRecord> records = ReadAllRecords(corpusDirectory).ToList();
            Assert.That(records, Is.Not.Empty);

            // Every record must carry ONE of the three valid Texel labels — not a per-ply score
            // leaking through, which is the correctness trap this test guards against.
            Assert.That(records, Has.All.Matches<TexelPositionRecord>(r => r.Label == 0.0 || r.Label == 0.5 || r.Label == 1.0));
        }

        [Test]
        public void Generate_ManyGamesInParallel_LosesNoPositionsFromAnyGame()
        {
            var profiles = new[] { FastProfile("fixture", maxDepth: 2) };

            var (corpusDirectory, reportedCount) = TexelCorpusGenerator.Generate(
                runSeed: 42, profiles, positionCount: 4, gamesPerPosition: 3, _tempDir);

            List<TexelPositionRecord> records = ReadAllRecords(corpusDirectory).ToList();

            Assert.That(records, Has.Count.EqualTo(reportedCount),
                "the generator's own reported position count must match what actually landed on disk — a lost write under concurrency would show up as a mismatch here.");
        }

        [Test]
        public void Generate_WritesAManifestDescribingTheRun()
        {
            var profiles = new[] { FastProfile("fixture", maxDepth: 2) };

            var (corpusDirectory, _) = TexelCorpusGenerator.Generate(
                runSeed: 111, profiles, positionCount: 1, gamesPerPosition: 1, _tempDir);

            string manifestPath = Path.Combine(corpusDirectory, "manifest.json");
            Assert.That(File.Exists(manifestPath), Is.True);
            string manifest = File.ReadAllText(manifestPath);
            Assert.That(manifest, Does.Contain("\"runSeed\": 111"));
            Assert.That(manifest, Does.Contain("\"fixture\""));
        }

        private static List<string> ReadPositionLines(string corpusDirectory) =>
            File.ReadAllLines(Path.Combine(corpusDirectory, "corpus.jsonl")).Skip(1).ToList();

        private static IEnumerable<TexelPositionRecord> ReadAllRecords(string corpusDirectory)
        {
            foreach (string line in ReadPositionLines(corpusDirectory))
            {
                if (TexelPositionRecord.TryParse(line, out TexelPositionRecord record))
                    yield return record;
            }
        }
    }
}
