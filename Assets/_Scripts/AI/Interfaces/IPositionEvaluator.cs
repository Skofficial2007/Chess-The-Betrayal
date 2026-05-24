using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Scores a board position from one team's point of view. Positive means that team is winning, negative means they're losing.
    /// </summary>
    public interface IPositionEvaluator
    {
        int Evaluate(BoardState board, Team forTeam);
    }
}
