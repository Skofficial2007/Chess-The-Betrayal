using UnityEngine;

namespace ChessTheBetrayal.AI.OpeningBook
{
    /// <summary>
    /// A compiled opening book: every position the book knows a reply for, keyed by the exact
    /// Zobrist hash the search itself would compute for that position. Built offline by
    /// OpeningBookCompiler from a plain-text list of variations, never authored by hand.
    ///
    /// The three arrays are parallel and kept sorted by Keys so a lookup can binary-search
    /// instead of scanning. A single Zobrist hash can legitimately map to more than one move
    /// (two different book lines transposing into the same position but recommending different
    /// replies), so a hit is really a small run of matching entries, not always exactly one.
    /// </summary>
    [CreateAssetMenu(menuName = "Chess/AI/Opening Book", fileName = "OpeningBook")]
    public sealed class OpeningBookAsset : ScriptableObject
    {
        [SerializeField] private ulong[] _keys = System.Array.Empty<ulong>();
        [SerializeField] private uint[] _packedMoves = System.Array.Empty<uint>();
        [SerializeField] private ushort[] _weights = System.Array.Empty<ushort>();

        /// <summary>
        /// Fingerprint of the Zobrist key tables this book was compiled against. A book compiled
        /// against one set of random keys is meaningless read against another set, so the runtime
        /// checks this before trusting a single entry rather than silently returning nonsense
        /// moves for hashes that only look valid.
        /// </summary>
        [SerializeField] private ulong _schemeVersion;

        public int EntryCount => _keys.Length;
        public ulong SchemeVersion => _schemeVersion;

        public ulong KeyAt(int index) => _keys[index];
        public uint PackedMoveAt(int index) => _packedMoves[index];
        public ushort WeightAt(int index) => _weights[index];

        /// <summary>
        /// Replaces the book's contents. Called once by OpeningBookCompiler right after building
        /// the asset; entries must already be sorted by key ascending, since every reader relies
        /// on that ordering to binary-search instead of scanning.
        /// </summary>
        public void SetEntries(ulong[] keys, uint[] packedMoves, ushort[] weights, ulong schemeVersion)
        {
            _keys = keys;
            _packedMoves = packedMoves;
            _weights = weights;
            _schemeVersion = schemeVersion;
        }
    }
}
