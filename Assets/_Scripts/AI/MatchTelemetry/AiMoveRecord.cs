using ChessTheBetrayal.AI.Search;
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
            int elapsedMs, int completedDepth, SearchStopReason stopReason, int gateHoldMs = 0,
            int depthLoopMs = 0)
        {
            PlyNumber = plyNumber;
            Team = team;
            Move = move;
            Source = source;
            ElapsedMs = elapsedMs;
            CompletedDepth = completedDepth;
            StopReason = stopReason;
            GateHoldMs = gateHoldMs;
            DepthLoopMs = depthLoopMs;
        }

        /// <summary>A ply the rules produced rather than either side choosing it.</summary>
        public static AiMoveRecord ForDefection(int plyNumber, Team defectedTo, MoveCommand move) =>
            new AiMoveRecord(plyNumber, defectedTo, move, AiMoveSource.Defection,
                elapsedMs: 0, completedDepth: 0, stopReason: SearchStopReason.Unset);

        /// <summary>
        /// The same record with the ply number it actually landed on. What a search cost is known
        /// the moment it finishes, but which ply it becomes is not — a move can wait behind an
        /// animation before it reaches the board — so the two are filled in at different moments.
        /// </summary>
        public AiMoveRecord WithPlyNumber(int plyNumber) =>
            new AiMoveRecord(plyNumber, Team, Move, Source, ElapsedMs, CompletedDepth, StopReason, GateHoldMs, DepthLoopMs);

        /// <summary>The same record with the wait between the move being decided and it reaching the
        /// board. Filled in at the same moment the ply number is, since both are only known once the
        /// move has actually landed.</summary>
        public AiMoveRecord WithGateHold(int gateHoldMs) =>
            new AiMoveRecord(PlyNumber, Team, Move, Source, ElapsedMs, CompletedDepth, StopReason, gateHoldMs, DepthLoopMs);

        public int PlyNumber { get; }

        /// <summary>Who made the move — or, for a Defection, whose army the piece landed in.</summary>
        public Team Team { get; }

        public MoveCommand Move { get; }
        public AiMoveSource Source { get; }
        public int ElapsedMs { get; }
        public int CompletedDepth { get; }
        public SearchStopReason StopReason { get; }

        /// <summary>
        /// How long the move waited between being decided and reaching the board. Moves are paced
        /// against whatever animation is still running, so a reply that arrives while the previous
        /// capture is still playing is held back - and the fastest replies are the ones most likely
        /// to be held, which is the opposite of what a bare elapsed time suggests. Zero when the
        /// board was free, which is most of the time.
        /// </summary>
        public int GateHoldMs { get; }

        /// <summary>
        /// How long the deepening loop took to reach <see cref="CompletedDepth"/>, out of the whole
        /// <see cref="ElapsedMs"/> - so a move that reports its ceiling after three seconds may have
        /// reached that ceiling in one of them. Nothing else in a report can tell those apart, and
        /// they mean opposite things about how hard the device was working.
        ///
        /// Only completed depths are timed, so a depth the clock cut short is not in here. Where the
        /// rest of the time went therefore depends on <see cref="StopReason"/>, which is why the
        /// report asks it rather than naming the tie-break pass on every line.
        /// </summary>
        public int DepthLoopMs { get; }
    }
}
