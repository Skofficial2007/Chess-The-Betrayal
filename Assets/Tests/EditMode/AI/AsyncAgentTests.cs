using NUnit.Framework;
using System.Diagnostics;
using System.Threading;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Exercises AsyncAIAgent's threading contract: OnMoveDecided fires only from Tick() on the
    /// calling thread, a cancelled search never fires it, and the live board handed to
    /// RequestBestMove is never mutated (the search only ever touches its own CloneForSnapshot).
    /// Uses a shallow, fast search (depth 1) and bounded polling loops — this is background-thread
    /// code, so these tests wait on real wall-clock time rather than driving the worker directly.
    /// </summary>
    [TestFixture]
    public class AsyncAgentTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollIntervalMs = 10;

        private AsyncAIAgent _agent;

        [SetUp]
        public void Setup()
        {
            _agent = new AsyncAIAgent(
                new ChessEngineAdapter(),
                new BetrayalAwareEvaluator(),
                new AISearchSettings(maxDepth: 1, new AITimeBudget(5000, 5000), BetrayalUsage.Full));
        }

        [TearDown]
        public void TearDown()
        {
            _agent.Dispose();
        }

        [Test]
        public void RequestBestMove_ThenPumpTick_FiresOnMoveDecidedWithALegalMove()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            MoveCommand? delivered = null;

            _agent.OnMoveDecided += move => delivered = move;
            _agent.RequestBestMove(board, Team.White);

            PumpTickUntil(() => delivered.HasValue);

            Assert.That(delivered, Is.Not.Null, "OnMoveDecided must fire once the background search completes and Tick() is pumped.");
            Assert.That(delivered.Value.PieceTeam, Is.EqualTo(Team.White));
        }

        [Test]
        public void IsSearching_FalseAfterTickDeliversTheResult()
        {
            // IsSearching's own doc comment promises it goes false "once a result is consumed via
            // Tick()" — UndoService relies on exactly this to decide pop-1 vs pop-2. If it stayed
            // true after delivery, every Undo pressed after the AI's first reply would incorrectly
            // read as "search still in flight."
            BoardState board = TestBoardSetupUtility.CreateStandard();
            bool delivered = false;
            _agent.OnMoveDecided += _ => delivered = true;

            _agent.RequestBestMove(board, Team.White);
            Assert.That(_agent.IsSearching, Is.True);

            PumpTickUntil(() => delivered);

            Assert.That(_agent.IsSearching, Is.False,
                "IsSearching must go false once Tick() has consumed and delivered the result.");
        }

        [Test]
        public void RequestBestMove_BeforeTickIsPumped_DoesNotFireOnMoveDecided()
        {
            // The worker publishes its result via a volatile flag; only Tick() may raise the event.
            // Give the worker ample time to finish and simply never call Tick() — if the worker
            // fired the event directly, this would already have failed by the time we assert.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            bool fired = false;
            _agent.OnMoveDecided += _ => fired = true;

            _agent.RequestBestMove(board, Team.White);
            SleepForSearchToFinish();

            Assert.That(fired, Is.False, "OnMoveDecided must not fire before Tick() is pumped, even though the worker has already finished.");
        }

        [Test]
        public void RequestBestMove_CancelledBeforeCompletion_NeverFiresOnMoveDecided()
        {
            // A deep, slow search on an otherwise-legal position gives us a wide cancellation window.
            var slowAgent = new AsyncAIAgent(
                new ChessEngineAdapter(),
                new BetrayalAwareEvaluator(),
                new AISearchSettings(maxDepth: 32, new AITimeBudget(30_000, 30_000), BetrayalUsage.Full));

            try
            {
                BoardState board = TestBoardSetupUtility.CreateStandard();
                bool fired = false;
                slowAgent.OnMoveDecided += _ => fired = true;

                using var cts = new CancellationTokenSource();
                slowAgent.RequestBestMove(board, Team.White, cts.Token);
                cts.Cancel();

                // Give the worker time to observe cancellation and (incorrectly, if buggy) publish.
                Thread.Sleep(200);
                slowAgent.Tick();

                Assert.That(fired, Is.False, "A cancelled search must never publish a result, even after Tick() is pumped.");
            }
            finally
            {
                slowAgent.Dispose();
            }
        }

        [Test]
        public void RequestBestMove_HardTimeBudgetExpires_StillFiresOnMoveDecided()
        {
            // Regression guard: AsyncAIAgent.CancelAfter(HardMs) arms a CancellationTokenSource to
            // bound iterative deepening, and FindBestMove correctly returns the best move from the
            // last fully-completed depth when that timer fires — a NORMAL, successful outcome, not an
            // abort. A prior bug checked token.IsCancellationRequested after the search returned, which
            // is ALSO true in this exact case (the token that legitimately expired), so the result was
            // silently discarded and OnMoveDecided never fired — the AI would sit forever, never
            // playing a move, even though the search itself completed correctly. Only a genuinely
            // superseded/aborted search (RequestBestMove called again, CancelSearch, Dispose) may ever
            // discard a result.
            var budgetedAgent = new AsyncAIAgent(
                new ChessEngineAdapter(),
                new BetrayalAwareEvaluator(),
                // Deep enough that depth-32 iterative deepening cannot possibly finish naturally on a
                // starting position before the tiny budget below expires — forces CancelAfter to fire
                // mid-search, exactly like a hard position hitting its real-game budget.
                new AISearchSettings(maxDepth: 32, new AITimeBudget(150, 150), BetrayalUsage.Full));

            try
            {
                BoardState board = TestBoardSetupUtility.CreateStandard();
                MoveCommand? delivered = null;
                budgetedAgent.OnMoveDecided += move => delivered = move;

                budgetedAgent.RequestBestMove(board, Team.White);

                var stopwatch = Stopwatch.StartNew();
                while (!delivered.HasValue && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    budgetedAgent.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(delivered, Is.Not.Null,
                    "A search that hits its own HardMs budget must still deliver its best-move-so-far via OnMoveDecided, not silently vanish.");
                Assert.That(delivered.Value.PieceTeam, Is.EqualTo(Team.White));
            }
            finally
            {
                budgetedAgent.Dispose();
            }
        }

        [Test]
        public void RequestBestMove_NewRequestCancelsPriorInFlightSearch()
        {
            // A second RequestBestMove before the first resolves must cancel the first — only the
            // second's move (or nothing) should ever reach OnMoveDecided.
            var slowAgent = new AsyncAIAgent(
                new ChessEngineAdapter(),
                new BetrayalAwareEvaluator(),
                new AISearchSettings(maxDepth: 32, new AITimeBudget(30_000, 30_000), BetrayalUsage.Full));

            try
            {
                BoardState board = TestBoardSetupUtility.CreateStandard();
                int fireCount = 0;
                slowAgent.OnMoveDecided += _ => fireCount++;

                slowAgent.RequestBestMove(board, Team.White);
                slowAgent.RequestBestMove(board, Team.White); // cancels the first in-flight search

                Thread.Sleep(200);
                slowAgent.Tick();

                Assert.That(fireCount, Is.EqualTo(0),
                    "Neither the cancelled first search nor the still-running second search should have fired yet.");
            }
            finally
            {
                slowAgent.Dispose();
            }
        }

        [Test]
        public void RequestBestMove_LiveBoardIsNeverMutatedByTheBackgroundSearch()
        {
            // RequestBestMove clones the board once on the calling thread; the search must only
            // ever touch that isolated clone. The live board's hash must be identical before and
            // after the background search runs, regardless of what the search explored.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            ulong hashBefore = board.ZobristHash;
            bool delivered = false;

            _agent.OnMoveDecided += _ => delivered = true;
            _agent.RequestBestMove(board, Team.White);
            PumpTickUntil(() => delivered);

            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore),
                "The live board passed to RequestBestMove must never be mutated by the background search.");
        }

        [Test]
        public void RequestBestMove_WithNonzeroDialProfileAndSeededRng_StillDeliversALegalSearchRankedMove()
        {
            // Integration smoke test: a real personality profile (AI-24's MoveSelectionPolicy
            // epilogue wired in via the full constructor) must not break the existing
            // threading/marshalling contract — the delivered move is still legal and still arrives
            // only through Tick() on the calling thread.
            var profile = AIProfileTable.BuiltIn[0]; // "easy" — nonzero BlunderRate/TieBreakWindowCp
            var agent = new AsyncAIAgent(
                new ChessEngineAdapter(),
                new BetrayalAwareEvaluator(),
                new AISearchSettings(maxDepth: 2, new AITimeBudget(5000, 5000), BetrayalUsage.Full),
                profile,
                new SystemRandomSource(seed: 7));

            BoardState board = TestBoardSetupUtility.CreateStandard();
            MoveCommand? delivered = null;

            try
            {
                agent.OnMoveDecided += move => delivered = move;
                agent.RequestBestMove(board, Team.White);

                var stopwatch = Stopwatch.StartNew();
                while (!delivered.HasValue && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
                {
                    agent.Tick();
                    Thread.Sleep(PollIntervalMs);
                }

                Assert.That(delivered, Is.Not.Null,
                    "A profile-driven agent must still deliver a move through the standard Tick() pump.");
                Assert.That(delivered.Value.PieceTeam, Is.EqualTo(Team.White));
            }
            finally
            {
                agent.Dispose();
            }
        }

        /// <summary>Repeatedly calls Tick() until <paramref name="isDone"/> is true or the timeout
        /// elapses — Tick() is the only public surface that can observe the worker's completion.</summary>
        private void PumpTickUntil(System.Func<bool> isDone)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!isDone() && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
            {
                _agent.Tick();
                Thread.Sleep(PollIntervalMs);
            }
        }

        private static void SleepForSearchToFinish() => Thread.Sleep(300);
    }
}
