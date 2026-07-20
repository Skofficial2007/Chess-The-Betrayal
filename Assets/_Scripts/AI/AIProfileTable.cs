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

        // Each tier's time budget is a soft/hard pair, and the gap between them is what lets the
        // search spend effort in proportion to how hard the position actually is. HardMs is the
        // promise to the player: a move always arrives within it (3 seconds at most). SoftMs is
        // the point past which the search is allowed to stop early IF the position is settled —
        // the same best move has held across several deeper searches, so more thinking would only
        // refine a number, not change the move. On a quiet or forced position the search returns
        // near SoftMs; only genuinely tactical positions, where the best move keeps changing as
        // depth grows, spend the full way out to HardMs. Without that gap (soft == hard) the AI
        // burned its entire budget on every move, including dead-obvious recaptures, which players
        // experience as the engine stalling over decisions it has already made.
        //
        // MaxDepth is a CEILING, not a guarantee. easy and normal are shallow by design (their
        // difficulty comes from that plus the blunder rate), and reach their full depth well
        // inside SoftMs. The deeper tiers are budget-bound: they reach whatever depth the budget
        // allows on the given hardware, deeper on faster machines, and iterative deepening always
        // keeps the last fully completed depth's move, so a budget stop is never a wasted search.
        public static readonly IReadOnlyList<AIProfile> BuiltIn = new[]
        {
            new AIProfile("easy",       maxDepth: 3,  timeBudget: new AITimeBudget(400, 1300),     blunderRate: 0.30f, blunderMarginCp: 120, betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 30, useOpeningBook: true),
            new AIProfile("normal",     maxDepth: 5,  timeBudget: new AITimeBudget(700, 2250),     blunderRate: 0.10f, blunderMarginCp: 80,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 20, useOpeningBook: true),
            new AIProfile("hard",       maxDepth: 8,  timeBudget: new AITimeBudget(900, 3000),     blunderRate: 0.02f, blunderMarginCp: 40,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 15, useOpeningBook: true),
            new AIProfile("aggressive", maxDepth: 7,  timeBudget: new AITimeBudget(900, 3000),     blunderRate: 0.05f, blunderMarginCp: 60,  betrayalAggression: 0.7f,  attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: true),
            new AIProfile("extreme",    maxDepth: 9,  timeBudget: new AITimeBudget(1000, 3000),    blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0.3f,  attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: true),
            new AIProfile("impossible", maxDepth: 9,  timeBudget: new AITimeBudget(1200, 3000),    blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 0,  useOpeningBook: true),
        };
    }
}
