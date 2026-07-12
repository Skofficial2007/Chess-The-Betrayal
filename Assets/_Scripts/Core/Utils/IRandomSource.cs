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

        /// <summary>Returns a value in [0, 1). Used for probability rolls (e.g. AI blunder rate)
        /// and weighted picks — composition (Pick/WeightedPick over a candidate set) lives in the
        /// caller, this stays a minimal primitive.</summary>
        float NextFloat();
    }
}
