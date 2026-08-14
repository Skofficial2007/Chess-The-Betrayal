using System;
using System.Collections.Generic;
using System.IO;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Tests.Utilities;
using UnityEditor;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Batch entry point over TexelCorpusGenerationPlan/TexelCorpusRunner: builds a plan, plays every
    /// game across worker threads via TexelCorpusRunner, and writes the result to a timestamped
    /// corpus directory. Same shape as BenchmarkRunner.RunAll — the thing a menu command or a CI
    /// -executeMethod invocation calls — deliberately kept separate from BenchmarkRunner rather than
    /// folded into it, since a corpus build has no pairings or win-rate tallies to report.
    ///
    /// Dev/editor-only, same category as the opening-book compiler and the benchmark tooling — never
    /// referenced by Core, AI, or the shipped player build.
    /// </summary>
    public static class TexelCorpusGenerator
    {
        public const int SchemaVersion = 1;

        /// <summary>
        /// Builds a plan from <paramref name="profiles"/> x the first <paramref name="positionCount"/>
        /// curated positions x <paramref name="gamesPerPosition"/> repeats per side, plays it in
        /// parallel, and writes every sampled quiet position to a timestamped subdirectory of
        /// <paramref name="baseDirectory"/>. Returns the corpus directory and total position count so
        /// a caller (menu command, batch entry point, or a test) can report or assert on the result
        /// without re-reading the file.
        /// </summary>
        public static (string CorpusDirectory, int PositionCount) Generate(
            int runSeed, IReadOnlyList<AIProfile> profiles,
            int positionCount, int gamesPerPosition, string baseDirectory,
            Action<int, int> onGameCompleted = null)
        {
            TexelCorpusGenerationPlan plan = TexelCorpusGenerationPlan.Build(runSeed, profiles, positionCount, gamesPerPosition);

            DateTime startUtc = DateTime.UtcNow;
            string folderName = $"texel-{runSeed}-{startUtc:yyyyMMdd-HHmmss}";
            string corpusDirectory = Path.Combine(baseDirectory, folderName);
            string headerLine = TexelCorpusWriter.BuildHeaderLine(SchemaVersion, runSeed, startUtc);

            int positionCountWritten;
            using (var writer = new TexelCorpusWriter(corpusDirectory, headerLine))
            {
                TexelCorpusRunner.Run(plan, writer, onGameCompleted: onGameCompleted);
                positionCountWritten = writer.PositionsWritten;
            }

            WriteManifest(corpusDirectory, runSeed, profiles, positionCount, gamesPerPosition,
                plan.Games.Count, positionCountWritten, startUtc);

            return (corpusDirectory, positionCountWritten);
        }

        private static void WriteManifest(
            string corpusDirectory, int runSeed, IReadOnlyList<AIProfile> profiles,
            int positionCount, int gamesPerPosition, int totalGames, int totalPositions, DateTime startUtc)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{').Append('\n');
            sb.Append($"  \"schemaVersion\": {SchemaVersion},\n");
            sb.Append($"  \"runSeed\": {runSeed},\n");
            sb.Append($"  \"startUtc\": \"{startUtc:O}\",\n");
            sb.Append($"  \"endUtc\": \"{DateTime.UtcNow:O}\",\n");
            sb.Append($"  \"positionCountPerProfile\": {positionCount},\n");
            sb.Append($"  \"gamesPerPosition\": {gamesPerPosition},\n");
            sb.Append($"  \"totalGames\": {totalGames},\n");
            sb.Append($"  \"totalPositions\": {totalPositions},\n");
            sb.Append("  \"profiles\": [");
            for (int i = 0; i < profiles.Count; i++)
            {
                sb.Append('"').Append(profiles[i].Id).Append('"');
                if (i < profiles.Count - 1) sb.Append(", ");
            }
            sb.Append("]\n}");

            File.WriteAllText(Path.Combine(corpusDirectory, "manifest.json"), sb.ToString());
        }
    }

    /// <summary>
    /// Interactive/batchmode entry points for TexelCorpusGenerator — same split BenchmarkMenu keeps
    /// between the runnable logic and the menu/CI wiring around it.
    /// </summary>
    public static class TexelCorpusGeneratorMenu
    {
        private const int DefaultRunSeed = 20260713;
        private const int DefaultPositionCount = 4;
        private const int DefaultGamesPerPosition = 1;

        [MenuItem("Chess: The Betrayal/AI/Generate Texel Corpus (Quick)")]
        private static void GenerateQuickFromMenu() => GenerateAndLog(DefaultPositionCount, DefaultGamesPerPosition, AIProfileTable.BuiltIn);

        /// <summary>Batchmode/CI entry point: <c>Unity -batchmode -executeMethod
        /// ChessTheBetrayal.EditorTools.Benchmark.TexelCorpusGeneratorMenu.GenerateCorpusBatch</c>.
        /// Logs progress the same way BenchmarkMenu's batch commands do and exits nonzero on any
        /// unhandled failure, so a CI job can gate on it without parsing log text.</summary>
        public static void GenerateCorpusBatch()
        {
            try
            {
                GenerateAndLog(DefaultPositionCount, DefaultGamesPerPosition, AIProfileTable.BuiltIn);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Texel corpus generation failed: {ex}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        private static void GenerateAndLog(int positionCount, int gamesPerPosition, IReadOnlyList<AIProfile> profiles)
        {
            var progress = new DebugLogProgressSink("Texel Corpus");
            var (corpusDirectory, writtenPositionCount) = TexelCorpusGenerator.Generate(
                DefaultRunSeed, profiles, positionCount, gamesPerPosition, CorpusDirectory,
                onGameCompleted: progress.ReportGameCompleted);

            Debug.Log($"Texel corpus generated: {writtenPositionCount} quiet positions written to {corpusDirectory}");
        }

        private static string CorpusDirectory =>
            Path.Combine(Application.dataPath, "..", "Docs", "Benchmarks", "Corpus");
    }
}
