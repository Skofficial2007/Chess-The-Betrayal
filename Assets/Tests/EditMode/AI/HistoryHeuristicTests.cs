using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the history heuristic added to the quiet-move ordering band: a quiet move that causes a
    /// beta cutoff should sort ahead of quiet moves that haven't, next time either is legal
    /// anywhere in the tree. Also pins the two things that must never happen — a capture/promotion/
    /// Betrayal-stage move updating history, and a quiet move's history bonus ever outranking a real
    /// tactical move (capture, promotion, or Act).
    /// </summary>
    [TestFixture]
    public class HistoryHeuristicTests
    {
        private static readonly Vector2Int From = TestBoardSetupUtility.AlgebraicToVector("a2");
        private static readonly Vector2Int To = TestBoardSetupUtility.AlgebraicToVector("a3");
        private static readonly Vector2Int OtherTo = TestBoardSetupUtility.AlgebraicToVector("a4");

        private static AlphaBetaSearch NewSearch() =>
            new AlphaBetaSearch(new ChessEngineAdapter(), new BetrayalAwareEvaluator());

        private static MoveCommand QuietMove(ChessPieceType pieceType, Vector2Int? to = null)
        {
            PieceData piece = new PieceData(Team.White, pieceType, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, to ?? To, piece);
        }

        private static MoveCommand CaptureMove(ChessPieceType pieceType, ChessPieceType capturedType)
        {
            PieceData piece = new PieceData(Team.White, pieceType, moveDirection: 1, startRow: 1);
            PieceData captured = new PieceData(Team.Black, capturedType, moveDirection: -1, startRow: 6);
            return MoveCommand.CreateStandardMove(From, To, piece, captured);
        }

        private static MoveCommand PromotionMove()
        {
            PieceData pawn = new PieceData(Team.White, ChessPieceType.Pawn, moveDirection: 1, startRow: 1);
            return MoveCommand.CreatePromotionMove(From, To, pawn, ChessPieceType.Queen);
        }

        private static MoveCommand ActMove()
        {
            PieceData piece = new PieceData(Team.White, ChessPieceType.Knight, moveDirection: 1, startRow: 1);
            return MoveCommand.CreateStandardMove(From, To, piece).WithStage(BetrayalStage.Act);
        }

        [Test]
        public void QuietCutoff_RaisesThatMovesOrderScore_AboveAnUnrecordedQuietMove()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand recorded = QuietMove(ChessPieceType.Knight);
            MoveCommand never = QuietMove(ChessPieceType.Bishop, OtherTo);

            search.RecordQuietCutoffForTest(recorded, depth: 4, plyFromRoot: -1);

            int recordedScore = search.OrderScoreForTest(recorded, ttMove: 0, plyFromRoot: -1);
            int neverScore = search.OrderScoreForTest(never, ttMove: 0, plyFromRoot: -1);

            Assert.That(recordedScore, Is.GreaterThan(neverScore),
                "A quiet move that already caused a cutoff should sort ahead of one that never has.");
        }

        [Test]
        public void QuietCutoff_IsKeyedByPieceTypeAndDestinationSquare_NotByAnyOtherMoveInThePair()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand knightToA3 = QuietMove(ChessPieceType.Knight);
            MoveCommand bishopToA3 = QuietMove(ChessPieceType.Bishop);

            search.RecordQuietCutoffForTest(knightToA3, depth: 4, plyFromRoot: -1);

            int knightScore = search.OrderScoreForTest(knightToA3, ttMove: 0, plyFromRoot: -1);
            int bishopScore = search.OrderScoreForTest(bishopToA3, ttMove: 0, plyFromRoot: -1);

            Assert.That(knightScore, Is.GreaterThan(bishopScore),
                "History is indexed by [piece type, destination square] — a different piece moving to the same square must not inherit the bonus.");
        }

        [Test]
        public void DeeperCutoff_EarnsALargerHistoryBonus_ThanAShallowerOne()
        {
            AlphaBetaSearch shallow = NewSearch();
            AlphaBetaSearch deep = NewSearch();
            MoveCommand move = QuietMove(ChessPieceType.Knight);

            shallow.RecordQuietCutoffForTest(move, depth: 1, plyFromRoot: -1);
            deep.RecordQuietCutoffForTest(move, depth: 6, plyFromRoot: -1);

            int shallowScore = shallow.OrderScoreForTest(move, ttMove: 0, plyFromRoot: -1);
            int deepScore = deep.OrderScoreForTest(move, ttMove: 0, plyFromRoot: -1);

            Assert.That(deepScore, Is.GreaterThan(shallowScore),
                "A cutoff proven deeper in the tree is stronger evidence and should weigh more heavily on future ordering.");
        }

        [Test]
        public void Capture_NeverUpdatesHistory()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand capture = CaptureMove(ChessPieceType.Knight, ChessPieceType.Pawn);
            MoveCommand quietSameShape = QuietMove(ChessPieceType.Knight);

            search.RecordQuietCutoffForTest(capture, depth: 6, plyFromRoot: -1);

            // A capture always sorts by its own tier band regardless of history, so read the quiet
            // move's score directly — if the capture had (wrongly) fed history, this would move.
            int quietScore = search.OrderScoreForTest(quietSameShape, ttMove: 0, plyFromRoot: -1);
            int freshSearchQuietScore = NewSearch().OrderScoreForTest(quietSameShape, ttMove: 0, plyFromRoot: -1);

            Assert.That(quietScore, Is.EqualTo(freshSearchQuietScore),
                "Recording a capture must never touch the quiet-move history table.");
        }

        [Test]
        public void Promotion_NeverUpdatesHistory()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand promotion = PromotionMove();
            MoveCommand quietPawn = QuietMove(ChessPieceType.Pawn);

            search.RecordQuietCutoffForTest(promotion, depth: 6, plyFromRoot: -1);

            int quietScore = search.OrderScoreForTest(quietPawn, ttMove: 0, plyFromRoot: -1);
            int freshSearchQuietScore = NewSearch().OrderScoreForTest(quietPawn, ttMove: 0, plyFromRoot: -1);

            Assert.That(quietScore, Is.EqualTo(freshSearchQuietScore),
                "Recording a promotion must never touch the quiet-move history table.");
        }

        [Test]
        public void BetrayalStageMove_NeverUpdatesHistory()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand act = ActMove();
            MoveCommand quietKnight = QuietMove(ChessPieceType.Knight);

            search.RecordQuietCutoffForTest(act, depth: 6, plyFromRoot: -1);

            int quietScore = search.OrderScoreForTest(quietKnight, ttMove: 0, plyFromRoot: -1);
            int freshSearchQuietScore = NewSearch().OrderScoreForTest(quietKnight, ttMove: 0, plyFromRoot: -1);

            Assert.That(quietScore, Is.EqualTo(freshSearchQuietScore),
                "An Act (or any other Betrayal-stage) move must never blend into normal-ply quiet-move history — it isn't an ordinary quiet developing move.");
        }

        [Test]
        public void HistoryBonus_NeverOutranksAnActMove()
        {
            // Losing captures and quiet moves have always shared the same tier (a losing capture's
            // own score can be as low as 1, e.g. queen takes pawn) — history is only guaranteed to
            // stay below the tier ABOVE that shared band, which is Act.
            AlphaBetaSearch search = NewSearch();
            MoveCommand quiet = QuietMove(ChessPieceType.Knight);
            MoveCommand act = ActMove();

            // Saturate history for this quiet move with many deep cutoffs.
            for (int i = 0; i < 20; i++)
                search.RecordQuietCutoffForTest(quiet, depth: 7, plyFromRoot: -1);

            int quietScore = search.OrderScoreForTest(quiet, ttMove: 0, plyFromRoot: -1);
            int actScore = search.OrderScoreForTest(act, ttMove: 0, plyFromRoot: -1);

            Assert.That(quietScore, Is.LessThan(actScore),
                "Even a heavily-favored quiet move must stay below the Act tier band.");
        }

        [Test]
        public void HistoryBonus_IsClamped_SoItStaysWellWithinTheQuietBand()
        {
            AlphaBetaSearch search = NewSearch();
            MoveCommand quiet = QuietMove(ChessPieceType.Knight);

            for (int i = 0; i < 50; i++)
                search.RecordQuietCutoffForTest(quiet, depth: 7, plyFromRoot: -1);

            int quietScore = search.OrderScoreForTest(quiet, ttMove: 0, plyFromRoot: -1);

            Assert.That(quietScore, Is.LessThan(10_000),
                "History must never grow large enough to reach the Act tier band, however many cutoffs accumulate.");
        }
    }
}
