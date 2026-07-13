using System;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>How a simulated game ended, from White's point of view.</summary>
    public enum MatchOutcome
    {
        WhiteWon,
        BlackWon,
        Draw
    }

    /// <summary>One completed simulated game.</summary>
    public readonly struct MatchResult
    {
        public readonly MatchOutcome Outcome;
        public readonly int PlyCount;
        public readonly bool ReachedPlyCap;

        public MatchResult(MatchOutcome outcome, int plyCount, bool reachedPlyCap)
        {
            Outcome = outcome;
            PlyCount = plyCount;
            ReachedPlyCap = reachedPlyCap;
        }
    }

    /// <summary>
    /// Plays one full game between two AIProfile-driven sides, entirely synchronously and off the
    /// worker-thread path AsyncAIAgent normally uses — there is no benefit to threading here and it
    /// would only add nondeterministic scheduling to what needs to be a bit-reproducible tournament.
    ///
    /// Composes the same stack a real match uses (AlphaBetaSearch + MoveSelectionPolicy +
    /// BetrayalAwareEvaluator, moves applied through MatchDriver so Betrayal sub-sequences,
    /// checkmate/stalemate detection, and move logging all run through the exact seam a live game
    /// uses) but calls FindBestMove directly instead of going through AsyncAIAgent, since the
    /// threading/cancellation contract there is already covered by its own tests and would only
    /// slow this down.
    ///
    /// A game with no result by the ply cap is adjudicated by static evaluation margin rather than
    /// played out indefinitely — there is no threefold-repetition detection yet, so the cap is what
    /// actually terminates a repeating line.
    /// </summary>
    public sealed class MatchSimulator
    {
        public const int DefaultPlyCap = 120;
        public const int AdjudicationMarginCp = 300;

        private readonly IChessEngine _engine = new ChessEngineAdapter();
        private readonly IPositionEvaluator _adjudicationEvaluator = new BetrayalAwareEvaluator();

        /// <summary>
        /// Plays one game from <paramref name="startingPosition"/> between <paramref name="whiteProfile"/>
        /// and <paramref name="blackProfile"/>. The starting position is cloned internally, so the
        /// caller's instance is never mutated and the same curated position can be reused across many
        /// simulated games. rngSeedWhite/rngSeedBlack are independent streams (see the harness's
        /// per-side seeding scheme) so perturbing one side's roll count can never affect the other's.
        /// </summary>
        public MatchResult PlayGame(
            BoardState startingPosition, AIProfile whiteProfile, AIProfile blackProfile,
            int rngSeedWhite, int rngSeedBlack, int plyCap = DefaultPlyCap)
        {
            BoardState board = startingPosition.CloneForSnapshot();

            var matchDriver = new MatchDriver(_engine, board, logMoves: false, domainLogger: null,
                gameOverChannel: null, turnChangedChannel: null, moveExecutedChannel: null,
                moveRejectedChannel: null, checkDetectedChannel: null, betrayalChannel: null);
            matchDriver.TransitionToPhase(TurnPhase.Normal);

            var whiteSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(whiteProfile)));
            var blackSearch = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(blackProfile)));
            var whitePolicy = new MoveSelectionPolicy();
            var blackPolicy = new MoveSelectionPolicy();
            IRandomSource whiteRng = new SystemRandomSource(rngSeedWhite);
            IRandomSource blackRng = new SystemRandomSource(rngSeedBlack);

            int ply = 0;
            for (; ply < plyCap && !board.IsGameOver; ply++)
            {
                Team mover = board.CurrentTurn;
                bool isWhite = mover == Team.White;

                AIProfile profile = isWhite ? whiteProfile : blackProfile;
                AlphaBetaSearch search = isWhite ? whiteSearch : blackSearch;
                MoveSelectionPolicy policy = isWhite ? whitePolicy : blackPolicy;
                IRandomSource rng = isWhite ? whiteRng : blackRng;

                var settings = AISearchSettings.FromProfile(BetrayalUsage.Full, profile);
                int rescoreMargin = Math.Max(profile.BlunderMarginCp, profile.TieBreakWindowCp);

                MoveCommand move = search.FindBestMove(board, settings, System.Threading.CancellationToken.None, rescoreMargin);

                if (profile.BlunderRate > 0f || profile.TieBreakWindowCp > 0)
                {
                    move = policy.SelectFinalMove(
                        search.RootMoves, search.RootScores, search.RootMoveCount, search.BestRootIndex,
                        profile, rng);
                }

                matchDriver.PlayMove(move);
            }

            if (board.IsGameOver)
            {
                MatchOutcome outcome = board.Winner switch
                {
                    Team.White => MatchOutcome.WhiteWon,
                    Team.Black => MatchOutcome.BlackWon,
                    _ => MatchOutcome.Draw
                };
                return new MatchResult(outcome, ply, reachedPlyCap: false);
            }

            return new MatchResult(AdjudicateByMargin(board), ply, reachedPlyCap: true);
        }

        /// <summary>
        /// A game that hit the ply cap without a decisive result is scored by static evaluation
        /// margin rather than left unresolved — a clear material/positional edge (beyond
        /// AdjudicationMarginCp) counts as a win, anything closer counts as a draw.
        /// </summary>
        private MatchOutcome AdjudicateByMargin(BoardState board)
        {
            int whiteScore = _adjudicationEvaluator.Evaluate(board, Team.White);

            if (whiteScore > AdjudicationMarginCp) return MatchOutcome.WhiteWon;
            if (whiteScore < -AdjudicationMarginCp) return MatchOutcome.BlackWon;
            return MatchOutcome.Draw;
        }
    }
}
