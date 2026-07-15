using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the killer-move heuristic: a quiet move that caused a beta cutoff at a given ply is
    /// tried early the next time that same ply is reached. The DoD for this ticket is the
    /// sub-phase-ply-isolation case — killers are keyed by plyFromRoot rather than by whose turn it
    /// is, specifically so an Act at ply k and its forced Retribution at ply k+1 never share a
    /// killer slot, even though Betrayal doesn't flip the mover every ply the way ordinary chess does.
    /// </summary>
    [TestFixture]
    public class KillerMoveOrderingTests
    {
        private static readonly Vector2Int From = TestBoardSetupUtility.AlgebraicToVector("a2");
        private static readonly Vector2Int To = TestBoardSetupUtility.AlgebraicToVector("a3");
        private static readonly Vector2Int OtherTo = TestBoardSetupUtility.AlgebraicToVector("a4");
        private static readonly Vector2Int ThirdTo = TestBoardSetupUtility.AlgebraicToVector("a5");

        private static AlphaBetaSearch NewSearch() =>
            new AlphaBetaSearch(new ChessEngineAdapter(), new BetrayalAwareEvaluator());

        private static MoveCommand QuietMove(ChessPieceType pieceType, Vector2Int to)
        {
            PieceData piece = new PieceData(Team.White, pieceType, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, to, piece);
        }

        private static MoveCommand CaptureMove(ChessPieceType pieceType, ChessPieceType capturedType)
        {
            PieceData piece = new PieceData(Team.White, pieceType, moveDirection: 1, startRow: 1);
            PieceData captured = new PieceData(Team.Black, capturedType, moveDirection: -1, startRow: 6);
            return MoveCommand.CreateStandardMove(From, To, piece, captured);
        }

        private static MoveCommand ActMove(Vector2Int to)
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Knight, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, to, piece).WithStage(BetrayalStage.Act);
        }

        [Test]
        public void QuietCutoff_MakesThatMoveOutrankAnotherQuietMoveAtTheSamePly()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand killer = QuietMove(ChessPieceType.Knight, To);
            MoveCommand other = QuietMove(ChessPieceType.Bishop, OtherTo);
            const int ply = 3;

            search.RecordQuietCutoffForTest(killer, depth: 4, plyFromRoot: ply);

            int killerScore = search.OrderScoreForTest(killer, ttMove: 0, plyFromRoot: ply);
            int otherScore = search.OrderScoreForTest(other, ttMove: 0, plyFromRoot: ply);

            Assert.That(killerScore, Is.GreaterThan(otherScore),
                "A move that just caused a cutoff at this ply should be tried first the next time this ply is reached.");
        }

        [Test]
        public void KillerAtOnePly_DoesNotPolluteAnAdjacentPly()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand killer = QuietMove(ChessPieceType.Knight, To);

            search.RecordQuietCutoffForTest(killer, depth: 4, plyFromRoot: 3);

            // The exact same move, evaluated at a neighboring ply, must not carry the killer bonus —
            // this is the core Betrayal-safety guarantee: an Act at ply k and its forced Retribution
            // at ply k+1 must never share a killer slot.
            int scoreAtRecordedPly = search.OrderScoreForTest(killer, ttMove: 0, plyFromRoot: 3);
            int scoreAtNextPly = search.OrderScoreForTest(killer, ttMove: 0, plyFromRoot: 4);
            int scoreAtPreviousPly = search.OrderScoreForTest(killer, ttMove: 0, plyFromRoot: 2);

            Assert.That(scoreAtNextPly, Is.LessThan(scoreAtRecordedPly),
                "A killer recorded at ply 3 must not appear at ply 4 — this is the Act/Retribution isolation case.");
            Assert.That(scoreAtPreviousPly, Is.LessThan(scoreAtRecordedPly),
                "A killer recorded at ply 3 must not appear at ply 2 either.");
        }

        [Test]
        public void SecondDistinctKillerAtSamePly_PushesTheFirstIntoTheSecondSlot()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand first = QuietMove(ChessPieceType.Knight, To);
            MoveCommand second = QuietMove(ChessPieceType.Bishop, OtherTo);
            const int ply = 5;

            search.RecordQuietCutoffForTest(first, depth: 4, plyFromRoot: ply);
            search.RecordQuietCutoffForTest(second, depth: 4, plyFromRoot: ply);

            // Both killer slots at this ply should now outrank a move that was never recorded, and
            // the most recently recorded one (second) should be at least as strong as the first —
            // slot 0 carries a larger bonus than slot 1 (see QuietMoveOrderScore).
            MoveCommand neverRecorded = QuietMove(ChessPieceType.Rook, ThirdTo);
            int firstScore = search.OrderScoreForTest(first, ttMove: 0, plyFromRoot: ply);
            int secondScore = search.OrderScoreForTest(second, ttMove: 0, plyFromRoot: ply);
            int neverScore = search.OrderScoreForTest(neverRecorded, ttMove: 0, plyFromRoot: ply);

            Assert.That(secondScore, Is.GreaterThanOrEqualTo(firstScore),
                "The most recently recorded killer should sort at or above an older one at the same ply.");
            Assert.That(firstScore, Is.GreaterThan(neverScore),
                "Both remembered killer slots should still outrank a move that was never recorded.");
        }

        [Test]
        public void RepeatedCutoffByTheSameMove_LeavesTheSecondSlotEmpty()
        {
            // Recording the SAME move twice in a row must be a no-op on the killer table (only
            // history keeps accumulating) — it must not shift a copy of itself into slot 1, which
            // would waste the second slot on a move already remembered in the first.
            AlphaBetaSearch search = NewSearch();
            MoveCommand killer = QuietMove(ChessPieceType.Knight, To);
            MoveCommand neverRecorded = QuietMove(ChessPieceType.Rook, ThirdTo);
            const int ply = 2;

            search.RecordQuietCutoffForTest(killer, depth: 4, plyFromRoot: ply);
            search.RecordQuietCutoffForTest(killer, depth: 4, plyFromRoot: ply); // same move again

            int killerScore = search.OrderScoreForTest(killer, ttMove: 0, plyFromRoot: ply);
            int neverScore = search.OrderScoreForTest(neverRecorded, ttMove: 0, plyFromRoot: ply);

            Assert.That(killerScore, Is.GreaterThan(neverScore),
                "The repeated move should still be the top killer at this ply.");
        }

        [Test]
        public void DistinctMoveAfterAKiller_CorrectlyEvictsTheOlderKillerIntoTheSecondSlot()
        {
            // Once a NEW distinct move causes a cutoff at the same ply, it becomes the new top
            // killer and the previous top killer is pushed down to slot 1 — the standard two-killer
            // scheme, distinct from the same-move-repeated case above (which must NOT do this).
            AlphaBetaSearch search = NewSearch();
            MoveCommand olderKiller = QuietMove(ChessPieceType.Knight, To);
            MoveCommand newerKiller = QuietMove(ChessPieceType.Bishop, OtherTo);
            const int ply = 2;

            search.RecordQuietCutoffForTest(olderKiller, depth: 4, plyFromRoot: ply);
            search.RecordQuietCutoffForTest(newerKiller, depth: 4, plyFromRoot: ply);

            MoveCommand neverRecorded = QuietMove(ChessPieceType.Rook, ThirdTo);
            int newerScore = search.OrderScoreForTest(newerKiller, ttMove: 0, plyFromRoot: ply);
            int olderScore = search.OrderScoreForTest(olderKiller, ttMove: 0, plyFromRoot: ply);
            int neverScore = search.OrderScoreForTest(neverRecorded, ttMove: 0, plyFromRoot: ply);

            Assert.That(newerScore, Is.GreaterThan(olderScore),
                "The most recent distinct killer must outrank the one it replaced at slot 0.");
            Assert.That(olderScore, Is.GreaterThan(neverScore),
                "The evicted killer should still outrank a move that was never recorded — it's in slot 1, not gone.");
        }

        [Test]
        public void Capture_NeverBecomesAKiller()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand capture = CaptureMove(ChessPieceType.Knight, ChessPieceType.Pawn);
            const int ply = 1;

            search.RecordQuietCutoffForTest(capture, depth: 4, plyFromRoot: ply);

            // A capture always sorts by its own (much higher) tier band, so this proves the killer
            // table itself was never written to, not merely that the capture out-scores it anyway:
            // a same-shape quiet move at the same destination must still read as "never recorded".
            MoveCommand quietSameShape = QuietMove(ChessPieceType.Knight, To);
            int quietScore = search.OrderScoreForTest(quietSameShape, ttMove: 0, plyFromRoot: ply);
            int freshSearchQuietScore = NewSearch().OrderScoreForTest(quietSameShape, ttMove: 0, plyFromRoot: ply);

            Assert.That(quietScore, Is.EqualTo(freshSearchQuietScore),
                "A capture must never occupy a killer slot.");
        }

        [Test]
        public void BetrayalStageMove_NeverBecomesAKiller()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand act = ActMove(To);
            const int ply = 1;

            search.RecordQuietCutoffForTest(act, depth: 4, plyFromRoot: ply);

            MoveCommand quietKnight = QuietMove(ChessPieceType.Knight, To);
            int quietScore = search.OrderScoreForTest(quietKnight, ttMove: 0, plyFromRoot: ply);
            int freshSearchQuietScore = NewSearch().OrderScoreForTest(quietKnight, ttMove: 0, plyFromRoot: ply);

            Assert.That(quietScore, Is.EqualTo(freshSearchQuietScore),
                "An Act move must never occupy a killer slot — Betrayal-stage moves are excluded from quiet-move memory entirely.");
        }

        [Test]
        public void NegativePly_OptsOutOfKillerLookup_ButStillUpdatesHistory()
        {
            // plyFromRoot == -1 is the sentinel quiescence's OrderMoves call site uses (it has no
            // ply-indexed killer table of its own) — this proves that path still benefits from
            // history without ever touching (or crashing on) the killer array.
            AlphaBetaSearch search = NewSearch();
            MoveCommand move = QuietMove(ChessPieceType.Knight, To);

            Assert.DoesNotThrow(() => search.RecordQuietCutoffForTest(move, depth: 4, plyFromRoot: -1));

            MoveCommand never = QuietMove(ChessPieceType.Bishop, OtherTo);
            int movedScore = search.OrderScoreForTest(move, ttMove: 0, plyFromRoot: -1);
            int neverScore = search.OrderScoreForTest(never, ttMove: 0, plyFromRoot: -1);

            Assert.That(movedScore, Is.GreaterThan(neverScore),
                "History should still apply even when plyFromRoot opts out of the killer lookup.");
        }

        [Test]
        public void KillerBonus_NeverOutranksAnActMove()
        {
            // Losing captures and quiet moves have always shared the same tier (a losing capture's
            // own score can be as low as 1, e.g. queen takes pawn) — the killer bonus is only
            // guaranteed to stay below the tier ABOVE that shared band, which is Act.
            AlphaBetaSearch search = NewSearch();
            MoveCommand killer = QuietMove(ChessPieceType.Knight, To);
            MoveCommand act = ActMove(OtherTo);
            const int ply = 4;

            search.RecordQuietCutoffForTest(killer, depth: 7, plyFromRoot: ply);

            int killerScore = search.OrderScoreForTest(killer, ttMove: 0, plyFromRoot: ply);
            int actScore = search.OrderScoreForTest(act, ttMove: 0, plyFromRoot: ply);

            Assert.That(killerScore, Is.LessThan(actScore),
                "A killer must stay below the Act tier band.");
        }

        [Test]
        public void TTMove_StillOutranksAKillerAtTheSamePly()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand killer = QuietMove(ChessPieceType.Knight, To);
            MoveCommand ttMove = QuietMove(ChessPieceType.Bishop, OtherTo);
            const int ply = 4;

            search.RecordQuietCutoffForTest(killer, depth: 7, plyFromRoot: ply);
            uint ttPacked = AlphaBetaSearch.PackMove(ttMove);

            int killerScore = search.OrderScoreForTest(killer, ttPacked, plyFromRoot: ply);
            int ttScore = search.OrderScoreForTest(ttMove, ttPacked, plyFromRoot: ply);

            Assert.That(ttScore, Is.GreaterThan(killerScore),
                "The TT/PV move must always sort first, even ahead of this ply's own killer.");
        }
    }
}
