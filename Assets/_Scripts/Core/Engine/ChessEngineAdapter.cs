using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Engine
{
    /// <summary>
    /// Default <see cref="IChessEngine"/> implementation, backed by the static ChessEngine rules
    /// engine and a <see cref="TurnResolver"/>. The rules logic itself is not being rewritten here —
    /// only the seam call sites depend on, so it can later be swapped for a server-hosted or
    /// AI-search-tuned implementation without touching GameManager or LocalMoveExecutor.
    /// </summary>
    public sealed class ChessEngineAdapter : IChessEngine
    {
        private readonly TurnResolver _turnResolver = new TurnResolver();

        public TurnAdvanceResult Advance(BoardState board, MoveCommand move) => _turnResolver.Advance(board, move);

        public TurnAdvanceResult ResolveVoluntaryDefection(BoardState board) => _turnResolver.ResolveVoluntaryDefection(board);

        public void GetLegalMoves(BoardState board, Vector2Int position, List<MoveCommand> output) =>
            ChessEngine.GetLegalMoves(board, position, output);

        public void GetAllLegalMoves(BoardState board, Team team, List<MoveCommand> masterBuffer) =>
            ChessEngine.GetAllLegalMoves(board, team, masterBuffer);

        public void GetAllLegalMovesIncludingBetrayal(BoardState board, Team team, List<MoveCommand> masterBuffer) =>
            ChessEngine.GetAllLegalMovesIncludingBetrayal(board, team, masterBuffer);

        public void GetRetributionMoves(BoardState board, Team executionerTeam, Vector2Int betrayerSquare, List<MoveCommand> output) =>
            ChessEngine.GetRetributionMoves(board, executionerTeam, betrayerSquare, output);

        public void GetForcedSaveMoves(BoardState board, Team team, List<MoveCommand> output) =>
            ChessEngine.GetForcedSaveMoves(board, team, output);

        public bool IsKingInCheck(BoardState board, Team team) => ChessEngine.IsKingInCheck(board, team);

        public bool HasAnyLegalMoves(BoardState board, Team team) => ChessEngine.HasAnyLegalMoves(board, team);

        public GameState EvaluateGameState(BoardState board, Team team, ClockState? clock = null) =>
            ChessEngine.EvaluateGameState(board, team, clock);

        public int GetMaterialAdvantage(BoardState board) => ChessEngine.GetMaterialAdvantage(board);

        public void ApplyMove(BoardState board, MoveCommand move) => ChessEngine.ApplyMoveToBoard(board, move);

        public void UndoMove(BoardState board, MoveCommand move) => ChessEngine.UndoMoveOnBoard(board, move);
    }
}
