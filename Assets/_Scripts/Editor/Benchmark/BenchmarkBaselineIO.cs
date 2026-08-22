using System.IO;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Reads/writes BenchmarkReport as the committed Docs/Benchmarks/baseline.json artifact. Plain
    /// JsonUtility — no new package dependency for a file this simple, and every field on
    /// BenchmarkReport/PairResult/TierPerformance is already a JsonUtility-serializable shape.
    /// </summary>
    public static class BenchmarkBaselineIO
    {
        public static string DefaultPath => Path.Combine(Application.dataPath, "..", "Docs", "Benchmarks", "baseline.json");

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
