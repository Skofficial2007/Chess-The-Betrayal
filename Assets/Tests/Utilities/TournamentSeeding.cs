namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Derives a per-game, per-side RNG seed from one tournament-wide run seed. Every stochastic
    /// element in a simulated game (blunder rolls, tie-break picks) ultimately traces back to one
    /// of these — so a fixed runSeed reproduces a bit-identical tournament, and distinct streams
    /// per side mean perturbing one agent's roll count can never affect the other's sequence.
    /// </summary>
    public static class TournamentSeeding
    {
        public static int DeriveSeed(int runSeed, int positionIndex, int pairIndex, int gameIndex, int side)
        {
            unchecked
            {
                int hash = runSeed;
                hash = CombineHash(hash, positionIndex);
                hash = CombineHash(hash, pairIndex);
                hash = CombineHash(hash, gameIndex);
                hash = CombineHash(hash, side);
                return hash;
            }
        }

        // FNV-1a-style mixing — small, deterministic, and stable across .NET versions (unlike
        // GetHashCode(), which is explicitly documented not to be stable across processes/runs).
        private static int CombineHash(int seed, int value)
        {
            unchecked
            {
                int hash = seed ^ value;
                hash *= -1640531527; // 0x9E3779B9 as a signed int — the usual golden-ratio mixing constant
                return hash;
            }
        }
    }
}
