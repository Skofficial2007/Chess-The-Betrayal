using System.Collections.Generic;
using UnityEngine;

namespace ChessTheBetrayal.EditorTools.Benchmark
{
    /// <summary>
    /// Logs one readable line per finished game — pairing, result, running score for that pairing,
    /// and elapsed wall time for the game — by subscribing to TournamentSession.OnGameCompleted.
    /// A bare "N/total" counter proves the run is alive, but it can't tell a human whether the
    /// numbers so far look healthy; this is the difference between "still moving" and "still moving
    /// AND on track."
    ///
    /// Deliberately separate from ITournamentProgress: that interface is called from worker threads
    /// with no game details, by design (see ParallelTournamentExecutor). OnGameCompleted fires only
    /// on the fold thread, in original game order, after ApplyCompletedGame has already updated the
    /// session's tallies — exactly the point where "this pairing is now N-M-D" is available.
    /// </summary>
    public sealed class TournamentGameLogger
    {
        private readonly string _label;

        /// <summary>Keyed by PairIndex alone, never by which id happened to play White this game —
        /// TournamentSession plays the SAME pairing with the subject on both colors specifically to
        /// cancel first-move advantage (see CuratedPositionSuite), so a pairing's running score must
        /// stay one running total across both colors, not split into two depending on who's White.</summary>
        private readonly Dictionary<int, (string SubjectId, string OpponentId, int Wins, int Losses, int Draws)> _runningScores = new();

        public TournamentGameLogger(string label)
        {
            _label = label;
        }

        public void HandleGameCompleted(TournamentGameRecord record)
        {
            string subjectId = record.SubjectIsWhite ? record.WhiteId : record.BlackId;
            string opponentId = record.SubjectIsWhite ? record.BlackId : record.WhiteId;

            _runningScores.TryGetValue(record.PairIndex, out var score);
            score.SubjectId = subjectId;
            score.OpponentId = opponentId;
            bool subjectWon = record.SubjectIsWhite
                ? record.Result.Result.Outcome == Tests.Utilities.MatchOutcome.WhiteWon
                : record.Result.Result.Outcome == Tests.Utilities.MatchOutcome.BlackWon;
            bool opponentWon = record.SubjectIsWhite
                ? record.Result.Result.Outcome == Tests.Utilities.MatchOutcome.BlackWon
                : record.Result.Result.Outcome == Tests.Utilities.MatchOutcome.WhiteWon;

            if (subjectWon) score.Wins++;
            else if (opponentWon) score.Losses++;
            else score.Draws++;
            _runningScores[record.PairIndex] = score;

            double elapsedMs = record.Result.WhiteStats.TotalElapsedMs + record.Result.BlackStats.TotalElapsedMs;
            string resultWord = subjectWon ? "win" : opponentWon ? "loss" : "draw";

            Debug.Log($"[{_label}] {score.SubjectId} vs {score.OpponentId} — {resultWord} " +
                $"(running {score.Wins}-{score.Losses}-{score.Draws}), {elapsedMs / 1000.0:F1}s");
        }
    }
}
