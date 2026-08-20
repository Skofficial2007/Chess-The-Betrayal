using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tooling
{
    /// <summary>One move's proving score, and enough context to explain a verdict without re-running
    /// the proof.</summary>
    public readonly struct ProvenMoveScore
    {
        public readonly MoveCommand Move;

        /// <summary>Material the mover ends up with by force after this move, in centipawns, from
        /// the mover's own point of view. Positive means the mover comes out ahead.</summary>
        public readonly int ScoreCp;

        public ProvenMoveScore(MoveCommand move, int scoreCp)
        {
            Move = move;
            ScoreCp = scoreCp;
        }

        public override string ToString() => $"{Move.StartPosition}->{Move.EndPosition} {ScoreCp}cp";
    }

    /// <summary>
    /// Decides whether one quiet move wins material by force, and by how much more than every
    /// alternative.
    ///
    /// The existing yardstick proofs answer "is this an immediate mate?" and "is this a clean
    /// capture no alternative matches?" Both are single-ply questions about material that is
    /// already on the board, which makes them unable to express the case this exists for: a quiet
    /// move whose point arrives several plies later, such as a pawn push that cannot be stopped
    /// from queening. Positions like that are the only ones that can measure an evaluation term,
    /// because a move with no immediate material consequence is exactly what the positional terms
    /// are for.
    ///
    /// Every score here comes from a search driven by MaterialOnlyEvaluator, never the production
    /// evaluator - see that class for why a proof that consulted the real evaluator would be
    /// circular and therefore worthless.
    /// </summary>
    public static class QuietMoveProver
    {
        /// <summary>
        /// Scores every legal move by how much material the mover wins by force within the given
        /// depth, best first.
        ///
        /// The search runs from the side that must reply, so its answer already accounts for the
        /// opponent defending as well as it can; the result is negated back into the mover's point
        /// of view. It is bound by depth alone with no cancellation and no clock, so the verdict is
        /// a property of the position rather than of how busy the machine was when it ran - a proof
        /// that changed under load would not be a proof.
        /// </summary>
        public static List<ProvenMoveScore> ScoreAllMoves(BoardState board, int provingDepth)
        {
            var engine = new ChessEngineAdapter();
            Team mover = board.CurrentTurn;

            var legalMoves = new List<MoveCommand>(64);
            engine.GetAllLegalMovesIncludingBetrayal(board, mover, legalMoves);

            var scored = new List<ProvenMoveScore>(legalMoves.Count);
            foreach (MoveCommand candidate in legalMoves)
                scored.Add(new ProvenMoveScore(candidate, ScoreAfter(board, candidate, mover, provingDepth)));

            scored.Sort((a, b) => b.ScoreCp.CompareTo(a.ScoreCp));
            return scored;
        }

        /// <summary>
        /// The mover's forced material outcome after playing one candidate move.
        ///
        /// Uses Advance rather than the raw ApplyMove the search uses internally, because Advance is
        /// what actually flips the side to move and resolves a Betrayal sub-phase. A raw ApplyMove
        /// would leave the turn unflipped and hand the follow-up search to the wrong side, which
        /// silently inverts the whole result.
        /// </summary>
        private static int ScoreAfter(BoardState board, MoveCommand candidate, Team mover, int provingDepth)
        {
            var engine = new ChessEngineAdapter();
            BoardState afterMove = board.CloneForSnapshot();
            engine.Advance(afterMove, candidate);

            Team opponent = mover == Team.White ? Team.Black : Team.White;

            // A move that mates outright ends the line here: there is nothing left to search, and a
            // material score would badly understate it. Ranked above any amount of won material so
            // a mate always sorts first.
            GameState state = engine.EvaluateGameState(afterMove, opponent);
            if (state == GameState.Checkmate) return MateScoreCp;
            if (state == GameState.Stalemate) return 0;

            // A fresh search per candidate, so no transposition table carries a conclusion from one
            // candidate into another and every move is judged on its own.
            var search = new AlphaBetaSearch(engine, new MaterialOnlyEvaluator());
            var settings = new AISearchSettings(provingDepth, ProvingTimeBudget, BetrayalUsage.Full);

            search.FindBestMove(afterMove, settings, CancellationToken.None);
            if (search.RootMoveCount == 0) return 0;

            // The search answered from the opponent's point of view, since it is their turn after
            // the candidate move. Negate to get the mover's.
            return -search.RootScores[search.BestRootIndex];
        }

        /// <summary>Ranked above any material outcome so a mating move always sorts first, while
        /// staying far below the search's own mate scores so it can never be confused with one.</summary>
        public const int MateScoreCp = 100000;

        /// <summary>
        /// Deliberately generous, and never expected to bind: the proving search is depth-bound and
        /// the budget exists only so a pathological position cannot hang a test run forever. A proof
        /// that ran out of time would be reporting the clock, not the position.
        /// </summary>
        private static AITimeBudget ProvingTimeBudget => new AITimeBudget(600000, 600000);
    }
}
