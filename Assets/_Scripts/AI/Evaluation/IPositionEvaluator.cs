using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.AI.Evaluation
{
    /// <summary>
    /// Scores a board position from one team's point of view. Positive means that team is winning, negative means they're losing.
    /// </summary>
    public interface IPositionEvaluator
    {
        int Evaluate(BoardState board, Team forTeam);

        /// <summary>
        /// A cheaper partial score a caller can use to skip the full evaluation when it already
        /// falls far enough outside a search window that no additional term could change the
        /// outcome. Must never cost more than Evaluate, and Evaluate must never return something
        /// this couldn't have bounded.
        /// </summary>
        int EvaluateCheap(BoardState board, Team forTeam);

        /// <summary>
        /// The most Evaluate's result can differ from EvaluateCheap's, on any position. A caller
        /// whose cheap score already sits further outside its window than this can skip the full
        /// evaluation and trust the cheap one. Reporting this too low is not a speed tradeoff — it
        /// makes that caller keep a score the full evaluation would have contradicted, so an
        /// evaluator whose cheap path already is its full path reports zero.
        /// </summary>
        int MaxCheapToFullSwing { get; }
    }
}
