using System.Collections.Generic;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// Built-in fallback roster of <see cref="AIProfile"/> rows. Ships in the AI assembly so
    /// EditMode tests and a missing/corrupt asset provider always have a valid roster to fall
    /// back on.
    /// </summary>
    public static class AIProfileTable
    {
        public const string DefaultId = "normal";

        // PROVISIONAL — these six rows are pre-validation estimates. They'll be checked against
        // real AI-vs-AI results and a manual playtest pass, and this note removed once that's
        // happened and the numbers below reflect what was actually measured rather than a guess.
        //
        // Time budgets above 3000ms are a temporary placeholder. The real target for every
        // difficulty tier is an AI move under 3 seconds — "hard" and above will need to come
        // down once the remaining search performance work lands.
        //
        // Each tier's hard budget is the soft budget plus a fixed margin the search may spend
        // into only when the position looks unsettled — same provisional status as every other
        // number here, not yet validated against real play.
        public static readonly IReadOnlyList<AIProfile> BuiltIn = new[]
        {
            new AIProfile("easy",       maxDepth: 3,  timeBudget: new AITimeBudget(800, 1300),    blunderRate: 0.30f, blunderMarginCp: 120, betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 30, useOpeningBook: true),
            new AIProfile("normal",     maxDepth: 5,  timeBudget: new AITimeBudget(1500, 2250),   blunderRate: 0.10f, blunderMarginCp: 80,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 20, useOpeningBook: true),
            new AIProfile("hard",       maxDepth: 7,  timeBudget: new AITimeBudget(3000, 4500),   blunderRate: 0.02f, blunderMarginCp: 40,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 15, useOpeningBook: true),
            new AIProfile("aggressive", maxDepth: 6,  timeBudget: new AITimeBudget(2500, 3750),   blunderRate: 0.05f, blunderMarginCp: 60,  betrayalAggression: 0.7f,  attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: true),
            new AIProfile("extreme",    maxDepth: 8,  timeBudget: new AITimeBudget(4500, 6750),   blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0.3f,  attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: true),
            new AIProfile("impossible", maxDepth: 10, timeBudget: new AITimeBudget(8000, 12000),  blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 0,  useOpeningBook: true),
        };
    }
}
