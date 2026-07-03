using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.Gameplay.Flow
{
    /// <summary>
    /// Prototype post-game behavior: tear down the finished match and return to the Mode
    /// Select screen so the next mode is an explicit player choice, never an implicit default.
    /// </summary>
    public class BackToModeSelectAction : IPostGameAction
    {
        public void Execute(IMatchFlow flow, MatchResult result)
        {
            flow.TearDownCurrentMatch();
            flow.ReturnToModeSelect();
        }
    }
}
