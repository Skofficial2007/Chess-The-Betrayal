using System.Collections.Generic;
using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.Logic.Movement
{
    /// <summary>
    /// Factory for retrieving piece movement strategies.
    /// Uses the Strategy Pattern to decouple piece logic from the core engine.
    /// Caches strategy instances to prevent GC pressure.
    /// </summary>
    public static class MovementFactory
    {
        // Lazy-initialized strategy cache
        private static Dictionary<ChessPieceType, IPieceMovement> strategies;

        /// <summary>
        /// Initialize the strategy dictionary. Called automatically on first access.
        /// </summary>
        private static void Initialize()
        {
            if (strategies != null) return;

            strategies = new Dictionary<ChessPieceType, IPieceMovement>
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
            Initialize();

            if (strategies.TryGetValue(type, out IPieceMovement strategy))
            {
                return strategy;
            }

            return null;
        }

        /// <summary>
        /// Registers a custom piece movement strategy (for modding/custom pieces).
        /// Example: RegisterStrategy(ChessPieceType.Custom, new BetrayerMovement());
        /// </summary>
        public static void RegisterStrategy(ChessPieceType type, IPieceMovement strategy)
        {
            Initialize();
            strategies[type] = strategy;
        }

        /// <summary>
        /// Clears all registered strategies (useful for unit testing).
        /// </summary>
        public static void ClearStrategies()
        {
            strategies?.Clear();
            strategies = null;
        }
    }
}