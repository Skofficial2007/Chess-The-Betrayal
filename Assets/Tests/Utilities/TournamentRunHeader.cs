using System;
using System.Globalization;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>Parsed form of the first line TournamentRunWriter writes to every run.jsonl.</summary>
    public readonly struct TournamentRunHeader
    {
        public readonly int SchemaVersion;
        public readonly string Mode;
        public readonly int RunSeed;
        public readonly int TotalGames;
        public readonly string TimeControl;
        public readonly int WorkerCount;
        public readonly DateTime StartUtc;

        public TournamentRunHeader(int schemaVersion, string mode, int runSeed, int totalGames,
            string timeControl, int workerCount, DateTime startUtc)
        {
            SchemaVersion = schemaVersion;
            Mode = mode;
            RunSeed = runSeed;
            TotalGames = totalGames;
            TimeControl = timeControl;
            WorkerCount = workerCount;
            StartUtc = startUtc;
        }

        public static bool TryParse(string line, out TournamentRunHeader header)
        {
            header = default;
            if (string.IsNullOrEmpty(line)) return false;

            string[] fields = line.Split('\t');
            if (fields.Length != 7) return false;

            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int schemaVersion)) return false;
            string mode = fields[1];
            if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int runSeed)) return false;
            if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int totalGames)) return false;
            string timeControl = fields[4];
            if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int workerCount)) return false;
            if (!DateTime.TryParse(fields[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime startUtc)) return false;

            header = new TournamentRunHeader(schemaVersion, mode, runSeed, totalGames, timeControl, workerCount, startUtc);
            return true;
        }
    }
}
