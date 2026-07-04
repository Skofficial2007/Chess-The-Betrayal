namespace ChessTheBetrayal.Core.Utils
{
    /// <summary>
    /// A Unity-free source of randomness for domain logic. Core never calls UnityEngine.Random
    /// directly — Gameplay hands in a SystemRandomSource for local play, and multiplayer hands in
    /// a seeded implementation so both clients derive identical results from the same seed.
    /// </summary>
    public interface IRandomSource
    {
        bool NextBool();
        int NextInt(int maxExclusive);
    }
}
