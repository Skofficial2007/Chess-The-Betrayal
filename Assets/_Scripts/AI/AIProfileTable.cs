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
        // soft time budget is capped at 3 seconds, matching the real per-move target for every
        // difficulty. A fresh uncapped timing pass on a representative midgame position showed
        // every tier finishing in well under 1 second on its own, thanks to the pruning/ordering/
        // extension work that landed since these numbers were last picked.
        //
        // aggressive/hard/extreme each got their MaxDepth raised by one ply on top of that
        // measurement — these tiers exist specifically to search as strong as the engine can
        // manage, so freed-up budget headroom belongs to them; easy/normal are deliberately shallow
        // as PART of their difficulty identity (their blunder rate already does the weakening), so
        // raising their depth would work against the design instead of with it.
        //
        // impossible's own +1 raise (10 -> 11) was reverted once the multi-move benchmark (which
        // plays several successive turns, including a real Betrayal resolution, on one persistent
        // search) caught it costing 6-9s on the position that resolution leaves behind — a real,
        // legitimately harder middlegame than the single fixed opening position the depth-raise was
        // originally measured against, not a test artifact. Depth 10 (its original, pre-raise
        // depth) still occasionally brushed the 3-second ceiling on the same run's hardest ply, so
        // this dropped one further to 9 — the depth actually proven, across a full successive-turn
        // run including a real Betrayal resolution, to hold comfortably under target rather than
        // right at its edge.
        public static readonly IReadOnlyList<AIProfile> BuiltIn = new[]
        {
            new AIProfile("easy",       maxDepth: 3,  timeBudget: new AITimeBudget(800, 1300),    blunderRate: 0.30f, blunderMarginCp: 120, betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 30, useOpeningBook: true),
            new AIProfile("normal",     maxDepth: 5,  timeBudget: new AITimeBudget(1500, 2250),   blunderRate: 0.10f, blunderMarginCp: 80,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 20, useOpeningBook: true),
            new AIProfile("hard",       maxDepth: 8,  timeBudget: new AITimeBudget(3000, 3000),   blunderRate: 0.02f, blunderMarginCp: 40,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 15, useOpeningBook: true),
            new AIProfile("aggressive", maxDepth: 7,  timeBudget: new AITimeBudget(2500, 3000),   blunderRate: 0.05f, blunderMarginCp: 60,  betrayalAggression: 0.7f,  attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: true),
            new AIProfile("extreme",    maxDepth: 9,  timeBudget: new AITimeBudget(3000, 3000),   blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0.3f,  attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: true),
            new AIProfile("impossible", maxDepth: 9,  timeBudget: new AITimeBudget(3000, 3000),   blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 0,  useOpeningBook: true),
        };
    }
}
