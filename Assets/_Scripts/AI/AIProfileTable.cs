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
        //
        // OpeningBookDepthPlies is how much theory each tier is allowed to play from memory before
        // it has to think for itself, and it is the other half of what makes a tier feel like its
        // difficulty. Left unlimited it hands every tier identical grandmaster openings, which
        // makes the weak ones play far above their level for the first several moves and then fall
        // off a cliff. The shipped book runs a median of 16 plies and no line shorter than 8, so
        // every allowance below is one a real game actually reaches rather than a number that
        // quietly never applies. The top two tiers keep the whole book: correct theory played
        // instantly, with the entire time budget saved for the middlegame.
        //
        // Past roughly ply 10 the book holds close to one reply per position, so the allowances
        // below give up very little variety — the choice of opening is made and done inside the
        // first few moves.
        public static readonly IReadOnlyList<AIProfile> BuiltIn = new[]
        {
            new AIProfile("easy",       maxDepth: 3,  timeBudget: new AITimeBudget(400, 1300),     blunderRate: 0.30f, blunderMarginCp: 120, betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 30, useOpeningBook: true, openingBookDepthPlies: 4),
            new AIProfile("normal",     maxDepth: 5,  timeBudget: new AITimeBudget(700, 2250),     blunderRate: 0.10f, blunderMarginCp: 80,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 20, useOpeningBook: true, openingBookDepthPlies: 8),
            new AIProfile("hard",       maxDepth: 8,  timeBudget: new AITimeBudget(900, 3000),     blunderRate: 0.02f, blunderMarginCp: 40,  betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 15, useOpeningBook: true, openingBookDepthPlies: 14),
            new AIProfile("aggressive", maxDepth: 7,  timeBudget: new AITimeBudget(900, 3000),     blunderRate: 0.05f, blunderMarginCp: 60,  betrayalAggression: 0.7f,  attackDefenseBias: 1.5f, tieBreakWindowCp: 25, useOpeningBook: true, openingBookDepthPlies: 10),
            new AIProfile("extreme",    maxDepth: 9,  timeBudget: new AITimeBudget(1000, 3000),    blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0.3f,  attackDefenseBias: 1.2f, tieBreakWindowCp: 10, useOpeningBook: true, openingBookDepthPlies: 0),
            new AIProfile("impossible", maxDepth: 9,  timeBudget: new AITimeBudget(1200, 3000),    blunderRate: 0f,    blunderMarginCp: 0,   betrayalAggression: 0f,    attackDefenseBias: 1.0f, tieBreakWindowCp: 0,  useOpeningBook: true, openingBookDepthPlies: 0),
        };
    }
}
