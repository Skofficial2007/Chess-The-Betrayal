using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;

namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// Takes a move somebody has asked to play and finds the legal move that matches it on the board
    /// as it stands now, or says why there is no such move.
    ///
    /// Nothing inside the domain needs this: the engine only ever generates moves that are already
    /// legal, so a caller picking from that list cannot pick a wrong one. It is for the places a
    /// request arrives from outside and has to be taken on trust — a player answering a question
    /// that stayed on screen while the position moved on, and a server that holds the only board
    /// which counts while a client sends what it would like to happen.
    ///
    /// A request is matched on the four fields that identify a move: where it starts, where it ends,
    /// what it promotes to, and which part of a Betrayal it is. Two of those are easy to forget and
    /// both matter — the same two squares can be an ordinary capture or a Betrayal Act, and a pawn
    /// reaching the last rank offers four moves that differ in nothing else.
    ///
    /// A rules violation from the board itself is left to travel: that is a caller mistake rather
    /// than a rejected request, and the two are deliberately reported differently.
    /// </summary>
    public static class MoveRequest
    {
        public static DomainResult<MoveCommand> Resolve(
            IChessEngine engine,
            BoardState board,
            Vector2Int from,
            Vector2Int to,
            BetrayalStage stage = BetrayalStage.None,
            ChessPieceType promotedTo = ChessPieceType.None,
            List<MoveCommand> scratch = null)
        {
            List<MoveCommand> candidates = scratch ?? new List<MoveCommand>();
            candidates.Clear();
            engine.GetLegalMoves(board, from, candidates);

            for (int i = 0; i < candidates.Count; i++)
            {
                MoveCommand candidate = candidates[i];

                if (candidate.EndPosition == to
                    && candidate.Stage == stage
                    && candidate.PromotedTo == promotedTo)
                {
                    return DomainResult<MoveCommand>.Success(candidate);
                }
            }

            return DomainResult<MoveCommand>.Failure(
                DomainEventCode.Engine_IllegalMoveRequested,
                $"{from} to {to} is not legal here (stage {stage}, promoting to {promotedTo}).");
        }
    }
}
