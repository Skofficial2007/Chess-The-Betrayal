using NUnit.Framework;
using ChessTheBetrayal.EditorTools.Benchmark;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Reports tournament progress through TestContext.Progress — NUnit's write-through progress
    /// channel, distinct from TestContext.WriteLine/Out, which buffer and only surface once the
    /// test method returns. A long-running test using the buffered channel looks identical to a
    /// stuck one from outside; this is what actually lets a human or CI watch a slow-but-alive run
    /// distinguish itself from a genuinely hung one while it's still in progress.
    /// </summary>
    public sealed class TestContextProgressSink : ITournamentProgress
    {
        private readonly string _label;

        public TestContextProgressSink(string label)
        {
            _label = label;
        }

        public void ReportGameCompleted(int current, int total) =>
            TestContext.Progress.WriteLine($"[{_label}] {current}/{total} games complete");
    }
}
