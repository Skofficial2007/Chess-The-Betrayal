using System;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Owns the background-thread AI agent's lifecycle: constructing it for a session, deciding
    /// when to ask it for a move, pumping its main-thread result delivery, and tearing it down.
    /// Extracted from GameManager (AI-13) so the AI-specific slice of match orchestration is a
    /// plain, testable C# class instead of MonoBehaviour-embedded code.
    ///
    /// Takes the move-playing seam as a delegate (<see cref="_playMove"/>) rather than a
    /// <see cref="MatchDriver"/> reference — the coordinator's only call into match-flow is
    /// "play this move," so a narrow <see cref="Action{MoveCommand}"/> is the smallest correct
    /// seam, matching the existing IMoveExecutor.OnMoveConfirmed wiring pattern already used
    /// elsewhere in this codebase. No back-reference to GameManager or MatchDriver.
    /// </summary>
    public sealed class AIMatchCoordinator : IDisposable
    {
        private readonly IChessEngine _engine;
        private readonly BoardState _board;
        private readonly Action<MoveCommand> _playMove;
        private readonly Func<BetrayalUsage, AISearchSettings> _searchSettingsFactory;

        private IAIAgent _aiAgent;
        private Team _aiTeam;

        /// <summary>True once <see cref="SetAIMode"/> has constructed an agent for this session.</summary>
        public bool IsAiMode => _aiAgent != null;

        /// <summary>True while a background search is in flight — UndoService's cancel-before-pop ordering reads this.</summary>
        public bool IsSearchInFlight => _aiAgent is AsyncAIAgent asyncAgent && asyncAgent.IsSearching;

        /// <summary>Fires when the AI's background search worker throws. TEMP debug surface (see AsyncAIAgent) — GameManager routes it to Debug.LogError.</summary>
        public event Action<string> OnSearchException;

        public AIMatchCoordinator(IChessEngine engine, BoardState board, Action<MoveCommand> playMove)
            : this(engine, board, playMove, AISearchSettings.Ultimate)
        {
        }

        /// <summary>
        /// Lets a caller substitute a shallow/fast <see cref="AISearchSettings"/> factory (e.g.
        /// maxDepth: 1) instead of the production <see cref="AISearchSettings.Ultimate"/> depth-7
        /// search — used by tests so search-lifecycle assertions (cancellation, delivery,
        /// IsSearchInFlight) don't have to wait out a full-depth search. GameManager's composition
        /// root always uses the single-argument constructor above.
        /// </summary>
        public AIMatchCoordinator(
            IChessEngine engine, BoardState board, Action<MoveCommand> playMove,
            Func<BetrayalUsage, AISearchSettings> searchSettingsFactory)
        {
            _engine = engine;
            _board = board;
            _playMove = playMove;
            _searchSettingsFactory = searchSettingsFactory;
        }

        /// <summary>
        /// Configures the session for AI play and constructs the background-thread search agent.
        /// AI sessions always run untimed — the caller is responsible for bypassing clock setup.
        /// </summary>
        public void SetAIMode(Team aiTeam, BetrayalUsage betrayalUsage)
        {
            _aiTeam = aiTeam;

            TearDownAgent();

            var agent = new AsyncAIAgent(
                _engine,
                new BetrayalAwareEvaluator(),
                _searchSettingsFactory(betrayalUsage));

            agent.OnMoveDecided += HandleMoveDecided;
            _aiAgent = agent;
        }

        /// <summary>Call once it's aiTeam's turn in a live match to kick off a background search.</summary>
        public void TryRequestMove(bool isGameActive)
        {
            if (!AITurnGate.ShouldRequestMove(_aiAgent != null, _board.CurrentTurn, _aiTeam, isGameActive)) return;
            _aiAgent.RequestBestMove(_board, _aiTeam);
        }

        /// <summary>Cancels an in-flight search without tearing down the agent — used by Undo's cancel-before-pop ordering, so the agent stays usable for the player's very next move.</summary>
        public void CancelInFlightSearch() => (_aiAgent as AsyncAIAgent)?.CancelSearch();

        /// <summary>Pumps the background agent so a completed search hands its move back on the main thread. Call from a MonoBehaviour Update().</summary>
        public void Tick()
        {
            if (_aiAgent is not AsyncAIAgent asyncAgent) return;

            asyncAgent.Tick();

            string searchException = asyncAgent.ConsumeLastSearchException();
            if (searchException != null)
            {
                OnSearchException?.Invoke(searchException);
            }
        }

        /// <summary>
        /// Feeds the AI's chosen move through the exact same seam a human move takes — the AI
        /// never gets a special-cased execution path. Runs on the main thread: AsyncAIAgent.Tick()
        /// only raises this from Tick() above.
        /// </summary>
        private void HandleMoveDecided(MoveCommand move) => _playMove(move);

        private void TearDownAgent()
        {
            if (_aiAgent is AsyncAIAgent asyncAgent)
            {
                asyncAgent.OnMoveDecided -= HandleMoveDecided;
                asyncAgent.Dispose();
            }
            _aiAgent = null;
        }

        public void Dispose() => TearDownAgent();
    }
}
