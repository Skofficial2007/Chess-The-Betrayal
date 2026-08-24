using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling.Match;

namespace ChessTheBetrayal.Tooling.Agreement
{
    /// <summary>
    /// Observes the positions a simulated game passes through so a caller can record training data
    /// (labelled by the game's eventual result) without MatchSimulator itself knowing anything about
    /// corpora, files, or threads. The simulator only decides WHICH positions are worth offering — the
    /// quiet ones, where a static evaluation is meaningful — and hands each one to this observer; what
    /// the observer does with it (buffer it, clone it, discard it) is entirely its own concern.
    ///
    /// This split is deliberate. The label a training corpus needs is the FINAL game outcome, which is
    /// not known until the game ends, so an observer must buffer a game's positions and stamp them once
    /// <see cref="OnGameComplete"/> fires. Keeping that buffering in the observer (rather than a list
    /// MatchSimulator fills) also keeps the simulator free of shared mutable state: under a parallel
    /// tournament each game gets its own simulator AND its own observer, so nothing an observer touches
    /// is shared across threads unless the observer itself chooses to share it.
    ///
    /// An observer must treat the BoardState handed to <see cref="OnQuietPosition"/> as borrowed and
    /// about to change: the simulator mutates that same instance on the very next move. An observer that
    /// needs to retain the position must snapshot it (BoardState.CloneForSnapshot) before returning.
    /// </summary>
    public interface IPositionSampler
    {
        /// <summary>
        /// Offered one quiet position the game is about to move from: the side to move is not in check,
        /// there is no Betrayer pending, and the board is settled in Normal phase (not mid-Betrayal
        /// sub-sequence). The board is the live, about-to-be-mutated instance — clone it to keep it.
        /// postDefectionOccurred is true once any Act earlier in THIS game resolved as a Defection (no
        /// legal Retribution existed) — the board's own piece placement already reflects the swapped
        /// team, but a consumer building a Betrayal-aware training corpus needs to know this position
        /// sits downstream of that swap without re-deriving it from move history.
        /// </summary>
        void OnQuietPosition(BoardState board, Team sideToMove, int ply, bool postDefectionOccurred);

        /// <summary>
        /// The game just ended with this outcome (from White's point of view). Fires exactly once per
        /// game, after the last <see cref="OnQuietPosition"/>, on every way a game can end — a decisive
        /// result, an early adjudication, or the ply cap. This is the signal to stamp the game's
        /// buffered positions with their training label and flush them.
        /// </summary>
        void OnGameComplete(MatchOutcome outcome);
    }
}
