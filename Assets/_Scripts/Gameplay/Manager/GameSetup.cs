using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Logic;

namespace ChessTheBetrayal.Gameplay
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
        // The standard piece order for the back rank, left to right.
        private static readonly ChessPieceType[] StandardBackRank =
        {
            ChessPieceType.Rook,   ChessPieceType.Knight, ChessPieceType.Bishop,
            ChessPieceType.Queen,  ChessPieceType.King,   ChessPieceType.Bishop,
            ChessPieceType.Knight, ChessPieceType.Rook
        };

        private readonly bool _logMoves;

        public GameSetup(bool logMoves)
        {
            _logMoves = logMoves;
        }

        /// <summary>
        /// Rolls which team the human player controls and which side moves first. Per the pitch
        /// doc, first-mover is also random — deliberately independent of player-team assignment —
        /// so neither player can rely on opening book knowledge from knowing they're White.
        /// </summary>
        public (Team playerTeam, Team firstMover) RollTeams()
        {
            Team playerTeam = UnityEngine.Random.value > 0.5f ? Team.White : Team.Black;
            Team firstMover = UnityEngine.Random.value > 0.5f ? Team.White : Team.Black;

            if (_logMoves)
            {
                Debug.Log($"[GameSetup] Roll Decided -> Player Team: {playerTeam} | First Mover: {firstMover}");
            }

            return (playerTeam, firstMover);
        }

        /// <summary>
        /// Fills the board with pieces in the standard starting configuration and recomputes its
        /// Zobrist hash from scratch. Does not spawn any GameObjects — that's BoardVisuals' job
        /// when it receives OnGameStarted.
        /// </summary>
        public void PlaceStandardPieces(BoardState board, int boardSizeX, int boardSizeY)
        {
            for (int x = 0; x < boardSizeX; x++)
            {
                board.SetPiece(new PieceData(Team.White, StandardBackRank[x], moveDirection: 1, startRow: 0), x, 0);
                board.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1), x, 1);
            }

            for (int x = 0; x < boardSizeX; x++)
            {
                board.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, moveDirection: -1, startRow: boardSizeY - 2), x, boardSizeY - 2);
                board.SetPiece(new PieceData(Team.Black, StandardBackRank[x], moveDirection: -1, startRow: boardSizeY - 1), x, boardSizeY - 1);
            }

            board.ComputeFullZobristHash();
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
