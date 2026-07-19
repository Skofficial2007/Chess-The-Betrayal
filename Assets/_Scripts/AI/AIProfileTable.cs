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

        // Config data, re-measured against the live search (not an algorithm change): every tier's
        // soft time budget is now capped at 3 seconds, matching the real per-move target for every
        // difficulty. This is safe because a fresh uncapped timing pass on a representative
        // midgame position showed every tier — including "impossible" at its full depth of 10 —
        // finishing in well under 1 second on its own, thanks to the pruning/ordering/extension
        // work that landed since these numbers were last picked. None of the six rows needed a
        // depth cut to hit the 3-second target; MaxDepth is unchanged from before this pass.
        public static readonly IReadOnlyList<AIProfile> BuiltIn = new[]
        {
            new AIProfile("easy",       maxDepth: 3,  timeBudget: new AITimeBudget(800, 1300),    blunderRate: 0.30f, blunderMarginCp: 120, betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 30, useOpeningBook: true),
            new AIProfile("normal",     maxDepth: 5,  timeBudget: new AITimeBudget(1500, 2250),   blunderRate: 0.10f, blunderMarginCp: 80,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 20, useOpeningBook: true),
            new AIProfile("hard",       maxDepth: 7,  timeBudget: new AITimeBudget(3000, 3000),   blunderRate: 0.02f, blunderMarginCp: 40,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 15, useOpeningBook: true),
            new AIProfile("aggressive", maxDepth: 6,  timeBudget: new AITimeBudget(2500, 3000),   blunderRate: 0.05f, blunderMarginCp: 60,  betrayalAggression: 0.7f,  attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: true),
            new AIProfile("extreme",    maxDepth: 8,  timeBudget: new AITimeBudget(3000, 3000),   blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0.3f,  attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: true),
            new AIProfile("impossible", maxDepth: 10, timeBudget: new AITimeBudget(3000, 3000),   blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 0,  useOpeningBook: true),
        };
    }
}
