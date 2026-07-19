using System;
using System.Threading;
using System.Threading.Tasks;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;

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

        /// <summary>Fires alongside OnMoveDecided specifically when the move came from the opening
        /// book instead of a search — AIMatchCoordinator uses this to emit AI_BookMovePlayed instead
        /// of the usual AI_MoveDecided/elapsed-ms telemetry, since a book hit has no search time.</summary>
        public event Action<MoveCommand> OnBookMovePlayed;

        private readonly IChessEngine _engine;
        private readonly AlphaBetaSearch _search;
        private readonly AISearchSettings _settings;
        private readonly TranspositionTable _tt;
        private readonly AIProfile _profile;
        private readonly IRandomSource _rng;
        private readonly OpeningBookAsset _openingBook;
        private readonly MoveSelectionPolicy _selectionPolicy = new MoveSelectionPolicy();

        private volatile bool _hasResult;
        private volatile bool _pendingResultFromBook;
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

        /// <summary>Convenience constructor with no AI personality — the search's own best move is
        /// always returned, with zero RNG calls. Used by callers that don't model AIProfile at all
        /// (tests exercising raw search behavior). Production always goes through the full
        /// constructor below via AIMatchCoordinator.</summary>
        public AsyncAIAgent(IChessEngine engine, IPositionEvaluator evaluator, AISearchSettings settings)
            : this(engine, evaluator, settings, AIProfile.None, null, null)
        {
        }

        /// <summary>
        /// profile/rng feed MoveSelectionPolicy's blunder-roll/tie-break/Betrayal-aggression
        /// selection, applied to the search's own ranked root-move output once FindBestMove
        /// completes (see RequestBestMove's worker-thread epilogue) — never inventing a move,
        /// only choosing among the search's own legal, ranked candidates. rng == null skips
        /// selection entirely and returns the search's exact best move, matching the convenience
        /// constructor above byte-for-byte.
        /// </summary>
        public AsyncAIAgent(IChessEngine engine, IPositionEvaluator evaluator, AISearchSettings settings,
            AIProfile profile, IRandomSource rng)
            : this(engine, evaluator, settings, profile, rng, null)
        {
        }

        /// <summary>
        /// openingBook is optional (null skips book lookup entirely) and only ever consulted when
        /// profile.UseOpeningBook is true — a profile that opts out of the book plays search moves
        /// from move one, matching the pre-AI-28 behavior for that profile.
        /// </summary>
        public AsyncAIAgent(IChessEngine engine, IPositionEvaluator evaluator, AISearchSettings settings,
            AIProfile profile, IRandomSource rng, OpeningBookAsset openingBook)
        {
            // Owned here (not by AlphaBetaSearch) so it PERSISTS across FindBestMove calls within a
            // match — this is what attacks the successive-turn escalation the TT is built for.
            // A fresh AsyncAIAgent (one per match, via AIMatchCoordinator.SetAIMode) gets a fresh
            // table for free; there is no need to clear it mid-match.
            _tt = new TranspositionTable(log2Size: 20); // ~16 MB desktop; mobile sizing TBD via settings
            _engine = engine;
            _search = new AlphaBetaSearch(engine, evaluator, transpositionTable: _tt);
            _settings = settings;
            _profile = profile;
            _rng = rng;
            _openingBook = openingBook;
        }

        /// <summary>
        /// Kicks off a background search. Clones the board on the CALLING (main) thread — cloning
        /// reads the live board, so it must happen before we hand off, while nothing else mutates it.
        /// </summary>
        public void RequestBestMove(BoardState board, Team team, CancellationToken cancellation = default)
        {
            CancelInFlight();

            // Book lookup runs synchronously, on the calling (main) thread, against the LIVE board —
            // it's a binary search plus a legal-move regeneration, not a tree search, so it's cheap
            // enough to never need a worker thread or a clone. A hit skips FindBestMove entirely.
            if (_profile.UseOpeningBook && _openingBook != null)
            {
                MoveCommand? bookMove = OpeningBook.OpeningBookLookup.TryGetBookMove(_openingBook, board, _engine, _rng);
                if (bookMove != null)
                {
                    _pendingResult = bookMove.Value;
                    _pendingResultFromBook = true;
                    _hasResult = true;
                    return;
                }
            }

            // Clone on the main thread — snapshot is now owned exclusively by the worker.
            BoardState isolated = board.CloneForSnapshot();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            CancellationTokenSource ctsForThisSearch = _cts; // captured so a later RequestBestMove/CancelSearch swapping _cts can't affect this closure
            CancellationToken token = _cts.Token;

            // The wall-clock backstop of iterative deepening: FindBestMove's depth loop already
            // keeps the best move from the last FULLY COMPLETED depth and discards a cancelled
            // partial depth (see its doc comment). Arming this timer at the HARD budget rather
            // than the soft one gives the search room to spend the gap between them on a position
            // that looks unsettled (see FindBestMove's own soft-budget handling) while this timer
            // still guarantees the search can never run past the hard ceiling no matter what it
            // decides internally. Never throws/blocks — it just schedules Cancel() on the timer thread.
            _cts.CancelAfter(_settings.TimeBudget.HardMs);

            Task.Run(() =>
            {
                try
                {
                    // rescoreMargin > 0 only for a personality-driven profile (BlunderMarginCp or
                    // TieBreakWindowCp nonzero) — un-biases the alpha-beta-tightened scores of the
                    // handful of candidates SelectFinalMove might actually pick. Zero for the
                    // convenience-ctor/zero-dial (Impossible tier) path, matching pre-AI-24 cost.
                    int rescoreMargin = Math.Max(_profile.BlunderMarginCp, _profile.TieBreakWindowCp);
                    MoveCommand best = _search.FindBestMove(isolated, _settings, token, rescoreMargin);

                    // MoveSelectionPolicy applies AIProfile's personality dials to the search's own
                    // ranked root-move output — still on THIS worker thread, before the result ever
                    // crosses to the main thread. rng == null (convenience ctor) or a fully zero-dial
                    // profile both skip this entirely, returning FindBestMove's own best move
                    // unchanged — byte-identical to pre-AI-24 behavior.
                    if (_rng != null && (_profile.BlunderRate > 0f || _profile.TieBreakWindowCp > 0))
                    {
                        best = _selectionPolicy.SelectFinalMove(
                            _search.RootMoves, _search.RootScores, _search.RootMoveCount, _search.BestRootIndex,
                            _profile, _rng);
                    }

                    // A budget-expiry cancellation is the EXPECTED, successful outcome of iterative
                    // deepening — FindBestMove already returns the best move from the last fully
                    // completed depth in that case, so it must still be delivered. token.
                    // IsCancellationRequested being true does NOT mean the search was aborted: it is
                    // ALSO true when our own CancelAfter(HardMs) timer simply expired, which
                    // is the common, correct outcome in a real match. Two genuinely different "this
                    // search no longer matters" conditions must still discard the result:
                    //   1. The caller's own `cancellation` token fired (game reset / scene change /
                    //      whatever owns it) — checked directly, independent of our internal timer.
                    //   2. This search was superseded — CancelInFlight() (via a new RequestBestMove or
                    //      CancelSearch/Dispose) replaced _cts with a new instance (or null) before
                    //      this callback ran, so an identity check against the captured instance is
                    //      the correct "am I still the current search" signal.
                    if (cancellation.IsCancellationRequested) return;
                    if (!ReferenceEquals(_cts, ctsForThisSearch)) return;

                    _pendingResult = best;
                    _hasResult = true; // volatile write publishes _pendingResult to the main thread
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

            bool fromBook = _pendingResultFromBook;
            _pendingResultFromBook = false;

            if (fromBook)
                OnBookMovePlayed?.Invoke(_pendingResult);
            else
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
