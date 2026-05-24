using System;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Movement
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

        /// <summary>
        /// Registers a custom piece movement strategy (for modding/custom pieces).
        /// Heads up: because of how [ThreadStatic] works, this only registers the strategy for whichever thread calls it.
        /// If you're registering custom pieces, make sure you do it on every thread that needs them.
        /// </summary>
        public static void RegisterStrategy(ChessPieceType type, IPieceMovement strategy)
        {
            _threadStrategies ??= CreateStrategies();
            _threadStrategies[type] = strategy;
        }

        /// <summary>
        /// Clears all registered strategies for the CURRENT thread.
        /// </summary>
        public static void ClearStrategies()
        {
            _threadStrategies?.Clear();
            _threadStrategies = null;
        }
    }
}