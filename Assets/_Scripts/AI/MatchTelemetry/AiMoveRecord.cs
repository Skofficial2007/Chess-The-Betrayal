using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI.MatchTelemetry
{
    /// <summary>Where a recorded ply came from. Only a Searched one has an elapsed time and a
    /// depth to report; the other two would carry zeros that read as a very fast, very shallow
    /// search rather than as no search at all.</summary>
    public enum AiMoveSource
    {
        /// <summary>The AI worked the move out for itself.</summary>
        Searched,

        /// <summary>The opening book answered instantly, with no search.</summary>
        Book,

        /// <summary>A Betrayer changed sides. Nobody chose this move — the rules produced it once a
        /// Retribution was refused or impossible — but it spends a ply and moves a piece between
        /// the two armies, so a log without it cannot account for the board it describes.</summary>
        Defection,
    }

    /// <summary>
    /// One ply of a real match, as opposed to the synthetic device benchmark. Elapsed time and
    /// depth are only meaningful for a searched move; see <see cref="AiMoveSource"/>.
    /// </summary>
    public readonly struct AiMoveRecord
    {
        public AiMoveRecord(int plyNumber, Team team, MoveCommand move, AiMoveSource source,
            int elapsedMs, int completedDepth, SearchStopReason stopReason)
        {
            PlyNumber = plyNumber;
            Team = team;
            Move = move;
            Source = source;
            ElapsedMs = elapsedMs;
            CompletedDepth = completedDepth;
            StopReason = stopReason;
        }

        /// <summary>A ply the rules produced rather than either side choosing it.</summary>
        public static AiMoveRecord ForDefection(int plyNumber, Team defectedTo, MoveCommand move) =>
            new AiMoveRecord(plyNumber, defectedTo, move, AiMoveSource.Defection,
                elapsedMs: 0, completedDepth: 0, stopReason: SearchStopReason.Unset);

        public int PlyNumber { get; }

        /// <summary>Who made the move — or, for a Defection, whose army the piece landed in.</summary>
        public Team Team { get; }

        public MoveCommand Move { get; }
        public AiMoveSource Source { get; }
        public int ElapsedMs { get; }
        public int CompletedDepth { get; }
        public SearchStopReason StopReason { get; }
    }
}
