using System.Runtime.InteropServices;

namespace ChessTheBetrayal.AI
{
    /// <summary>Exact score, or a bound produced by an alpha/beta cutoff at the stored depth.</summary>
    public enum TTFlag : byte
    {
        Exact = 0,
        LowerBound = 1,
        UpperBound = 2
    }

    /// <summary>
    /// One transposition table slot. Two 64-bit lanes, blittable and NativeArray-ready (see the
    /// ADR's Burst-deferral note) — KeyLane stores the Zobrist hash XORed with DataLane (the Hyatt
    /// lockless scheme), so a single XOR on probe verifies index collisions, key mismatches, AND
    /// torn writes all at once. DataLane packs everything else needed to resume the search at this
    /// node: the score, the best move found (for ordering), the depth it was searched to, the
    /// bound type, and a generation stamp for replacement.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TTEntry
    {
        public ulong KeyLane;
        public ulong DataLane;
    }

    /// <summary>
    /// Single-slot transposition table for <see cref="AlphaBetaSearch"/>. Owned exclusively by one
    /// search on one worker thread (see AsyncAIAgent) — no locks, no volatile, no atomics; the XOR
    /// verification scheme is dormant insurance for a future multi-threaded search, not something
    /// this pass needs to rely on.
    ///
    /// Betrayal-correctness basis (load-bearing): board.ZobristHash already disambiguates the full
    /// sub-state via ToggleBetrayalSubStateHash (pending-Betrayer square + initiator) and
    /// ApplyZobristMove's turn-hash skip on Act/Defection. Identical hash therefore means identical
    /// sub-phase, so a TT cutoff is valid even mid-Betrayal-sequence — no special-casing needed here.
    /// Pinned by BetrayalZobristTests; re-pinned consumer-side by TranspositionTableTests.
    /// </summary>
    public sealed class TranspositionTable
    {
        // DataLane bit layout (low to high): Score(32) | Move(19) | Depth(6) | Flag(2) | Generation(5)
        private const int ScoreBits = 32;
        private const int MoveBits = 19;
        private const int DepthBits = 6;
        private const int FlagBits = 2;
        private const int GenerationBits = 5;

        private const int MoveShift = ScoreBits;
        private const int DepthShift = MoveShift + MoveBits;
        private const int FlagShift = DepthShift + DepthBits;
        private const int GenerationShift = FlagShift + FlagBits;

        private const ulong ScoreMask = (1UL << ScoreBits) - 1;
        private const ulong MoveMask = (1UL << MoveBits) - 1;
        private const ulong DepthMask = (1UL << DepthBits) - 1;
        private const ulong FlagMask = (1UL << FlagBits) - 1;
        private const ulong GenerationMask = (1UL << GenerationBits) - 1;

        private const int GenerationWrap = 1 << GenerationBits; // 32

        private readonly TTEntry[] _entries;
        private readonly ulong _indexMask;
        private uint _generation;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Telemetry only (AI-21) — every counter lives behind this symbol so a release
        /// build pays nothing for tracking it. Reset by the owning AlphaBetaSearch per FindBestMove.</summary>
        public SearchStats Stats;
#endif

        /// <summary>entryCount = 1 &lt;&lt; log2Size. Desktop: 20 (~16 MB); mobile: 18 (~4 MB).</summary>
        public TranspositionTable(int log2Size)
        {
            int entryCount = 1 << log2Size;
            _entries = new TTEntry[entryCount];
            _indexMask = (ulong)(entryCount - 1);
            _generation = 0;
        }

        /// <summary>Packs a search result into a slot. Returns false on a torn/collided/empty read.</summary>
        public bool Probe(ulong hash, out int score, out uint packedMove, out int depth, out TTFlag flag)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Stats.TTProbes++;
#endif
            int index = (int)(hash & _indexMask);
            TTEntry entry = _entries[index];

            if ((entry.KeyLane ^ entry.DataLane) != hash)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                bool wasEverWritten = entry.KeyLane != 0 || entry.DataLane != 0;
                if (wasEverWritten) Stats.TTVerificationMisses++; // index collision or torn write
                else Stats.TTEmptyMisses++;
#endif
                score = 0;
                packedMove = 0;
                depth = 0;
                flag = TTFlag.Exact;
                return false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Stats.TTHits++;
#endif
            ulong data = entry.DataLane;
            score = UnpackScore(data);
            packedMove = (uint)((data >> MoveShift) & MoveMask);
            depth = (int)((data >> DepthShift) & DepthMask);
            flag = (TTFlag)((data >> FlagShift) & FlagMask);
            return true;
        }

        /// <summary>Depth-preferred replacement: a stale-generation slot is always replaceable;
        /// a same-generation slot only yields to a search that went at least as deep.</summary>
        public void Store(ulong hash, int score, uint packedMove, int depth, TTFlag flag)
        {
            int index = (int)(hash & _indexMask);
            TTEntry existing = _entries[index];
            bool wasOccupied = existing.KeyLane != 0 || existing.DataLane != 0;

            if (wasOccupied)
            {
                ulong existingData = existing.DataLane;
                uint existingGeneration = (uint)((existingData >> GenerationShift) & GenerationMask);
                int existingDepth = (int)((existingData >> DepthShift) & DepthMask);

                if (existingGeneration == _generation && depth < existingDepth)
                    return;
            }

            ulong packedScore = (ulong)(uint)score & ScoreMask;
            ulong data =
                packedScore |
                ((ulong)(packedMove & MoveMask) << MoveShift) |
                ((ulong)((uint)depth & DepthMask) << DepthShift) |
                ((ulong)((uint)flag & FlagMask) << FlagShift) |
                ((ulong)(_generation & GenerationMask) << GenerationShift);

            _entries[index] = new TTEntry
            {
                KeyLane = hash ^ data,
                DataLane = data
            };

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Stats.TTStores++;
            if (wasOccupied) Stats.TTReplacements++;
#endif
        }

        /// <summary>Bumps the replacement generation. Call once per FindBestMove, before the depth loop.</summary>
        public void NewSearch()
        {
            _generation = (_generation + 1) % GenerationWrap;
        }

        /// <summary>Wipes the table. Call once per new match (owned by AsyncAIAgent).</summary>
        public void Clear()
        {
            System.Array.Clear(_entries, 0, _entries.Length);
            _generation = 0;
        }

        private static int UnpackScore(ulong data)
        {
            // Score was packed as an unsigned 32-bit field; sign-extend it back to a signed int.
            return (int)(uint)(data & ScoreMask);
        }
    }
}
