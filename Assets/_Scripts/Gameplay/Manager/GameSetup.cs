using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Core.Randomness;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Builds the initial state for a new match: rolls player/first-mover team, places the
    /// standard starting position, and constructs the clock for the selected game mode.
    ///
    /// This is the "before the first move" half of what GameManager used to do inline. It owns
    /// no Unity lifecycle of its own — GameManager calls it once per new game and holds the
    /// results (LiveBoard, PlayerTeam, the constructed ChessClock/GameClockController).
    ///
    /// Kept separate from <see cref="MatchDriver"/> so a rules bug (wrong move resolution) and a
    /// setup bug (wrong starting position, wrong clock config) are never the same file: if the
    /// board looks wrong before move one is played, the bug is here; if it goes wrong after, it's
    /// in MatchDriver.
    /// </summary>
    public sealed class GameSetup
    {
        private readonly bool _logMoves;
        private readonly IRandomSource _rng;
        private readonly IFirstMoverPolicy _firstMoverPolicy;

        public GameSetup(bool logMoves)
            : this(logMoves, new SystemRandomSource(), new RandomFirstMoverPolicy())
        {
        }

        public GameSetup(bool logMoves, IRandomSource rng, IFirstMoverPolicy firstMoverPolicy)
        {
            _logMoves = logMoves;
            _rng = rng;
            _firstMoverPolicy = firstMoverPolicy;
        }

        /// <summary>
        /// Rolls which team the human player (Seat.PlayerA) controls. White always moves first —
        /// orthodox chess rules are untouched — so firstMover is always Team.White; only the
        /// seat-to-color mapping is randomized, via IFirstMoverPolicy, so neither player can rely
        /// on opening book knowledge from knowing they're White ahead of the roll.
        /// </summary>
        public (Team playerTeam, Team firstMover) RollTeams()
        {
            SideAssignment assignment = _firstMoverPolicy.Assign(_rng);
            Team playerTeam = assignment.White == Seat.PlayerA ? Team.White : Team.Black;
            Team firstMover = Team.White;

            if (_logMoves)
            {
                Debug.Log($"[GameSetup] Roll Decided -> Player Team: {playerTeam} | First Mover: {firstMover}");
            }

            return (playerTeam, firstMover);
        }

        /// <summary>
        /// Puts the board into the standard starting configuration and recomputes its Zobrist hash
        /// from scratch. Where the pieces go is Core's rule, shared with the board the search and
        /// the opening book are built against; what a new match then has to reset around them is
        /// this method's own business. Does not spawn any GameObjects — that's BoardVisuals' job
        /// when it receives OnGameStarted.
        /// </summary>
        public void PlaceStandardPieces(BoardState board)
        {
            StandardChessPosition.PlacePieces(board);

            board.ComputeFullZobristHash();

            // The opening position counts towards a repetition like any other, and a board reused
            // for a second match must not carry the first one's positions into it. Recorded here,
            // once the hash it is recorded under is the real one — every position after this is
            // recorded by the move that reaches it.
            board.ClearPositionHistory();
            board.PushPosition(board.ZobristHash, irreversible: true);
        }

        /// <summary>
        /// Constructs the pure-C# ChessClock and attaches its MonoBehaviour tick bridge to
        /// <paramref name="host"/>. Returns (null, null) for AI sessions or Unlimited mode, where
        /// there is deliberately no clock at all — bypassed entirely to preserve search performance.
        /// </summary>
        /// <param name="host">The GameObject the GameClockController component is attached to (GameManager's own).</param>
        /// <param name="existingController">Reuse this controller if one is already attached, instead of adding a duplicate component.</param>
        public (ChessClock clock, GameClockController controller) InitializeClock(
            GameModeConfig selectedMode,
            bool isAIMode,
            Team initialActiveSide,
            IClockEventHandler clockEventHandler,
            GameObject host,
            GameClockController existingController)
        {
            if (isAIMode || selectedMode.IsUnlimited)
            {
                return (null, null);
            }

            ChessClock clock = new ChessClock(selectedMode, clockEventHandler, initialActiveSide);

            GameClockController controller = existingController != null
                ? existingController
                : host.AddComponent<GameClockController>();

            controller.Initialize(clock);

            return (clock, controller);
        }
    }
}
