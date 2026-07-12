using System;
using System.Diagnostics;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
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
        private readonly Func<BetrayalUsage, AIProfile, AISearchSettings> _searchSettingsFactory;
        private readonly IAIProfileProvider _profileProvider;

        // Optional — null in most tests. Verbose-gated AI-lifecycle logging so a human can tell the
        // background search is actually running (and how long it took) instead of guessing. Never
        // an error surface — that's the separate ConsumeLastSearchException path.
        private readonly IDomainLogger _logger;

        private IAIAgent _aiAgent;
        private Team _aiTeam;

        // The MaxDepth of the settings the current agent was built with — captured in SetAIMode so
        // AI_SearchRequested can report the configured search depth without re-invoking the factory.
        private int _configuredDepth;

        // Times a single search from request to main-thread delivery, so AI_MoveDecided can report
        // elapsed ms. Stopwatch (not Time.*) because it must be readable independent of the Unity
        // main-thread clock; started in TryRequestMove, read in HandleMoveDecided.
        private readonly Stopwatch _searchStopwatch = new Stopwatch();

        /// <summary>True once <see cref="SetAIMode"/> has constructed an agent for this session.</summary>
        public bool IsAiMode => _aiAgent != null;

        /// <summary>True while a background search is in flight — UndoService's cancel-before-pop ordering reads this.</summary>
        public bool IsSearchInFlight => _aiAgent is AsyncAIAgent asyncAgent && asyncAgent.IsSearching;

        /// <summary>Fires when the AI's background search worker throws. TEMP debug surface (see AsyncAIAgent) — GameManager routes it to Debug.LogError.</summary>
        public event Action<string> OnSearchException;

        public AIMatchCoordinator(IChessEngine engine, BoardState board, Action<MoveCommand> playMove, IDomainLogger logger = null)
            : this(engine, board, playMove, AISearchSettings.FromProfile, new AIProfileTableProvider(), logger)
        {
        }

        /// <summary>
        /// Lets a caller substitute a shallow/fast <see cref="AISearchSettings"/> factory (e.g.
        /// maxDepth: 1) and/or profile provider instead of the production
        /// <see cref="AISearchSettings.FromProfile"/> mapping — used by tests so search-lifecycle
        /// assertions (cancellation, delivery, IsSearchInFlight) don't have to wait out a
        /// full-depth search. GameManager's composition root always uses the single-argument
        /// constructor above.
        /// </summary>
        public AIMatchCoordinator(
            IChessEngine engine, BoardState board, Action<MoveCommand> playMove,
            Func<BetrayalUsage, AIProfile, AISearchSettings> searchSettingsFactory,
            IAIProfileProvider profileProvider, IDomainLogger logger = null)
        {
            _engine = engine;
            _board = board;
            _playMove = playMove;
            _searchSettingsFactory = searchSettingsFactory;
            _profileProvider = profileProvider ?? new AIProfileTableProvider();
            _logger = logger;
        }

        /// <summary>
        /// Configures the session for AI play and constructs the background-thread search agent.
        /// AI sessions always run untimed — the caller is responsible for bypassing clock setup.
        /// </summary>
        public void SetAIMode(Team aiTeam, BetrayalUsage betrayalUsage, string aiProfileId)
        {
            _aiTeam = aiTeam;

            TearDownAgent();

            AIProfile profile = _profileProvider.Resolve(aiProfileId);
            AISearchSettings settings = _searchSettingsFactory(betrayalUsage, profile);
            _configuredDepth = settings.MaxDepth;

            EvaluationWeights weights = EvaluationWeights.FromProfile(profile);

            var agent = new AsyncAIAgent(
                _engine,
                new BetrayalAwareEvaluator(weights),
                settings,
                profile,
                new SystemRandomSource());

            agent.OnMoveDecided += HandleMoveDecided;
            _aiAgent = agent;
        }

        /// <summary>Call once it's aiTeam's turn in a live match to kick off a background search.</summary>
        public void TryRequestMove(bool isGameActive)
        {
            if (!AITurnGate.ShouldRequestMove(_aiAgent != null, _board.CurrentTurn, _aiTeam, isGameActive)) return;

            if (_logger != null && _logger.IsVerbose)
            {
                _logger.LogInfo(new DomainLogEvent(DomainEventCode.AI_SearchRequested, message: $"{_aiTeam} to move", auxInt: _configuredDepth));
            }

            _searchStopwatch.Restart();
            _aiAgent.RequestBestMove(_board, _aiTeam);
        }

        /// <summary>Cancels an in-flight search without tearing down the agent — used by Undo's cancel-before-pop ordering, so the agent stays usable for the player's very next move.</summary>
        public void CancelInFlightSearch()
        {
            if (_aiAgent is not AsyncAIAgent asyncAgent || !asyncAgent.IsSearching) return;

            asyncAgent.CancelSearch();
            _searchStopwatch.Reset();

            if (_logger != null && _logger.IsVerbose)
            {
                _logger.LogInfo(new DomainLogEvent(DomainEventCode.AI_SearchCancelled, message: $"{_aiTeam} search cancelled before it replied"));
            }
        }

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
        private void HandleMoveDecided(MoveCommand move)
        {
            if (_searchStopwatch.IsRunning) _searchStopwatch.Stop();

            if (_logger != null && _logger.IsVerbose)
            {
                // ElapsedMilliseconds is a long; clamp into the int AuxInt payload. A search that
                // somehow ran >24 days to overflow int is a bug worth seeing pinned at int.MaxValue.
                long elapsedMs = _searchStopwatch.ElapsedMilliseconds;
                int auxMs = elapsedMs > int.MaxValue ? int.MaxValue : (int)elapsedMs;
                _logger.LogInfo(new DomainLogEvent(DomainEventCode.AI_MoveDecided, message: $"{_aiTeam} plays {move}", auxInt: auxMs));
            }

            _playMove(move);
        }

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
