using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Watches one game ply by ply and decides whether an AdjudicationRules early-stopping
    /// condition has fired — threefold repetition, the fifty-move rule, or a sustained large/small
    /// evaluation score. Not a domain rule: this is harness-side arbiter logic MatchSimulator
    /// consults between moves, the same role a chess-engine testing tool's own adjudicator plays
    /// around two engines that have no repetition/fifty-move awareness of their own.
    ///
    /// One instance is scoped to exactly one game — its repetition table and streak counters have
    /// no meaning across games, so MatchSimulator constructs a fresh adjudicator per PlayGame call
    /// rather than trying to reuse one (unlike the transposition tables, which are wiped-and-reused
    /// for cost reasons; this type is cheap and reuse would just be extra bookkeeping to reset).
    /// </summary>
    public sealed class MatchAdjudicator
    {
        private readonly AdjudicationRules _rules;
        private readonly Dictionary<ulong, int> _positionCounts = new Dictionary<ulong, int>();
        private int _pliesSinceProgress;
        private int _winStreakPlies;
        private Team _winStreakFavors;
        private int _drawStreakPlies;

        public MatchAdjudicator(AdjudicationRules rules)
        {
            _rules = rules;
        }

        /// <summary>Records the starting position before any move is played — a repetition counts
        /// the position that recurs, not just the moves that led to it, so the starting position
        /// itself is part of the count.</summary>
        public void RecordStartingPosition(BoardState board)
        {
            RecordPositionForRepetition(board);
        }

        /// <summary>
        /// Call once per applied ply, after the move is on the board. Returns a verdict once an
        /// early-stopping condition fires; returns null while the game should keep playing
        /// normally. plyIndex is 0-based from the game's start (matches MatchSimulator's own ply
        /// loop counter) so score adjudication's MinPlyForScoreAdjudication gate lines up with
        /// "how many plies have actually been played," not a ply-cap-relative count.
        /// </summary>
        public MatchOutcome? RecordPly(BoardState board, MoveCommand move, int plyIndex, int scoreForWhiteCp)
        {
            RecordPositionForRepetition(board);
            if (_positionCounts[board.ZobristHash] >= _rules.ThreefoldRepetitionCount)
                return MatchOutcome.Draw;

            if (move.IsCapture || move.PieceType == ChessPieceType.Pawn)
                _pliesSinceProgress = 0;
            else
                _pliesSinceProgress++;
            if (_pliesSinceProgress >= _rules.FiftyMoveRulePlies)
                return MatchOutcome.Draw;

            if (plyIndex < _rules.MinPlyForScoreAdjudication) return null;

            int absScore = System.Math.Abs(scoreForWhiteCp);
            if (absScore >= _rules.WinAdjudicationMarginCp)
            {
                Team favors = scoreForWhiteCp > 0 ? Team.White : Team.Black;
                if (_winStreakPlies > 0 && _winStreakFavors == favors)
                    _winStreakPlies++;
                else
                {
                    _winStreakPlies = 1;
                    _winStreakFavors = favors;
                }

                if (_winStreakPlies >= _rules.WinAdjudicationConsecutivePlies)
                    return favors == Team.White ? MatchOutcome.WhiteWon : MatchOutcome.BlackWon;
            }
            else
            {
                _winStreakPlies = 0;
            }

            if (absScore <= _rules.DrawAdjudicationMarginCp)
            {
                _drawStreakPlies++;
                if (_drawStreakPlies >= _rules.DrawAdjudicationConsecutivePlies)
                    return MatchOutcome.Draw;
            }
            else
            {
                _drawStreakPlies = 0;
            }

            return null;
        }

        private void RecordPositionForRepetition(BoardState board)
        {
            _positionCounts.TryGetValue(board.ZobristHash, out int count);
            _positionCounts[board.ZobristHash] = count + 1;
        }
    }
}
