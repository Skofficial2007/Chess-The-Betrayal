using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.EditorTools.Benchmark;
using ChessTheBetrayal.Tooling;
using UnityEngine;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Texel
{
    /// <summary>
    /// The decisive measurement: does self-play at the highest BetrayalAggression dial produce
    /// ANY quiet positions downstream of a real Defection? Natural Act rate measured at ~0.9% even at
    /// this dial (see the Betrayal Act valuation audit), and most Acts resolve by Retribution rather
    /// than Defection — so this is a genuinely open question, not a foregone conclusion either way.
    /// Real production-budget self-play, so this is slow — an explicit, not a per-commit, run.
    /// </summary>
    [TestFixture]
    [Explicit("Plays a real generation pass at production time budgets — minutes, not a per-commit cost.")]
    public class TexelCorpusBetrayalStateProbe
    {
        [Test]
        [Timeout(30 * 60 * 1000)]
        public void Generate_AggressiveSelfPlayFullSuite_ReportsHowManyPostDefectionQuietPositionsAppear()
        {
            AIProfile aggressive = AIProfileTable.BuiltIn.Single(p => p.Id == "aggressive");
            string corpusBaseDirectory = Path.Combine(Application.dataPath, "..", "Docs", "Benchmarks", "Corpus");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var (corpusDirectory, positionCount) = TexelCorpusGenerator.Generate(
                runSeed: 20260713, new[] { aggressive }, positionCount: CuratedPositionSuite.Count,
                gamesPerPosition: 3, corpusBaseDirectory,
                onGameCompleted: (current, total) => Debug.Log($"[Texel Corpus Probe] {current}/{total} games complete"));
            stopwatch.Stop();

            List<TexelPositionRecord> records = ReadAllRecords(corpusDirectory).ToList();
            int postDefectionCount = records.Count(r => r.PostDefectionOccurred);

            Debug.Log($"BETRAYAL-CORPUS-PROBE positions={positionCount} postDefection={postDefectionCount} " +
                $"wallClockSeconds={stopwatch.Elapsed.TotalSeconds:F1} corpusDirectory={corpusDirectory}");

            Assert.That(positionCount, Is.GreaterThan(0), "the run must produce at least some quiet positions.");
        }

        private static IEnumerable<TexelPositionRecord> ReadAllRecords(string corpusDirectory)
        {
            foreach (string line in File.ReadAllLines(Path.Combine(corpusDirectory, "corpus.jsonl")).Skip(1))
            {
                if (TexelPositionRecord.TryParse(line, out TexelPositionRecord record))
                    yield return record;
            }
        }
    }
}
