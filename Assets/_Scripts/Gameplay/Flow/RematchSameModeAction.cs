using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.Gameplay.Flow
{
    /// <summary>
    /// TODO(AI mode): rematch immediately, always Ultimate/untimed, no mode-select detour.
    /// Not wired up yet — bind this in place of BackToModeSelectAction once AI opponents exist.
    /// </summary>
    public class RematchSameModeAction : IPostGameAction
    {
        public void Execute(IMatchFlow flow, MatchResult result)
        {
            throw new System.NotImplementedException("RematchSameModeAction is a stub for the future AI game-context.");
        }
    }
}
