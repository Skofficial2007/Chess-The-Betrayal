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

        /// <summary>
        /// Same as <see cref="GetAllLegalMoves"/>, but also includes each piece's Betrayal Act
        /// moves (via GetBetrayalTargets) — GetAllLegalMoves deliberately omits these because
        /// GetForcedSaveMoves and HasAnyLegalMoves both reuse it and would be corrupted by an Act
        /// move leaking in (mislabeled as DefensiveOverride, or counted as a check-escape that
        /// isn't one). The search is the one caller that needs Act moves visible at every ply —
        /// as both the mover's own choice and the opponent's threat — so it uses this instead.
        /// </summary>
        void GetAllLegalMovesIncludingBetrayal(BoardState board, Team team, List<MoveCommand> masterBuffer);

        /// <summary>
        /// Same legal set as filtering <see cref="GetAllLegalMovesIncludingBetrayal"/> down to
        /// captures, promotions, and Acts, but generated directly at that cost — built for
        /// quiescence search, which only ever explores this subset and previously paid full
        /// movegen cost on every node just to filter it down afterward.
        /// </summary>
        void GetCapturesAndActsOnly(BoardState board, Team team, List<MoveCommand> masterBuffer);

        void GetRetributionMoves(BoardState board, Team executionerTeam, Vector2Int betrayerSquare, List<MoveCommand> output);
        void GetForcedSaveMoves(BoardState board, Team team, List<MoveCommand> output);
        bool IsKingInCheck(BoardState board, Team team);
        bool HasAnyLegalMoves(BoardState board, Team team);
        GameState EvaluateGameState(BoardState board, Team team, ClockState? clock = null);
        int GetMaterialAdvantage(BoardState board);

        /// <summary>
        /// Applies <paramref name="move"/> directly to <paramref name="board"/> without going through
        /// ITurnResolver.Advance (no NextTurn, no Betrayal sub-phase auto-resolution). This is the raw
        /// make-half of make/unmake: callers that own per-ply control over the sub-phase (an Alpha-Beta
        /// search) apply and undo moves themselves, deciding independently whether the turn flips.
        /// </summary>
        void ApplyMove(BoardState board, MoveCommand move);

        /// <summary>
        /// Rolls a move applied via <see cref="ApplyMove"/> back out, restoring the board exactly.
        /// This is the seam a search uses to explore thousands of positions without ever cloning the
        /// board — only <see cref="BoardState.CloneForSnapshot"/> is called once, up front.
        /// </summary>
        void UndoMove(BoardState board, MoveCommand move);
    }
}
