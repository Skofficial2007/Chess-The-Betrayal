using System;
using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Factory for retrieving piece movement strategies.
    /// Uses the Strategy Pattern to decouple piece logic from the core engine.
    /// Thread-safe: Uses [ThreadStatic] to provide lock-free caching for multi-threaded AI.
    /// </summary>
    public static class MovementFactory
    {
        // ThreadStatic ensures each background thread (and the main thread) 
        // gets its own completely isolated instance of this dictionary.
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
            // The null-coalescing assignment operator (??=) is a clean C# 8+ 
            // way to initialize only if null on the current thread.
            _threadStrategies ??= CreateStrategies();

            if (_threadStrategies.TryGetValue(type, out IPieceMovement strategy))
            {
                return strategy;
            }

            return null;
        }

        /// <summary>
        /// Registers a custom piece movement strategy (for modding/custom pieces).
        /// NOTE: Because of ThreadStatic, this only registers the strategy on the CURRENT thread.
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