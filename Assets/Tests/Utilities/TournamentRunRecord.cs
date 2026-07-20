using System;
using System.Globalization;
using System.Text;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// One completed game as it gets written to a run's JSON Lines log — everything needed to
    /// reconstruct a BenchmarkReport offline, without replaying anything, so a killed run's file
    /// is real data rather than a breadcrumb pointing at data that no longer exists.
    ///
    /// Deliberately hand-rolled line encoding rather than a JSON library: each record is written by
    /// a single background thread as a one-line append, and the format only needs to round-trip
    /// this one flat shape reliably, including tolerating a line torn in half by a killed process.
    /// </summary>
    public readonly struct TournamentRunRecord
    {
        public readonly int GameIndex;
        public readonly int PairIndex;
        public readonly string SubjectId;
        public readonly string OpponentId;
        public readonly bool SubjectIsWhite;
        public readonly int PositionIndex;
        public readonly MatchOutcome Outcome;
        public readonly int PlyCount;
        public readonly bool ReachedPlyCap;
        public readonly double ElapsedMs;

        public TournamentRunRecord(int gameIndex, int pairIndex, string subjectId, string opponentId,
            bool subjectIsWhite, int positionIndex, MatchOutcome outcome, int plyCount, bool reachedPlyCap,
            double elapsedMs)
        {
            GameIndex = gameIndex;
            PairIndex = pairIndex;
            SubjectId = subjectId;
            OpponentId = opponentId;
            SubjectIsWhite = subjectIsWhite;
            PositionIndex = positionIndex;
            Outcome = outcome;
            PlyCount = plyCount;
            ReachedPlyCap = reachedPlyCap;
            ElapsedMs = elapsedMs;
        }

        /// <summary>Tab-separated, one line, no embedded newlines or tabs in any field (ids are
        /// short identifiers, never free text) — simple enough to write and parse without a JSON
        /// dependency on the hot append path.</summary>
        public string ToLine()
        {
            var sb = new StringBuilder(96);
            sb.Append(GameIndex).Append('\t');
            sb.Append(PairIndex).Append('\t');
            sb.Append(SubjectId).Append('\t');
            sb.Append(OpponentId).Append('\t');
            sb.Append(SubjectIsWhite ? '1' : '0').Append('\t');
            sb.Append(PositionIndex).Append('\t');
            sb.Append((int)Outcome).Append('\t');
            sb.Append(PlyCount).Append('\t');
            sb.Append(ReachedPlyCap ? '1' : '0').Append('\t');
            sb.Append(ElapsedMs.ToString("F1", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Parses one line written by ToLine. Returns false instead of throwing when the
        /// line is malformed — the expected shape for the final line of a file a process was killed
        /// while writing, which a reader must treat as "not there" rather than a fatal error.</summary>
        public static bool TryParse(string line, out TournamentRunRecord record)
        {
            record = default;
            if (string.IsNullOrEmpty(line)) return false;

            string[] fields = line.Split('\t');
            if (fields.Length != 10) return false;

            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int gameIndex)) return false;
            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pairIndex)) return false;
            string subjectId = fields[2];
            string opponentId = fields[3];
            bool subjectIsWhite = fields[4] == "1";
            if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int positionIndex)) return false;
            if (!int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int outcomeRaw)) return false;
            if (!Enum.IsDefined(typeof(MatchOutcome), outcomeRaw)) return false;
            if (!int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int plyCount)) return false;
            bool reachedPlyCap = fields[8] == "1";
            if (!double.TryParse(fields[9], NumberStyles.Float, CultureInfo.InvariantCulture, out double elapsedMs)) return false;

            record = new TournamentRunRecord(gameIndex, pairIndex, subjectId, opponentId, subjectIsWhite,
                positionIndex, (MatchOutcome)outcomeRaw, plyCount, reachedPlyCap, elapsedMs);
            return true;
        }
    }
}
