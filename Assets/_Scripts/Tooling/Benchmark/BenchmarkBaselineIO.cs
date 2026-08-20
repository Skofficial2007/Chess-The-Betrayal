using System.IO;
using UnityEngine;

namespace ChessTheBetrayal.Tooling.Benchmark
{
    /// <summary>
    /// Reads/writes BenchmarkReport as the committed Docs/Benchmarks/baseline.json artifact. Plain
    /// JsonUtility — no new package dependency for a file this simple, and every field on
    /// BenchmarkReport/PairResult/TierPerformance is already a JsonUtility-serializable shape.
    ///
    /// THE COMMITTED BASELINE IS HAND-MERGED, and nothing here should overwrite it wholesale.
    /// It deliberately takes each pairing from whichever run measured it best: the two
    /// top-of-ladder rows come from a 320-game run because they sit close enough to the strength
    /// floor that only a large sample separates them, while the rest come from a whole-matrix run
    /// where 40 games settle them by a wide margin. A single run cannot express that, so writing
    /// one straight over the file would replace well-measured rows with worse ones and silently
    /// drop the review fields (verdicts, intervals, and which run each number came from) that a
    /// reader relies on. Write a proposal instead and merge the rows that genuinely improve.
    /// </summary>
    public static class BenchmarkBaselineIO
    {
        public static string DefaultPath => Path.Combine(Application.dataPath, "..", "Docs", "Benchmarks", "baseline.json");

        /// <summary>Where a freshly measured candidate goes for review, so producing one can never
        /// damage the committed baseline.</summary>
        public static string ProposedPath => Path.Combine(Application.dataPath, "..", "Docs", "Benchmarks", "baseline.proposed.json");

        public static void Write(BenchmarkReport report, string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(report, prettyPrint: true);
            File.WriteAllText(path, json);
        }

        /// <summary>Returns null if no baseline file exists yet — the very first run has nothing to
        /// compare against, which is a normal state, not an error.</summary>
        public static BenchmarkReport TryRead(string path)
        {
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<BenchmarkReport>(json);
        }
    }
}
