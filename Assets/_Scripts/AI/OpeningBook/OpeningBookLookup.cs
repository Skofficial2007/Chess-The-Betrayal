using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;

namespace ChessTheBetrayal.AI.OpeningBook
{
    /// <summary>
    /// Reads an OpeningBookAsset at match time: given the position the search would otherwise be
    /// asked to solve, finds every book entry sharing that Zobrist hash (there can be more than
    /// one — see OpeningBookAsset's own doc comment) and, among those whose packed move still
    /// matches a currently legal move, picks one at random weighted by book frequency. Never
    /// invents a move — a hit is always re-validated against GetAllLegalMovesIncludingBetrayal
    /// before being trusted, the same defensive pattern OpeningBookCompiler.FindMatch uses, since a
    /// packed move is just 19 bits with no guarantee of legality in a hash-collided position.
    /// </summary>
    public static class OpeningBookLookup
    {
        /// <summary>
        /// Returns the book's chosen reply for <paramref name="board"/>, or null if the book has
        /// nothing usable here: wrong scheme version, no entries for this hash, a Betrayal
        /// sequence already in progress (the book only ever covers plain opening theory), or every
        /// candidate entry's packed move fails to match a currently legal move.
        /// </summary>
        public static MoveCommand? TryGetBookMove(
            OpeningBookAsset book, BoardState board, IChessEngine engine, IRandomSource rng)
        {
            if (book == null || book.EntryCount == 0) return null;
            if (book.SchemeVersion != BoardState.ZobristSchemeVersion) return null;
            if (board.BetrayalInitiator != null || board.PendingBetrayerSquare != null) return null;

            (int start, int count) = FindRun(book, board.ZobristHash);
            if (count == 0) return null;

            var legalMoves = new List<MoveCommand>();
            engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

            var candidates = new List<MoveCommand>(count);
            var candidateWeights = new List<int>(count);
            int totalWeight = 0;

            for (int i = start; i < start + count; i++)
            {
                uint packedMove = book.PackedMoveAt(i);
                MoveCommand? match = FindMatch(legalMoves, packedMove);
                if (match == null) continue;

                candidates.Add(match.Value);
                candidateWeights.Add(book.WeightAt(i));
                totalWeight += book.WeightAt(i);
            }

            if (candidates.Count == 0) return null;

            int roll = rng.NextInt(totalWeight);
            int cumulative = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += candidateWeights[i];
                if (roll < cumulative) return candidates[i];
            }

            return candidates[candidates.Count - 1];
        }

        /// <summary>Binary-searches the sorted Keys array for the first entry matching hash, then
        /// widens to the full contiguous run of entries sharing it (see class doc comment).</summary>
        private static (int Start, int Count) FindRun(OpeningBookAsset book, ulong hash)
        {
            int low = 0, high = book.EntryCount - 1, firstIndex = -1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                ulong midKey = book.KeyAt(mid);
                if (midKey == hash)
                {
                    firstIndex = mid;
                    high = mid - 1; // keep searching left for the run's true start
                }
                else if (midKey < hash)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            if (firstIndex < 0) return (0, 0);

            int runEnd = firstIndex;
            while (runEnd + 1 < book.EntryCount && book.KeyAt(runEnd + 1) == hash) runEnd++;

            return (firstIndex, runEnd - firstIndex + 1);
        }

        private static MoveCommand? FindMatch(List<MoveCommand> legalMoves, uint packedMove)
        {
            foreach (MoveCommand candidate in legalMoves)
            {
                if (AlphaBetaSearch.PackMove(candidate) == packedMove) return candidate;
            }

            return null;
        }
    }
}
