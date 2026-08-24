using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Engine.Movement
{
    /// <summary>
    /// Hands out the right movement rules for any piece type. Because each AI thread needs its own copy to work safely in parallel, we use [ThreadStatic] to give every thread its own private set.
    /// </summary>
    public static class MovementFactory
    {
        [ThreadStatic]
        private static Dictionary<ChessPieceType, IPieceMovement> _threadStrategies;

        /// <summary>
        /// Creates a fresh set of strategies for the calling thread.
        /// </summary>
        private static Dictionary<ChessPieceType, IPieceMovement> CreateStrategies()
        {
            return new Dictionary<ChessPieceType, IPieceMovement>
            {
                { ChessPieceType.Pawn, new PawnMovement() },
                { ChessPieceType.Knight, new KnightMovement() },
                { ChessPieceType.Rook, new RookMovement() },
                { ChessPieceType.Bishop, new BishopMovement() },
                { ChessPieceType.Queen, new QueenMovement() },
                { ChessPieceType.King, new KingMovement() }
            };
        }

        /// <summary>
        /// Retrieves the movement strategy for the specified piece type.
        /// Returns null if the piece type has no strategy (e.g., None).
        /// </summary>
        public static IPieceMovement GetStrategy(ChessPieceType type)
        {
            _threadStrategies ??= CreateStrategies();

            if (_threadStrategies.TryGetValue(type, out IPieceMovement strategy))
            {
                return strategy;
            }

            return null;
        }
    }
}