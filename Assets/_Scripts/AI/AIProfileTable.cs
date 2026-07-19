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

        // The time budget is the promise each tier makes to the player: a move always arrives
        // within HardMs (3 seconds at most, every tier). MaxDepth is a CEILING, not a guarantee —
        // easy and normal complete their full depth in a fraction of their budgets (their shallow
        // depth is part of the difficulty identity; the blunder rate does the intentional
        // weakening), while the deeper tiers are genuinely budget-bound: on the desktop baseline
        // they complete depth 7-8 of their configured 7-9 before the timer stops them, and faster
        // hardware reaches deeper without any config change. They became budget-bound when the
        // search started valuing Betrayal Defections honestly — a correct tree on a position with
        // the Betrayal right still live is much larger than the mis-scored one these depths were
        // originally timed against, and cutting depth to chase fixed-depth timings would cap the
        // tiers on strong hardware for the sake of a number no player experiences. Iterative
        // deepening always keeps the last fully completed depth's answer, so a budget stop is
        // never a wasted search.
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
