using ChessTheBetrayal.AI;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Shared AITimeBudget values for tests that don't care about time-budget specifics — most
    /// correctness/ordering/pruning fixtures search a fixed position under CancellationToken.None
    /// or a generous cap and just need "won't run out of time," not a particular soft/hard shape.
    /// Tests that actually exercise budget behavior (SearchTimeBudgetTests, the two benchmark
    /// suites) construct their own deliberate AITimeBudget instead of using this.
    /// </summary>
    public static class TestTimeBudgets
    {
        public static readonly AITimeBudget Generous = new AITimeBudget(60_000, 60_000);
    }
}
