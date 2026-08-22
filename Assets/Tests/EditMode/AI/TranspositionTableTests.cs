using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ChessTheBetrayal.AI;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins TranspositionTable's storage contract: blittability (no managed references, exactly 16
    /// bytes), the XOR lockless verification scheme (rejects collisions AND torn writes), and
    /// depth-preferred-with-generation replacement including generation wraparound.
    /// </summary>
    [TestFixture]
    public class TranspositionTableTests
    {
        [Test]
        public void TTEntry_IsExactly16Bytes()
        {
            Assert.That(Marshal.SizeOf<TTEntry>(), Is.EqualTo(16));
        }

        [Test]
        public void TTEntry_ContainsNoManagedReferences()
        {
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<TTEntry>(), Is.False);
        }

        [Test]
        public void Probe_OnEmptyTable_Misses()
        {
            var tt = new TranspositionTable(log2Size: 4);

            bool hit = tt.Probe(0xDEADBEEF, out _, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void StoreThenProbe_SameHash_RoundTripsExactly()
        {
            var tt = new TranspositionTable(log2Size: 4);
            ulong hash = 0x1234_5678_9ABC_DEF0;

            tt.Store(hash, score: 350, packedMove: 0x1A2B, depth: 5, TTFlag.Exact);
            bool hit = tt.Probe(hash, out int score, out uint move, out int depth, out TTFlag flag);

            Assert.That(hit, Is.True);
            Assert.That(score, Is.EqualTo(350));
            Assert.That(move, Is.EqualTo((uint)0x1A2B));
            Assert.That(depth, Is.EqualTo(5));
            Assert.That(flag, Is.EqualTo(TTFlag.Exact));
        }

        [Test]
        public void StoreThenProbe_NegativeScore_RoundTripsExactly()
        {
            var tt = new TranspositionTable(log2Size: 4);
            ulong hash = 0x0F0F_0F0F_0F0F_0F0F;

            tt.Store(hash, score: -12345, packedMove: 0, depth: 2, TTFlag.UpperBound);
            tt.Probe(hash, out int score, out _, out _, out TTFlag flag);

            Assert.That(score, Is.EqualTo(-12345));
            Assert.That(flag, Is.EqualTo(TTFlag.UpperBound));
        }

        [Test]
        public void Probe_IndexCollisionDifferentHash_Misses()
        {
            // Two different hashes that collide on a tiny table's index (same low bits).
            var tt = new TranspositionTable(log2Size: 4); // 16 slots, mask = 0xF
            ulong hashA = 0x0000_0000_0000_0001;
            ulong hashB = 0x0000_0000_0000_0011; // same low 4 bits, different hash overall

            tt.Store(hashA, score: 42, packedMove: 7, depth: 3, TTFlag.Exact);
            bool hit = tt.Probe(hashB, out _, out _, out _, out _);

            Assert.That(hit, Is.False, "A different hash landing on the same slot must miss, not return the wrong entry.");
        }

        [Test]
        public void Probe_TornEntry_Misses()
        {
            var tt = new TranspositionTable(log2Size: 4);
            ulong hash = 0xAAAA_BBBB_CCCC_DDDD;
            tt.Store(hash, score: 99, packedMove: 3, depth: 4, TTFlag.LowerBound);

            // Simulate a torn write by flipping a bit that only DataLane's mirror in KeyLane covers —
            // corrupt the table's backing array directly via reflection since it's private.
            var entriesField = typeof(TranspositionTable).GetField("_entries",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var entries = (TTEntry[])entriesField.GetValue(tt);
            int index = (int)(hash & 0xF);
            entries[index].DataLane ^= 0x1; // corrupt one bit of the data lane only

            bool hit = tt.Probe(hash, out _, out _, out _, out _);

            Assert.That(hit, Is.False, "A torn entry (KeyLane no longer XORs back to the hash) must be rejected as a miss.");
        }

        [Test]
        public void Store_SameGenerationShallowerDepth_DoesNotReplace()
        {
            var tt = new TranspositionTable(log2Size: 4);
            ulong hash = 0x2222_3333_4444_5555;

            tt.Store(hash, score: 10, packedMove: 1, depth: 6, TTFlag.Exact);
            tt.Store(hash, score: 20, packedMove: 2, depth: 3, TTFlag.Exact); // shallower, same gen

            tt.Probe(hash, out int score, out uint move, out int depth, out _);

            Assert.That(depth, Is.EqualTo(6));
            Assert.That(score, Is.EqualTo(10));
            Assert.That(move, Is.EqualTo((uint)1));
        }

        [Test]
        public void Store_SameGenerationDeeperOrEqualDepth_Replaces()
        {
            var tt = new TranspositionTable(log2Size: 4);
            ulong hash = 0x6666_7777_8888_9999;

            tt.Store(hash, score: 10, packedMove: 1, depth: 3, TTFlag.Exact);
            tt.Store(hash, score: 20, packedMove: 2, depth: 3, TTFlag.Exact); // equal depth replaces

            tt.Probe(hash, out int score, out uint move, out _, out _);

            Assert.That(score, Is.EqualTo(20));
            Assert.That(move, Is.EqualTo((uint)2));
        }

        [Test]
        public void NewSearch_NewGeneration_OverridesDepthPreference()
        {
            var tt = new TranspositionTable(log2Size: 4);
            ulong hash = 0xABCD_1234_5678_EF00;

            tt.Store(hash, score: 10, packedMove: 1, depth: 10, TTFlag.Exact);
            tt.NewSearch(); // bump generation
            tt.Store(hash, score: 99, packedMove: 2, depth: 1, TTFlag.Exact); // shallow, but new generation

            tt.Probe(hash, out int score, out uint move, out int depth, out _);

            Assert.That(score, Is.EqualTo(99), "A stale-generation entry must yield even to a shallower search.");
            Assert.That(depth, Is.EqualTo(1));
            Assert.That(move, Is.EqualTo((uint)2));
        }

        [Test]
        public void NewSearch_WrapsGenerationWithoutThrowing()
        {
            var tt = new TranspositionTable(log2Size: 4);

            for (int i = 0; i < 40; i++) // exceeds the 5-bit (32) generation wrap
            {
                tt.NewSearch();
            }

            ulong hash = 0x1111_2222_3333_4444;
            Assert.DoesNotThrow(() => tt.Store(hash, score: 5, packedMove: 0, depth: 1, TTFlag.Exact));
            bool hit = tt.Probe(hash, out int score, out _, out _, out _);
            Assert.That(hit, Is.True);
            Assert.That(score, Is.EqualTo(5));
        }

        [Test]
        public void Clear_RemovesAllEntriesAndResetsGeneration()
        {
            var tt = new TranspositionTable(log2Size: 4);
            ulong hash = 0x1357_2468_1357_2468;
            tt.Store(hash, score: 77, packedMove: 4, depth: 2, TTFlag.Exact);

            tt.Clear();

            Assert.That(tt.Probe(hash, out _, out _, out _, out _), Is.False);
        }
    }
}
