using System;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tooling.Strength
{
    /// <summary>
    /// What "won" means for a conversion position, and therefore what counts as progress toward
    /// converting it. Unlike YardstickPosition (one exact move, provably the only correct answer)
    /// a conversion position has NO single correct move — many different move orders drive a lone
    /// king to the edge or escort a pawn home equally well. So a conversion position is judged by a
    /// PROGRESS METRIC over many moves, not one move's identity — the KQK/KRK fixture trap.
    /// </summary>
    public enum ConversionGoal
    {
        /// <summary>The side with mating material must drive the lone enemy king toward the edge of
        /// the board and deliver mate. Progress = the lone king's distance-to-edge shrinking over
        /// time, verdict = mate reached within the ply budget.</summary>
        DriveLoneKingToMate,

        /// <summary>The side with an extra, unstoppable pawn must promote it. Progress = the pawn's
        /// distance-to-promotion shrinking, verdict = a queen (or better) appears on the board for
        /// the attacking side within the ply budget.</summary>
        PromoteThePawn,
    }

    /// <summary>
    /// One hand-authored endgame with a won-but-not-immediate goal, played out move by move by the
    /// real search rather than judged on a single move. Every position here must be proven WON at
    /// authoring time (see EndgameConversionProofTests) the same way YardstickSuite positions are
    /// proven correct — an unprovable position is a fixture bug, not a looser case to keep.
    /// </summary>
    public sealed class EndgameConversionPosition
    {
        public readonly string Name;
        public readonly ConversionGoal Goal;
        public readonly Team AttackingTeam;
        public readonly string Note;
        private readonly Func<BoardState> _buildBoard;

        public EndgameConversionPosition(string name, ConversionGoal goal, Team attackingTeam, string note,
            Func<BoardState> buildBoard)
        {
            Name = name;
            Goal = goal;
            AttackingTeam = attackingTeam;
            Note = note;
            _buildBoard = buildBoard;
        }

        /// <summary>Builds a fresh board each call — a driving loop mutates the board across dozens
        /// of plies, so every caller (the authoring-time proof AND the AI run) needs its own
        /// independent instance. Never share one BoardState between two probes.</summary>
        public BoardState BuildBoard() => _buildBoard();
    }
}
