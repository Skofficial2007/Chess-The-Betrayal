using System;
using System.Threading;
using System.Threading.Tasks;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.AI
{
    /// <summary>
    /// IAIAgent implementation that runs Alpha-Beta on a BACKGROUND THREAD, then marshals the
    /// chosen move back to the main thread. Two hard rules make this safe:
    ///
    ///   1. BOARD ISOLATION. The search NEVER touches the live main-thread board. We clone it once
    ///      (CloneForSnapshot) at the instant the search is requested, and the worker mutates only
    ///      that clone. This is mandatory, not optional: Core's move generation mutates the board
    ///      during the Betrayal "disguise trick", so a shared board would data-race the main thread.
    ///
    ///   2. MAIN-THREAD MARSHALLING. OnMoveDecided must fire on the main thread (callers touch
    ///      Unity objects). The worker doesn't invoke it directly; it enqueues the result and a
    ///      main-thread pump (Tick, called from a MonoBehaviour Update in the Gameplay layer)
    ///      raises the event. This class stays Unity-free so it lives in the AI assembly.
    ///
    /// Runs on both desktop and mobile targets — background-thread search is the chosen model for
    /// this project across platforms (no main-thread-only fallback), so a depth-6-8 search never
    /// hitches the frame on either.
    /// </summary>
    public sealed class AsyncAIAgent : IAIAgent
    {
        public event Action<MoveCommand> OnMoveDecided;

        private readonly AlphaBetaSearch _search;
        private readonly AISearchSettings _settings;
        private readonly TranspositionTable _tt;

        private volatile bool _hasResult;
        private MoveCommand _pendingResult;
        private CancellationTokenSource _cts;

        /// <summary>
        /// True from the moment RequestBestMove kicks off a search until either a result is
        /// consumed via Tick() or the search is cancelled. UndoService reads this to decide
        /// whether an Undo needs to cancel an in-flight AI reply before popping the board.
        /// </summary>
        public bool IsSearching => _cts != null;

        /// <summary>
        /// Cancels any in-flight search without disposing the agent — distinct from Dispose(),
        /// which the caller only calls when tearing down the whole match/agent. UndoService calls
        /// this (never Dispose) so the agent stays usable for the player's very next move.
        /// </summary>
        public void CancelSearch() => CancelInFlight();

        // TEMP DEBUG : surfaces a worker-thread exception to the
        // main-thread caller instead of letting it vanish into the Task's unobserved-exception
        // path. Remove once the AI flow has been confirmed working end-to-end in-editor.
        private volatile string _lastSearchException;
        public string ConsumeLastSearchException()
        {
            string ex = _lastSearchException;
            _lastSearchException = null;
            return ex;
        }

        public AsyncAIAgent(IChessEngine engine, IPositionEvaluator evaluator, AISearchSettings settings)
        {
            // Owned here (not by AlphaBetaSearch) so it PERSISTS across FindBestMove calls within a
            // match — this is what attacks the successive-turn escalation the TT is built for.
            // A fresh AsyncAIAgent (one per match, via AIMatchCoordinator.SetAIMode) gets a fresh
            // table for free; there is no need to clear it mid-match.
            _tt = new TranspositionTable(log2Size: 20); // ~16 MB desktop; mobile sizing TBD via settings
            _search = new AlphaBetaSearch(engine, evaluator, transpositionTable: _tt);
            _settings = settings;
        }

        /// <summary>
        /// Kicks off a background search. Clones the board on the CALLING (main) thread — cloning
        /// reads the live board, so it must happen before we hand off, while nothing else mutates it.
        /// </summary>
        public void RequestBestMove(BoardState board, Team team, CancellationToken cancellation = default)
        {
            CancelInFlight();

            // Clone on the main thread — snapshot is now owned exclusively by the worker.
            BoardState isolated = board.CloneForSnapshot();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            CancellationToken token = _cts.Token;

            Task.Run(() =>
            {
                try
                {
                    MoveCommand best = _search.FindBestMove(isolated, _settings, token);
                    if (!token.IsCancellationRequested)
                    {
                        _pendingResult = best;
                        _hasResult = true; // volatile write publishes _pendingResult to the main thread
                    }
                }
                catch (OperationCanceledException) { /* expected on reset/scene change */ }
                catch (Exception ex)
                {
                    _lastSearchException = ex.ToString();
                }
            }, token);
        }

        /// <summary>
        /// Pump this from a MonoBehaviour Update() on the main thread. When the worker has a result,
        /// it raises OnMoveDecided here — on the main thread — so the listener can safely feed the
        /// move into MatchDriver.PlayMove and drive Unity animations.
        /// </summary>
        public void Tick()
        {
            if (!_hasResult) return;
            _hasResult = false;

            // IsSearching must go false once a result is consumed, per its own contract above —
            // otherwise a completed-and-delivered search reads as still in-flight forever (until
            // the next RequestBestMove/CancelSearch/Dispose), which would make UndoService pop
            // only 1 turn instead of 2 on every Undo pressed after the AI's first reply.
            _cts?.Dispose();
            _cts = null;

            OnMoveDecided?.Invoke(_pendingResult);
        }

        private void CancelInFlight()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            _hasResult = false;
        }

        public void Dispose() => CancelInFlight();
    }
}
