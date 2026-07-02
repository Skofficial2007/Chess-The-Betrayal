using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// Instance-scoped seam over the chess rules engine. Call sites (GameManager, a future
    /// server, the AI) depend on this interface rather than the static ChessEngine class or a
    /// GameManager.Instance singleton, so a headless server can hold one instance per match and
    /// tests can substitute a fake without touching process-global state.
    ///
    /// See <see cref="ChessEngineAdapter"/> for the production implementation.
    /// </summary>
    public interface IChessEngine : ITurnResolver
    {
        void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output);
        void GetAllLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer);
        void GetRetributionMoves(BoardState board, Team executionerTeam, Vector2Int betrayerSquare, List<MoveCommand> output);
        void GetForcedSaveMoves(BoardState board, Team team, List<MoveCommand> output);
        bool IsKingInCheck(BoardState board, Team team);
        bool HasAnyLegalMoves(BoardState board, Team team);
        GameState EvaluateGameState(BoardState board, Team team, ClockState? clock = null);
        int GetMaterialAdvantage(BoardState board);
    }
}
