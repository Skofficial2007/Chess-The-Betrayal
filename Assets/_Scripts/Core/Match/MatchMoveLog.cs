using System.Collections.Generic;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>One recorded ply: the move as it was applied, plus the game state it produced.
    /// GameState is Normal for intermediate Betrayal sub-phase moves (Act/Retribution) that don't
    /// end a turn on their own — only the ply that actually completes a turn carries a meaningful
    /// Check/Checkmate/Stalemate result.</summary>
    public readonly struct MoveLogEntry
    {
        public readonly MoveCommand Move;
        public readonly GameState ResultingState;
        public readonly string Notation;

        public MoveLogEntry(MoveCommand move, GameState resultingState, string notation)
        {
            Move = move;
            ResultingState = resultingState;
            Notation = notation;
        }

        public override string ToString() => Notation;
    }

    /// <summary>
    /// Full, ordered ply-by-ply history for one match. This is the thing to dump when something
    /// looks wrong (a bug report, a "why didn't the game end" investigation) — one line per ply,
    /// in the exact order moves were applied, independent of whatever Debug.Log calls happened to
    /// fire along the way. Unity-free, so it can be written to a file from a headless server the
    /// same way it's read from the Editor console.
    /// </summary>
    public sealed class MatchMoveLog
    {
        private readonly List<MoveLogEntry> _entries = new List<MoveLogEntry>(128);

        public IReadOnlyList<MoveLogEntry> Entries => _entries;

        public void Record(MoveCommand move, int fullMoveNumber, GameState resultingState)
        {
            string notation = MoveNotation.WithResultSuffix(MoveNotation.Format(move, fullMoveNumber), resultingState);
            _entries.Add(new MoveLogEntry(move, resultingState, notation));
        }

        public void Clear() => _entries.Clear();

        /// <summary>Full move history as one string, one ply per line — paste this into a bug report.</summary>
        public string DumpToString()
        {
            var sb = new System.Text.StringBuilder(_entries.Count * 24);
            for (int i = 0; i < _entries.Count; i++)
            {
                sb.AppendLine(_entries[i].Notation);
            }
            return sb.ToString();
        }
    }
}
