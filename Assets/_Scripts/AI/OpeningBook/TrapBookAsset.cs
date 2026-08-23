using UnityEngine;

namespace ChessTheBetrayal.AI.OpeningBook
{
    /// <summary>
    /// A compiled set of known opening traps: positions where one natural-looking move loses
    /// material or gets mated, paired with the move to play instead.
    ///
    /// Deliberately a separate asset from the opening book rather than extra columns on it,
    /// because it answers a different question. The opening book knows what to play; this knows
    /// what must never be played, and what the mistake is called. Keeping them apart means the
    /// book's format does not have to grow a notion of "a move recorded so it can be avoided",
    /// which is the one thing that format cannot express: it stores every move of a line as a move
    /// to play, with no idea which side a line was written for. A trap line pasted into it would
    /// teach the blunder just as reliably as the refutation.
    ///
    /// The arrays are parallel and sorted by PositionKey so a lookup can binary-search. Unlike the
    /// opening book a position appears at most once — a position has one best move here, and two
    /// records claiming otherwise is a contradiction the compiler rejects rather than stores.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/AI/Trap Book", fileName = "TrapBook")]
    public sealed class TrapBookAsset : ScriptableObject
    {
        [SerializeField] private ulong[] _positionKeys = System.Array.Empty<ulong>();
        [SerializeField] private uint[] _blunderMoves = System.Array.Empty<uint>();
        [SerializeField] private uint[] _bestMoves = System.Array.Empty<uint>();
        [SerializeField] private string[] _names = System.Array.Empty<string>();

        /// <summary>
        /// Fingerprint of the Zobrist key tables this was compiled against. Checked before any
        /// entry is trusted: keys built from one set of random numbers mean nothing read against
        /// another, and the failure is silent — every lookup simply misses — so it has to be
        /// caught rather than discovered by noticing the AI walks into traps it should know.
        /// </summary>
        [SerializeField] private ulong _schemeVersion;

        public int EntryCount => _positionKeys.Length;
        public ulong SchemeVersion => _schemeVersion;

        public ulong PositionKeyAt(int index) => _positionKeys[index];

        /// <summary>The move that loses, packed the same way the search packs moves.</summary>
        public uint BlunderMoveAt(int index) => _blunderMoves[index];

        /// <summary>The move to play in that position instead.</summary>
        public uint BestMoveAt(int index) => _bestMoves[index];

        /// <summary>What the trap is called, for anything that needs to name it to a player.</summary>
        public string NameAt(int index) => _names[index];

        /// <summary>
        /// Replaces the contents. Called by the compiler right after building the asset; entries
        /// must already be sorted by key ascending, since every reader binary-searches them.
        /// </summary>
        public void SetEntries(
            ulong[] positionKeys, uint[] blunderMoves, uint[] bestMoves, string[] names, ulong schemeVersion)
        {
            _positionKeys = positionKeys;
            _blunderMoves = blunderMoves;
            _bestMoves = bestMoves;
            _names = names;
            _schemeVersion = schemeVersion;
        }
    }
}
