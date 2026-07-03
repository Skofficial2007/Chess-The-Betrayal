using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// Immutable summary of how a match ended, along with the config it was played under.
    /// Handed to IPostGameAction so it can decide what mode (if any) to carry into the next match.
    /// </summary>
    public readonly struct MatchResult
    {
        public readonly Team?          WinningTeam;
        public readonly bool           IsTimeout;
        public readonly GameModeConfig PlayedMode;

        public MatchResult(Team? winningTeam, bool isTimeout, GameModeConfig playedMode)
        {
            WinningTeam = winningTeam;
            IsTimeout   = isTimeout;
            PlayedMode  = playedMode;
        }
    }
}
