using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Core.Logic
{
    /// <summary>
    /// Supplies the current clock snapshot to anything that needs to stamp a move with it
    /// (e.g. LocalMoveExecutor). Exists so those call sites depend on an injected source
    /// instead of reaching into a GameManager.Instance singleton — required for a headless
    /// server hosting multiple matches, and for testing without a MonoBehaviour in play.
    /// </summary>
    public interface IClockSnapshotSource
    {
        /// <summary>Null when no clock is active (e.g. untimed or AI mode).</summary>
        ClockState? Current { get; }
    }
}
