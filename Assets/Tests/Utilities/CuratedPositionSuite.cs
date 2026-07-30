using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// The strength harness's view of the curated early-middlegame positions. The lines themselves and
    /// the replay live in <see cref="CuratedOpeningLines"/>, in the AI assembly, because the on-device
    /// benchmark needs the same positions and runs where this editor-only assembly does not exist.
    /// This stays as the name the harness and the tournament tooling already call, so there is one set
    /// of positions rather than two that agree until they don't.
    ///
    /// A tournament run always plays each position from both colors (see MatchSimulator callers), which
    /// cancels the first-move advantage baked into any single position — the position count times two is
    /// the tournament's real sample size for a deterministic (zero-blunder, zero-tie-break-window)
    /// profile pair, since a single fixed position played once is otherwise not "N independent games"
    /// at all.
    /// </summary>
    public static class CuratedPositionSuite
    {
        public static int Count => CuratedOpeningLines.Count;

        /// <summary>Replays position <paramref name="index"/>'s line from the standard start and returns
        /// the resulting board, Betrayal enabled and turn/Zobrist state fully consistent. Throws if the
        /// authored line is somehow no longer legal against the current engine — the same fail-loud
        /// contract the opening book compiler uses, since a silently-skipped bad line would quietly
        /// shrink the suite's sample size.</summary>
        public static BoardState Build(int index) => CuratedOpeningLines.BuildPosition(index);
    }
}
