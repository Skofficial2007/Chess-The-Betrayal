using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.Network.Flow
{
    /// <summary>
    /// TODO(Multiplayer): request a rematch from the opponent under the same mode that was played,
    /// waiting for their acceptance before starting. Not wired up yet — bind this in place of
    /// BackToModeSelectAction once networked matches exist.
    /// </summary>
    public class RequestRematchAction : IPostGameAction
    {
        public void Execute(IMatchFlow flow, MatchResult result)
        {
            throw new System.NotImplementedException("RequestRematchAction is a stub for the future multiplayer game-context.");
        }
    }
}
