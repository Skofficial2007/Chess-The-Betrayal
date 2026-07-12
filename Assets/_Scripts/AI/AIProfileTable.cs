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

        // Time budgets above 3000ms are a temporary placeholder. The real target for every
        // difficulty tier is an AI move under 3 seconds — "hard" and above will need to come
        // down once the remaining search performance work lands.
        public static readonly IReadOnlyList<AIProfile> BuiltIn = new[]
        {
            new AIProfile("easy",       maxDepth: 3,  softTimeBudgetMs: 800,  blunderRate: 0.30f, blunderMarginCp: 120, betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 30, useOpeningBook: true),
            new AIProfile("normal",     maxDepth: 5,  softTimeBudgetMs: 1500, blunderRate: 0.10f, blunderMarginCp: 80,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 20, useOpeningBook: true),
            new AIProfile("hard",       maxDepth: 7,  softTimeBudgetMs: 3000, blunderRate: 0.02f, blunderMarginCp: 40,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 15, useOpeningBook: true),
            new AIProfile("aggressive", maxDepth: 6,  softTimeBudgetMs: 2500, blunderRate: 0.05f, blunderMarginCp: 60,  betrayalAggression: 0.7f,  attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: true),
            new AIProfile("extreme",    maxDepth: 8,  softTimeBudgetMs: 4500, blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0.3f,  attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: true),
            new AIProfile("impossible", maxDepth: 10, softTimeBudgetMs: 8000, blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 0,  useOpeningBook: true),
        };
    }
}
