using ChessTheMasterPiece.Data;

namespace ChessTheMasterPiece.AI
{
    /// <summary>
    /// Evaluates a board position from the given team's perspective.
    /// Positive = advantage for the requested team. Negative = disadvantage.
    /// Implementations: MaterialEvaluator, PositionalEvaluator, etc.
    /// </summary>
    public interface IPositionEvaluator
    {
        int Evaluate(BoardState board, Team forTeam);
    }
}
