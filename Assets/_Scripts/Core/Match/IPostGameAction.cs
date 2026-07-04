namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// What happens when the player dismisses the Game Over screen. Swap the bound implementation
    /// per game-context (prototype/AI/multiplayer) instead of branching on a mode flag — see
    /// BackToModeSelectAction, RematchSameModeAction (AI, stub), RequestRematchAction (MP, stub).
    /// </summary>
    public interface IPostGameAction
    {
        void Execute(IMatchFlow flow, MatchResult result);
    }
}
