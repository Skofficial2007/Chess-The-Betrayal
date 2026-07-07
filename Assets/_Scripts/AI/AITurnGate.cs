using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Pure decision logic for whether an AI agent should request a move right now. Lives here
    /// (Unity-free) rather than on GameManager so it's testable without a live scene — the
    /// question of "whose turn triggers the AI" is domain/AI policy, not Unity wiring.
    /// </summary>
    public static class AITurnGate
    {
        public static bool ShouldRequestMove(bool hasAgent, Team currentTurn, Team aiTeam, bool isGameActive) =>
            hasAgent && currentTurn == aiTeam && isGameActive;
    }
}
