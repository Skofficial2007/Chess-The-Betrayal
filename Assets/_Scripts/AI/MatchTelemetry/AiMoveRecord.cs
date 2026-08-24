using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI.MatchTelemetry
{
    /// <summary>
    /// What one AI move cost, during a real match rather than the synthetic device benchmark. A
    /// book move never runs a search, so it carries FromBook: true with ElapsedMs and
    /// CompletedDepth left at zero rather than a reading that would only be misleading.
    /// </summary>
    public readonly struct AiMoveRecord
    {
        public AiMoveRecord(int plyNumber, Team team, MoveCommand move, bool fromBook,
            int elapsedMs, int completedDepth, SearchStopReason stopReason)
        {
            PlyNumber = plyNumber;
            Team = team;
            Move = move;
            FromBook = fromBook;
            ElapsedMs = elapsedMs;
            CompletedDepth = completedDepth;
            StopReason = stopReason;
        }

        public int PlyNumber { get; }
        public Team Team { get; }
        public MoveCommand Move { get; }
        public bool FromBook { get; }
        public int ElapsedMs { get; }
        public int CompletedDepth { get; }
        public SearchStopReason StopReason { get; }
    }
}
