using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Match
{
    /// <summary>
    /// The seam a post-game action needs to drive whatever happens next, without depending on
    /// GameManager/UIManager (MonoBehaviours) directly. GameManager implements this.
    /// </summary>
    public interface IMatchFlow
    {
        /// <summary>Tears down the current match's board, clock, and executor wiring.</summary>
        void TearDownCurrentMatch();

        /// <summary>Starts a fresh match under the given mode. Never falls back to a hidden default.</summary>
        void StartNewMatch(GameModeConfig mode);

        /// <summary>Returns to the mode-select screen instead of starting a match immediately.</summary>
        void ReturnToModeSelect();

        /// <summary>Returns to the Practice Match Setup (AI Settings) screen instead of the normal
        /// mode-select screen — the Replay destination for AI practice matches, which never went
        /// through mode-select in the first place.</summary>
        void ReturnToAIMatchSettings();

        /// <summary>
        /// The player dismissed the Game Over screen. Runs the bound post-game action (e.g. back to
        /// mode select). Exposed here so UIManager drives it through this seam instead of resolving
        /// the concrete GameManager — keeps the UI assembly off any upward dependency.
        /// </summary>
        void AcknowledgeGameOver();
    }
}
