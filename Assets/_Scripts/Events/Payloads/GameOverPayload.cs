using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Events.Payloads
{
    /// <summary>
    /// Carries the game result. WinningTeam is null on a draw.
    /// </summary>
    public readonly struct GameOverPayload
    {
        public readonly Team? WinningTeam;
        public readonly GameEndReason Reason;
        public readonly bool IsTimeout;

        public GameOverPayload(Team? winner, GameEndReason reason, bool isTimeout)
        {
            WinningTeam = winner;
            Reason      = reason;
            IsTimeout   = isTimeout;
        }
    }

    public enum GameEndReason { Checkmate, Stalemate, Resignation, Timeout }
}
