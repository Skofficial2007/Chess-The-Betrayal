using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Gameplay.Manager;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Manager
{
    /// <summary>
    /// AgentActivity is AIMatchCoordinator's local-AI-only presentation state machine (Idle,
    /// Searching, ResultReady — see the enum's doc comment). These tests exercise every legal
    /// transition directly through the coordinator's public lifecycle methods, mirroring
    /// AIMatchCoordinatorTests' fixture construction.
    /// </summary>
    [TestFixture]
    public class AgentActivityStateTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollIntervalMs = 10;

        private static AISearchSettings ShallowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 1, TestTimeBudgets.Generous, usage);

        private static AISearchSettings SlowSettings(BetrayalUsage usage, AIProfile profile) =>
            new AISearchSettings(maxDepth: 32, TestTimeBudgets.Generous, usage);

        private static readonly IAIProfileProvider ProfileProvider = new AIProfileTableProvider();

        private ChessEngineAdapter _engine;
        private BoardState _board;
        private AIMatchCoordinator _coordinator;
        private MoveCommand? _lastPlayedMove;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _board = TestBoardSetupUtility.CreateStandard();
            _lastPlayedMove = null;

            _coordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, ShallowSettings, ProfileProvider);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator.Dispose();
        }

        private void PumpTickUntil(System.Func<bool> isDone)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!isDone() && stopwatch.ElapsedMilliseconds < PollTimeoutMs)
            {
                _coordinator.Tick();
                Thread.Sleep(PollIntervalMs);
            }
        }

        [Test]
        public void Activity_StartsIdle()
        {
            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle));
        }

        [Test]
        public void Activity_SetAIMode_StaysIdle()
        {
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle));
        }

        [Test]
        public void Activity_TryRequestMove_TransitionsToSearching()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Searching));
        }

        [Test]
        public void Activity_TryRequestMove_NotAiTurn_StaysIdle()
        {
            _board.CurrentTurn = Team.White;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                "A no-op request (wrong team's turn) must never leave Idle.");
        }

        [Test]
        public void Activity_SearchDelivered_FallsBackToIdleAfterMovePlayed()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            _coordinator.TryRequestMove(isGameActive: true);
            PumpTickUntil(() => _lastPlayedMove.HasValue);

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                "ResultReady is a one-tick pulse; by the time Tick() returns the move has already been played and the machine has fallen back to Idle.");
        }

        [Test]
        public void Activity_CancelInFlightSearch_TransitionsStraightBackToIdle()
        {
            var slowCoordinator = new AIMatchCoordinator(_engine, _board, move => _lastPlayedMove = move, SlowSettings, ProfileProvider);
            _board.CurrentTurn = Team.Black;

            try
            {
                slowCoordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
                slowCoordinator.TryRequestMove(isGameActive: true);
                Assert.That(slowCoordinator.Activity, Is.EqualTo(AgentActivity.Searching));

                slowCoordinator.CancelInFlightSearch();

                Assert.That(slowCoordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                    "Cancellation must never visit ResultReady — it drops straight back to Idle.");
            }
            finally
            {
                slowCoordinator.Dispose();
            }
        }

        /// <summary>
        /// The book path is the one that used to escape cancellation. A book reply is answered
        /// synchronously inside RequestBestMove and never builds a cancellation source, so it sits
        /// decided-but-undelivered while IsSearching reads false — and a cancel that only asked
        /// IsSearching walked straight past it, leaving the move to be delivered on the next Tick()
        /// and played against whatever position Undo had just rewound to.
        /// </summary>
        [Test]
        public void CancelInFlightSearch_BookReplyDecidedButNotDelivered_DiscardsIt()
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile("e2e4 e7e5");
            var book = ScriptableObject.CreateInstance<OpeningBookAsset>();
            book.SetEntries(keys, packedMoves, weights, schemeVersion);

            // A book is keyed by Zobrist hash, so this has to be the exact position the compiler
            // keyed. The fixture's own standard board spends the Betrayal right and therefore
            // hashes differently — using it turns this into an ordinary search that cancels fine,
            // and the test passes while proving nothing.
            BoardState bookBoard = OpeningBookCompiler.CreateStandardStartingPosition();

            MoveCommand? playedFromBook = null;
            var coordinator = new AIMatchCoordinator(
                _engine, bookBoard, move => playedFromBook = move, SlowSettings, ProfileProvider);

            try
            {
                Assert.That(OpeningBookLookup.TryGetBookMove(book, bookBoard, _engine, new SystemRandomSource()), Is.Not.Null,
                    "Guard: the book must actually answer this position.");
                Assert.That(OpeningBookPolicy.ShouldConsult(ProfileProvider.Resolve("normal"), bookBoard), Is.True,
                    "Guard: this tier must actually be allowed to consult the book here.");

                coordinator.SetAIMode(Team.White, BetrayalUsage.Full, "normal", book);
                coordinator.TryRequestMove(isGameActive: true);

                Assert.That(coordinator.IsSearchInFlight, Is.True,
                    "The AI still owes a reply — it just answered from the book instead of searching for it.");

                coordinator.CancelInFlightSearch();

                Assert.That(coordinator.Activity, Is.EqualTo(AgentActivity.Idle));

                coordinator.Tick();

                Assert.That(playedFromBook, Is.Null,
                    "A cancelled book reply must never reach the board — it was chosen for a position Undo is about to discard.");
            }
            finally
            {
                coordinator.Dispose();
                Object.DestroyImmediate(book);
            }
        }

        [Test]
        public void Activity_SetAIModeCalledAgainWhileSearching_ResetsToIdle()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);
            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Searching));

            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle),
                "Reconfiguring mid-search must tear down the old agent and reset the state machine.");
        }

        [Test]
        public void Activity_Dispose_ResetsToIdle()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");
            _coordinator.TryRequestMove(isGameActive: true);

            _coordinator.Dispose();

            Assert.That(_coordinator.Activity, Is.EqualTo(AgentActivity.Idle));
        }

        [Test]
        public void IsSearchInFlight_MirrorsSearchingState()
        {
            _board.CurrentTurn = Team.Black;
            _coordinator.SetAIMode(Team.Black, BetrayalUsage.Full, "normal");

            Assert.That(_coordinator.IsSearchInFlight, Is.False);

            _coordinator.TryRequestMove(isGameActive: true);
            Assert.That(_coordinator.IsSearchInFlight, Is.True);

            PumpTickUntil(() => _lastPlayedMove.HasValue);
            Assert.That(_coordinator.IsSearchInFlight, Is.False);
        }
    }
}
