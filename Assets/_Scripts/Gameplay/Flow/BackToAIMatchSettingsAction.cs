using ChessTheBetrayal.Core.Match;

namespace ChessTheBetrayal.Gameplay.Flow
{
    /// <summary>
    /// AI practice-match post-game behavior: tear down the finished match and return to the
    /// Practice Match Setup (AI Settings) screen instead of the normal Mode Select screen — a
    /// practice match never went through Mode Select in the first place (it's hardcoded Ultimate),
    /// so Replay must not send the player somewhere they never came from. See BackToModeSelectAction
    /// for the plain-match counterpart; GameManager.AcknowledgeGameOver picks between the two based
    /// on MatchFlowCoordinator.IsAiMode.
    /// </summary>
    public class BackToAIMatchSettingsAction : IPostGameAction
    {
        public void Execute(IMatchFlow flow, MatchResult result)
        {
            flow.TearDownCurrentMatch();
            flow.ReturnToAIMatchSettings();
        }
    }
}
