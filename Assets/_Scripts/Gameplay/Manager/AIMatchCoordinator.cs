using System;
using System.Collections.Generic;
using System.Diagnostics;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.Agent;
using ChessTheBetrayal.AI.MatchTelemetry;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Diagnostics;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Gameplay.Manager
{
    /// <summary>
    /// Local-AI-only presentation state for the background search agent, driven entirely by
    /// AIMatchCoordinator on the main thread (the worker thread only sets AsyncAIAgent's own
    /// volatile completion flag; Tick() is what observes it and advances this machine). Idle is
    /// the rest state; Searching starts the instant TryRequestMove hands work to the agent;
    /// ResultReady is a one-tick pulse the instant Tick() observes a completed search, then the
    /// machine falls straight back to Idle once HandleMoveDecided has fed the move through
    /// _playMove — nothing external needs to observe ResultReady, so it's transient by design,
    /// not a state a caller can get stuck polling. CancelInFlightSearch takes Searching straight
    /// back to Idle without ever visiting ResultReady.
    /// </summary>
    public enum AgentActivity { Idle, Searching, ResultReady }

    /// <summary>
    /// Owns the background-thread AI agent's lifecycle: constructing it for a session, deciding
    /// when to ask it for a move, pumping its main-thread result delivery, and tearing it down.
    /// Split out of GameManager so the AI-specific slice of match orchestration is a plain,
    /// testable C# class instead of MonoBehaviour-embedded code.
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

        /// <summary>Current step of the search-lifecycle state machine — see <see cref="AgentActivity"/>.</summary>
        public AgentActivity Activity { get; private set; } = AgentActivity.Idle;

        /// <summary>True while a background search is in flight — UndoService's cancel-before-pop ordering reads this.</summary>
        public bool IsSearchInFlight => Activity == AgentActivity.Searching;

        /// <summary>Fires when the AI's background search worker throws. TEMP debug surface (see AsyncAIAgent) — GameManager routes it to Debug.LogError.</summary>
        public event Action<string> OnSearchException;

        /// <summary>Off by default — set by the composition root (GameManager) if it wants a real
        /// match's AI moves recorded for later sharing. Checked when a move is about to be
        /// recorded, not when the agent is configured, so flipping it has no effect on whichever
        /// search happens to already be in flight.</summary>
        public bool RecordTelemetry { get; set; }

        /// <summary>What the AI has done so far this match. Rebuilt fresh every SetAIMode call so a
        /// second match (Replay) never blends its move history with the one before it, and survives
        /// past TearDownAgent so it can still be read after the match ends — only the next
        /// SetAIMode call replaces it. Recording into it is gated by RecordTelemetry; the object
        /// itself always exists once a session has started, so a reader never has to null-check
        /// mid-match, only before the first SetAIMode call.</summary>
        public AiMatchTelemetry Telemetry { get; private set; }

        // Optional. Supplies the device/build facts a shared report needs to be attributable to the
        // hardware it came off — null in every test and in any host with no platform to read.
        private readonly Func<IReadOnlyList<string>> _deviceFacts;

        public AIMatchCoordinator(IChessEngine engine, BoardState board, Action<MoveCommand> playMove,
            IDomainLogger logger = null, Func<IReadOnlyList<string>> deviceFacts = null)
            : this(engine, board, playMove, AISearchSettings.FromProfile, new AIProfileTableProvider(), logger,
                deviceFacts)
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
            IAIProfileProvider profileProvider, IDomainLogger logger = null,
            Func<IReadOnlyList<string>> deviceFacts = null)
        {
            _engine = engine;
            _board = board;
            _playMove = playMove;
            _searchSettingsFactory = searchSettingsFactory;
            _profileProvider = profileProvider ?? new AIProfileTableProvider();
            _logger = logger;
            _deviceFacts = deviceFacts;
        }

        /// <summary>
        /// Configures the session for AI play and constructs the background-thread search agent.
        /// AI sessions always run untimed — the caller is responsible for bypassing clock setup.
        /// openingBook is optional (null skips opening-book play entirely, so the AI searches for
        /// its own move from move one) — GameManager supplies its compiled OpeningBookAsset via
        /// the Inspector.
        /// </summary>
        public void SetAIMode(Team aiTeam, BetrayalUsage betrayalUsage, string aiProfileId, OpeningBookAsset openingBook = null)
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
                new SystemRandomSource(),
                openingBook);

            agent.OnMoveDecided += HandleMoveDecided;
            agent.OnBookMovePlayed += HandleBookMovePlayed;
            agent.OnLeftOpeningBook += HandleLeftOpeningBook;
            _aiAgent = agent;

            _plyAwaitingItsNumber = null;
            Telemetry = new AiMatchTelemetry(DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            // Stamped per match rather than once at startup, so a report that arrives on its own
            // still says which phone produced it. Without this a shared match report is a page of
            // timings attributable to no hardware at all, which is most of what makes it worth
            // sending — the same facts the device benchmark's own report carries, from the same
            // reader, so two reports off one phone can never describe it differently.
            if (_deviceFacts != null)
            {
                foreach (string line in _deviceFacts()) Telemetry.AppendHeaderLine(line);
            }

            // Which tier played, and what it was allowed. Every timing and depth in the report below
            // is only judgeable against these: an elapsed worst of 3005ms is a tier sitting on its
            // budget or one comfortably inside it depending entirely on which budget it had, and
            // nothing else in the file says. Taken from the settings the agent was actually built
            // with rather than the profile row, since a caller may substitute them.
            Telemetry.AppendHeaderLine(
                $"AI tier: {profile.Id} (max depth {settings.MaxDepth}, budget {settings.TimeBudget.HardMs}ms)");
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
            Activity = AgentActivity.Searching;
            _aiAgent.RequestBestMove(_board, _aiTeam);
        }

        /// <summary>
        /// Discards whatever reply the AI still owes — a running search, or a move already decided
        /// but not yet delivered — without tearing down the agent, so it stays usable for the
        /// player's very next move. Used by Undo's cancel-before-pop ordering.
        ///
        /// Three states have to be covered, and each was found the hard way. An opening-book reply
        /// never starts a search, so it is undelivered while IsSearching is false; gating this on
        /// IsSearching alone let a book move survive an Undo and then land on the rewound board a
        /// frame later. And a move already handed to the pacing queue is past both of those checks
        /// while still not on the board, which is what the telemetry record held for it is waiting
        /// on.
        /// </summary>
        public void CancelInFlightSearch()
        {
            // Cleared before either guard below, because a move that has already been handed on is
            // past both of them: the agent has stopped searching and has nothing undelivered, yet
            // the move can still be sitting in the pacing queue waiting for an animation to finish.
            // Undo empties that queue too, so a record held for it is waiting on a ply that will
            // now never happen, and completing it later would put a move in the log that was never
            // played.
            _plyAwaitingItsNumber = null;

            if (_aiAgent is not AsyncAIAgent asyncAgent) return;
            if (!asyncAgent.IsSearching && !asyncAgent.HasUndeliveredResult) return;

            asyncAgent.CancelSearch();
            _searchStopwatch.Reset();
            Activity = AgentActivity.Idle;

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
            Activity = AgentActivity.ResultReady;

            // ElapsedMilliseconds is a long; clamp into the int payload both the log line and the
            // telemetry record use. A search that somehow ran >24 days to overflow int is a bug
            // worth seeing pinned at int.MaxValue, not silently wrapping negative.
            long elapsedMs = _searchStopwatch.ElapsedMilliseconds;
            int auxMs = elapsedMs > int.MaxValue ? int.MaxValue : (int)elapsedMs;

            if (_logger != null && _logger.IsVerbose)
            {
                // _aiAgent is typed as the narrow IAIAgent interface, but depth/stop-reason are an
                // AsyncAIAgent-specific reading of its own last completed search, not something every
                // IAIAgent implementation has to carry — same reasoning Tick() already applies below
                // for ConsumeLastSearchException.
                string message = _aiAgent is AsyncAIAgent loggingAgent
                    ? $"{_aiTeam} plays {move} (depth {loggingAgent.LastCompletedDepth}, {loggingAgent.StopReason})"
                    : $"{_aiTeam} plays {move}";
                _logger.LogInfo(new DomainLogEvent(DomainEventCode.AI_MoveDecided, message: message, auxInt: auxMs));
            }

            if (RecordTelemetry && _aiAgent is AsyncAIAgent recordingAgent)
            {
                HoldForPlyNumber(new AiMoveRecord(PlyNumberPendingUntilItLands, _aiTeam, move,
                    AiMoveSource.Searched, auxMs, recordingAgent.LastCompletedDepth, recordingAgent.StopReason));
            }

            _playMove(move);

            Activity = AgentActivity.Idle;
        }

        /// <summary>
        /// Same hand-off as HandleMoveDecided, but for a move the opening book answered instantly
        /// with no search — logged as AI_BookMovePlayed instead of AI_MoveDecided since there is no
        /// search elapsed-time to report.
        /// </summary>
        private void HandleBookMovePlayed(MoveCommand move)
        {
            if (_searchStopwatch.IsRunning) _searchStopwatch.Stop();
            Activity = AgentActivity.ResultReady;

            if (_logger != null && _logger.IsVerbose)
            {
                _logger.LogInfo(new DomainLogEvent(DomainEventCode.AI_BookMovePlayed, message: $"{_aiTeam} plays {move}"));
            }

            if (RecordTelemetry)
            {
                HoldForPlyNumber(new AiMoveRecord(PlyNumberPendingUntilItLands, _aiTeam, move,
                    AiMoveSource.Book, elapsedMs: 0, completedDepth: 0, stopReason: SearchStopReason.Unset));
            }

            _playMove(move);

            Activity = AgentActivity.Idle;
        }

        // What a search cost is known here; which ply it becomes is not. The move goes to a queue
        // that paces it against whatever animation is still playing, so it reaches the board either
        // immediately or a fraction of a second later, and the board's own count says nothing about
        // which until it lands. Reading the count here got the number right only when the queue
        // happened to be idle: in one real match the two fastest replies -- an instant book move and
        // a mate found in 36ms, both delivered while the opponent's move was still animating -- were
        // each recorded a ply short, while the four that followed three-second searches were right.
        private AiMoveRecord? _plyAwaitingItsNumber;

        private const int PlyNumberPendingUntilItLands = 0;

        private void HoldForPlyNumber(AiMoveRecord record) => _plyAwaitingItsNumber = record;

        /// <summary>
        /// Completes the held record once its move reaches the board. Fired for every ply, including
        /// the opponent's, so it matches on the move itself rather than assuming the next one to
        /// land is the AI's -- an opponent move already queued ahead of the AI's reply lands first
        /// and would otherwise hand its ply number to the wrong record.
        /// </summary>
        public void NotePlyApplied(MoveCommand move, int plyNumber)
        {
            if (_plyAwaitingItsNumber == null || Telemetry == null) return;

            AiMoveRecord held = _plyAwaitingItsNumber.Value;
            if (held.Move.StartPosition != move.StartPosition
                || held.Move.EndPosition != move.EndPosition
                || held.Move.Stage != move.Stage) return;

            Telemetry.RecordMove(held.WithPlyNumber(plyNumber));
            _plyAwaitingItsNumber = null;
        }

        /// <summary>
        /// Drops any already-recorded ply that a takeback has just taken off the board. Pass the
        /// board's ply count as it stands AFTER the rewind.
        ///
        /// CancelInFlightSearch above covers the move that hasn't landed yet; this covers the ones
        /// that had. Without it the report goes on describing moves nobody ended up playing: one
        /// match took two turns back and its report still listed them, so its ply numbers ran 47,
        /// 49, 50 and then 47 again, and it summarised two Defections in a game whose Betrayal
        /// right can only be spent once.
        /// </summary>
        public void NotePliesUnmade(int lastSurvivingPlyNumber) =>
            Telemetry?.RemoveAfterPly(lastSurvivingPlyNumber);

        /// <summary>
        /// Records a Betrayer changing sides. Nothing here decides that move — the rules produce it
        /// when Retribution is refused or impossible — so it reaches neither of the hand-offs above
        /// and would go unrecorded. It matters because it is the one ply in a match that moves a
        /// piece between the two armies: a log without it shows the AI holding a piece nothing
        /// accounts for, and one real match had a Black queen appear on a1 out of nowhere for
        /// exactly this reason. Recorded whichever side's Betrayer it was, since either changes
        /// what the AI is playing with. No elapsed time or depth, because no search produced it.
        ///
        /// The ply number comes from the driver raising this, the same source every other recorded
        /// ply uses. Reading the board's own count here instead would be a second way of answering
        /// a question the driver has already answered, and the two only agree by coincidence of
        /// when this happens to be called.
        /// </summary>
        public void RecordDefection(MoveCommand move, int plyNumber)
        {
            if (!RecordTelemetry || Telemetry == null) return;

            // The move carries the Betrayer as it was before it turned, so the army it landed in is
            // the other one.
            Team gainedBy = move.PieceTeam == Team.White ? Team.Black : Team.White;
            Telemetry.RecordMove(AiMoveRecord.ForDefection(plyNumber, gainedBy, move));
        }

        /// <summary>
        /// Logs the point the AI ran out of opening theory and started working moves out for
        /// itself. Purely a diagnostic marker — the move that follows is delivered through
        /// HandleMoveDecided like any other searched move, so nothing about play changes here.
        /// </summary>
        private void HandleLeftOpeningBook(MoveCommand move)
        {
            if (_logger != null && _logger.IsVerbose)
            {
                _logger.LogInfo(new DomainLogEvent(DomainEventCode.AI_LeftOpeningBook,
                    message: $"{_aiTeam} is out of book"));
            }
        }

        /// <summary>
        /// Configures the session for a match with no AI team at all — a plain match today, and a
        /// future multiplayer one. Tears down any agent left over from a previous AI match and
        /// clears its telemetry, so neither can leak into a match the AI was never part of: without
        /// this, a plain match played right after an AI one would still carry the old match's
        /// recorded moves, and a caller reading Telemetry would have no way to tell they belong to
        /// a different game. Idempotent — safe to call even when no AI match has run yet this
        /// session, which is exactly the state a fresh coordinator is already in.
        /// </summary>
        public void ClearAIMode()
        {
            TearDownAgent();
            _plyAwaitingItsNumber = null;
            Telemetry = null;
        }

        private void TearDownAgent()
        {
            if (_aiAgent is AsyncAIAgent asyncAgent)
            {
                asyncAgent.OnMoveDecided -= HandleMoveDecided;
                asyncAgent.OnBookMovePlayed -= HandleBookMovePlayed;
                asyncAgent.OnLeftOpeningBook -= HandleLeftOpeningBook;
                asyncAgent.Dispose();
            }
            _aiAgent = null;
            Activity = AgentActivity.Idle;
        }

        public void Dispose() => TearDownAgent();
    }
}
