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
    }
}
