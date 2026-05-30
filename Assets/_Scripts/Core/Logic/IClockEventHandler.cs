using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Logic
{
    /// <summary>
    /// Defines the callback contract for clock lifecycle events.
    /// Implemented by the presentation layer (e.g., GameManager) to react to domain timing events
    /// without coupling the core logic to the game engine.
    /// </summary>
    public interface IClockEventHandler
    {
        /// <summary>
        /// Invoked exactly once when a player's remaining time reaches zero.
        /// The implementor is responsible for resolving the game outcome (e.g., checking for insufficient material).
        /// </summary>
        void OnClockTimeout(Team timedOutTeam);

        /// <summary>
        /// Invoked exactly once per team per game when their remaining time drops below the urgency threshold.
        /// Used to trigger visual or audio cues in the UI layer.
        /// </summary>
        void OnLowTimeWarning(Team team, long remainingMs);
    }
}
