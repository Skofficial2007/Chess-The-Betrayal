using NUnit.Framework;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Agent;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

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
        public void RequestBestMove_AfterDelivery_ExposesTheCompletedSearchsDepthAndStopReason()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            bool delivered = false;
            _agent.OnMoveDecided += _ => delivered = true;

            _agent.RequestBestMove(board, Team.White);
            PumpTickUntil(() => delivered);

            Assert.That(_agent.LastCompletedDepth, Is.GreaterThan(0),
                "A depth-1 search that completed normally must report at least depth 1, not the zero default.");
            Assert.That(_agent.StopReason, Is.Not.EqualTo(SearchStopReason.Unset),
                "Every real search sets a concrete stop reason before returning.");
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
            // Integration smoke test: a real personality profile (which wires the move-selection
            // epilogue in via the full constructor) must not break the existing
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

        /// <summary>
        /// A two-ply book: White's opening move, and Black's reply to it. The position after both
        /// have been played is deliberately NOT in the book, so the agent's second turn is the
        /// first move it has to think about — which is the boundary these tests are about.
        /// </summary>
        private static OpeningBookAsset TwoPlyBook()
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile("e2e4 e7e5");
            var asset = ScriptableObject.CreateInstance<OpeningBookAsset>();
            asset.SetEntries(keys, packedMoves, weights, schemeVersion);
            return asset;
        }

        /// <summary>A tier that always consults the book and has no personality dials, so what the
        /// agent delivers is decided entirely by the book and the search.</summary>
        private static AIProfile BookProfile() => new AIProfile(
            "book-test", maxDepth: 1, timeBudget: new AITimeBudget(5000, 5000), blunderRate: 0f,
            blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f, tieBreakWindowCp: 0,
            useOpeningBook: true, openingBookDepthPlies: 0);

        private static AsyncAIAgent BookAgent(OpeningBookAsset book) => new AsyncAIAgent(
            new ChessEngineAdapter(),
            new BetrayalAwareEvaluator(),
            new AISearchSettings(maxDepth: 1, new AITimeBudget(5000, 5000), BetrayalUsage.Full),
            BookProfile(),
            new SystemRandomSource(seed: 31),
            book);

        /// <summary>Plays a specific move by locating it among the legal moves, advancing the turn
        /// the way a real game does.</summary>
        private static void Play(BoardState board, string from, string to)
        {
            var engine = new ChessEngineAdapter();
            var legalMoves = new System.Collections.Generic.List<MoveCommand>();
            engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

            Vector2Int start = TestBoardSetupUtility.AlgebraicToVector(from);
            Vector2Int end = TestBoardSetupUtility.AlgebraicToVector(to);

            foreach (MoveCommand candidate in legalMoves)
            {
                if (candidate.StartPosition == start && candidate.EndPosition == end)
                {
                    new TurnResolver().Advance(board, candidate);
                    return;
                }
            }

            Assert.Fail($"'{from}{to}' is not legal in this position.");
        }

        [Test]
        public void OnLeftOpeningBook_FiresOnceOnTheFirstMoveTheAgentHasToWorkOutItself()
        {
            OpeningBookAsset book = TwoPlyBook();
            AsyncAIAgent agent = BookAgent(book);
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            int leftBookCount = 0;
            int bookMoveCount = 0;
            MoveCommand? firstOwnMove = null;

            agent.OnLeftOpeningBook += move => { leftBookCount++; firstOwnMove = move; };
            agent.OnBookMovePlayed += move => { bookMoveCount++; new TurnResolver().Advance(board, move); };
            agent.OnMoveDecided += move => new TurnResolver().Advance(board, move);

            try
            {
                // White's first move comes straight from the book, so no boundary has been crossed.
                agent.RequestBestMove(board, Team.White);
                PumpUntil(agent, () => bookMoveCount == 1);
                Assert.That(leftBookCount, Is.Zero, "Playing a book move is not leaving the book.");

                Play(board, "e7", "e5");

                // The book has nothing here, so this move is the agent's own — the boundary.
                int decided = 0;
                agent.OnMoveDecided += _ => decided++;
                agent.RequestBestMove(board, Team.White);
                PumpUntil(agent, () => decided == 1);

                Assert.That(leftBookCount, Is.EqualTo(1),
                    "The first move the agent searched for after a run of book moves must announce leaving the book.");
                Assert.That(firstOwnMove, Is.Not.Null);

                // Every later move is also searched, but the book was already left — announcing it
                // again would turn a one-off boundary into a per-move message.
                Play(board, "b8", "c6");
                agent.RequestBestMove(board, Team.White);
                PumpUntil(agent, () => decided == 2);

                Assert.That(leftBookCount, Is.EqualTo(1), "Leaving the book must be announced once, not on every searched move.");
            }
            finally
            {
                agent.Dispose();
                Object.DestroyImmediate(book);
            }
        }

        /// <summary>
        /// Pins the contract that a book move reports depth 0 / StopReason.Unset. On a freshly
        /// constructed agent this is also plain C# default-field behavior, so on its own this test
        /// cannot tell "the book branch resets these on purpose" apart from "nobody ever wrote to
        /// them" — the test below forces a real search first specifically to close that gap.
        /// </summary>
        [Test]
        public void OnBookMovePlayed_NeverRanASearch_SoDepthAndStopReasonStayAtTheirZeroDefault()
        {
            OpeningBookAsset book = TwoPlyBook();
            AsyncAIAgent agent = BookAgent(book);
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            bool bookMovePlayed = false;
            agent.OnBookMovePlayed += _ => bookMovePlayed = true;

            try
            {
                agent.RequestBestMove(board, Team.White);
                PumpUntil(agent, () => bookMovePlayed);

                Assert.That(agent.LastCompletedDepth, Is.Zero,
                    "A book hit never ran a search, so there is no completed depth to report.");
                Assert.That(agent.StopReason, Is.EqualTo(SearchStopReason.Unset));
            }
            finally
            {
                agent.Dispose();
                Object.DestroyImmediate(book);
            }
        }

        [Test]
        public void OnBookMovePlayed_AfterAPriorRealSearch_ClearsTheOldSearchsDepthRatherThanLeakingIt()
        {
            // TwoPlyBook only covers "e2e4 e7e5", so 1.Nf3 is off the book entirely and Black's
            // reply to it must be a genuine search — that gives LastCompletedDepth a real, nonzero
            // value to leak from. The second call hands the same agent the exact starting position
            // the book does cover, which must hit it. Together these prove the book branch's reset
            // is doing real work, not just reflecting a never-touched agent's zero defaults.
            OpeningBookAsset book = TwoPlyBook();
            AsyncAIAgent agent = BookAgent(book);

            try
            {
                BoardState offBookBoard = OpeningBookCompiler.CreateStandardStartingPosition();
                Play(offBookBoard, "g1", "f3");

                bool searched = false;
                agent.OnMoveDecided += _ => searched = true;
                agent.RequestBestMove(offBookBoard, Team.Black);
                PumpUntil(agent, () => searched);

                Assert.That(agent.LastCompletedDepth, Is.GreaterThan(0),
                    "Sanity check: this call must have actually searched, or the reset assertion below proves nothing.");

                bool bookMovePlayed = false;
                agent.OnBookMovePlayed += _ => bookMovePlayed = true;
                agent.RequestBestMove(OpeningBookCompiler.CreateStandardStartingPosition(), Team.White);
                PumpUntil(agent, () => bookMovePlayed);

                Assert.That(agent.LastCompletedDepth, Is.Zero,
                    "A book hit must clear the previous search's depth, not leave it reading stale.");
                Assert.That(agent.StopReason, Is.EqualTo(SearchStopReason.Unset));
            }
            finally
            {
                agent.Dispose();
                Object.DestroyImmediate(book);
            }
        }

        [Test]
        public void OnLeftOpeningBook_WhenTheAgentNeverPlayedFromTheBook_NeverFires()
        {
            // Absence of the signal has to mean "searched from move one", never "still in book" —
            // otherwise a log with no boundary in it is ambiguous exactly when it matters.
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            bool leftBook = false;
            bool delivered = false;

            _agent.OnLeftOpeningBook += _ => leftBook = true;
            _agent.OnMoveDecided += _ => delivered = true;

            _agent.RequestBestMove(board, Team.White);
            PumpTickUntil(() => delivered);

            Assert.That(delivered, Is.True);
            Assert.That(leftBook, Is.False, "An agent with no book behind it has no book to leave.");
        }

        /// <summary>Tick-pumping variant for the locally-constructed agents above, which the shared
        /// PumpTickUntil helper cannot reach because it pumps the fixture's own agent.</summary>
        private static void PumpUntil(AsyncAIAgent agent, System.Func<bool> isDone)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!isDone() && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
            {
                agent.Tick();
                Thread.Sleep(PollIntervalMs);
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
