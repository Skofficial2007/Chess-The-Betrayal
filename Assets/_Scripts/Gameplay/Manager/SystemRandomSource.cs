using System;
using ChessTheBetrayal.Core.Utils;

namespace ChessTheBetrayal.Gameplay
{
    /// <summary>
    /// IRandomSource backed by System.Random, for local and AI sessions. Not used for
    /// multiplayer — a server-seeded implementation lives on the network side so both clients
    /// derive identical results.
    /// </summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;

        public SystemRandomSource() : this(new Random()) { }

        public SystemRandomSource(int seed) : this(new Random(seed)) { }

        private SystemRandomSource(Random random)
        {
            _random = random;
        }

        public bool NextBool() => _random.Next(2) == 0;

        public int NextInt(int maxExclusive) => _random.Next(maxExclusive);
    }
}
