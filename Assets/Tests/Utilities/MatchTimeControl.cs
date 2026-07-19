namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// How a simulated game's searches are bounded in time.
    ///
    /// ProductionBudget mirrors what a live match does: each move's search is armed with the
    /// profile's own hard time budget and may stop early on a settled position, exactly like the
    /// real AI agent. This is the mode tournament and benchmark runs should use — it measures the
    /// engine as it actually ships, and it bounds a pathological position to the budget instead of
    /// letting one move run arbitrarily long.
    ///
    /// Uncapped removes every time bound: each move searches to its profile's full depth no matter
    /// how long that takes. Because nothing depends on wall-clock time, two Uncapped runs with the
    /// same seeds are bit-identical on any machine — which makes this the right mode for debugging
    /// a specific game, at the cost of unbounded runtime.
    ///
    /// The trade-off between the two is the standard one in engine testing: under a time budget, a
    /// tier that actually hits its cap may complete a different depth on a slower machine and so
    /// pick a different move. Statistics over many games absorb that; single-game reproduction
    /// should use Uncapped.
    /// </summary>
    public enum MatchTimeControl
    {
        ProductionBudget,
        Uncapped
    }
}
