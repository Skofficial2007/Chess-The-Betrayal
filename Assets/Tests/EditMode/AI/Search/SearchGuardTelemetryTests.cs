using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.AI.Search
{
    /// <summary>
    /// Two guards inside the search that decide nothing on their own and so leave no trace in the
    /// move a search returns. Both were provable only by the endgame fixtures that play whole
    /// positions out over dozens of real plies, which are far too slow to run on a commit — so in
    /// practice either could be removed and every fast test would still pass. The search already
    /// counts both as it works; these read the counters.
    ///
    /// Each is asserted from both sides. A counter that must be zero proves nothing unless
    /// something nearby proves it can be nonzero, or the assertion passes for any reason at all,
    /// including the machinery never running.
    /// </summary>
    [TestFixture]
    public class SearchGuardTelemetryTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup() => _engine = new ChessEngineAdapter();

        private AlphaBetaSearch NewSearch() =>
            new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 20));

        private static AISearchSettings FixedDepth(int depth) =>
            new AISearchSettings(depth, new AITimeBudget(60_000, 60_000), BetrayalUsage.Full);

        [Test]
        public void ABareKingEndgameIsSearchedWithoutReducingLateMoves()
        {
            // Reducing late moves is a bet that a move the ordering ranked low is unlikely to be
            // best, which holds while there are captures and threats to rank against each other.
            // Forcing a lone king to mate has none of that: the technique is quiet king and rook
            // moves that look alike to any ordering heuristic, and the one that matters is often
            // not tried first. Reduce here and the search confines the king and then shuffles.
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var search = NewSearch();
            search.FindBestMove(board, FixedDepth(6), CancellationToken.None);

            Assert.That(search.Stats.LmrReductions, Is.Zero,
                $"Late move reduction fired {search.Stats.LmrReductions} times on a three-piece " +
                "board. The exemption that keeps it off in a bare-king finish is gone.");

            // Without this the assertion above is satisfied by a search that never got deep enough
            // to reduce anything, which is a different result wearing the same colour.
            Assert.That(search.Stats.LastCompletedDepth, Is.GreaterThanOrEqualTo(3),
                "Only reached depth " + search.Stats.LastCompletedDepth + ", which is shallower " +
                "than reduction is ever attempted at, so nothing above was actually tested.");
        }

        [Test]
        public void APopulatedMidgameIsSearchedWithLateMoveReductionOn()
        {
            // The control for the test above. Same search, same depth, a board with enough pieces
            // to be past the exemption: if reduction does not fire here, the zero above is telling
            // us about the machinery rather than about the guard.
            BoardState board = SearchProfilePositions.QuietMidgame();

            var search = NewSearch();
            search.FindBestMove(board, FixedDepth(6), CancellationToken.None);

            Assert.That(search.Stats.LmrReductions, Is.GreaterThan(0),
                "Late move reduction never fired on a full midgame board, so the zero the bare-king " +
                "test asserts would have been reached whether the exemption existed or not.");
        }

        [Test]
        public void ACaptureThatLosesMaterialOnRecaptureIsSkippedInQuiescence()
        {
            // A rook takes a defended pawn. It looks like material until every recapture on that
            // square is counted, at which point it is a rook for a pawn. Delta pruning cannot see
            // this — the capture passes it, because winning a pawn outright would be worth having.
            // Static exchange evaluation is the only thing in the search that answers it, and it is
            // consulted through one guard, so a search that stops consulting it stops pruning here.
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("c6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e6", Team.Black, ChessPieceType.Pawn)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var search = NewSearch();
            search.FindBestMove(board, FixedDepth(5), CancellationToken.None);

            Assert.That(search.Stats.SeeQuiescencePrunes, Is.GreaterThan(0),
                "Quiescence skipped no losing capture on a board built around one, so nothing in " +
                "the search is consulting static exchange evaluation any more.");
        }
    }
}
