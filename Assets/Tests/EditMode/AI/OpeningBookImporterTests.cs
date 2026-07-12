using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Every test here compiles real source text through the real engine — no hand-computed
    /// hashes or mocked move lists — because the entire point of this compiler is that a book
    /// entry can only ever be exactly what the engine itself would have played.
    /// </summary>
    [TestFixture]
    public class OpeningBookImporterTests
    {
        [Test]
        public void Compile_ShortLegalLine_ProducesOneEntryPerPly()
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile("e2e4 e7e5");

            Assert.That(keys.Length, Is.EqualTo(2));
            Assert.That(schemeVersion, Is.EqualTo(BoardState.ZobristSchemeVersion));

            var engine = new ChessEngineAdapter();
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            ulong hashBeforeE2E4 = board.ZobristHash;
            var legalMoves = new List<MoveCommand>();
            engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
            MoveCommand e2e4 = legalMoves.Single(m =>
                m.StartPosition == new Vector2Int(4, 1) && m.EndPosition == new Vector2Int(4, 3));

            // Keys are sorted by hash value for binary search, so the entry order doesn't follow
            // ply order — locate the e2e4 entry by its hash rather than assuming it's first.
            int e2e4Index = System.Array.IndexOf(keys, hashBeforeE2E4);
            Assert.That(e2e4Index, Is.GreaterThanOrEqualTo(0));
            Assert.That(packedMoves[e2e4Index], Is.EqualTo(AlphaBetaSearch.PackMove(e2e4)));
            Assert.That(weights[e2e4Index], Is.EqualTo(1));
        }

        [Test]
        public void Compile_IllegalMoveToken_ThrowsWithLineAndTokenInMessage()
        {
            // e2e5 is not a legal opening move for a pawn on e2 (too far, nothing to capture).
            var ex = Assert.Throws<OpeningBookParseException>(() =>
                OpeningBookCompiler.Compile("e2e4 e7e5\ne2e5 g8f6"));

            Assert.That(ex.Message, Does.Contain("line 2"));
            Assert.That(ex.Message, Does.Contain("e2e5"));
        }

        [Test]
        public void ReplayLine_BetrayalActMove_IsRejected()
        {
            // A hand-built position where White's queen directly attacks its own knight — a
            // textbook legal Act target — lets this test exercise the rejection without needing
            // to reach a Betrayal-eligible position via many plies of normal opening theory.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d2", Team.White, ChessPieceType.Knight)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            var engine = new ChessEngineAdapter();
            var legalMoves = new List<MoveCommand>();
            engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
            MoveCommand act = legalMoves.Single(m =>
                m.Stage == BetrayalStage.Act && m.EndPosition == new Vector2Int(3, 1));

            var line = OpeningBookLine.Parse("d1d2", sourceLineNumber: 1);

            var ex = Assert.Throws<OpeningBookParseException>(() =>
                OpeningBookCompiler.ReplayLine(line, engine, board).ToList());

            Assert.That(ex.Message, Does.Contain("Betrayal"));
            Assert.That(act.Stage, Is.EqualTo(BetrayalStage.Act), "Sanity check: the position really does offer an Act here.");
        }

        [Test]
        public void Compile_TransposingLines_MergeIntoOneEntryWithSummedWeight()
        {
            // A book entry is keyed on the position BEFORE a move, so two lines only collide on
            // the same entry when they play the identical move from the identical position — here,
            // two separate lines both reach 1.e4 c5 and both continue 2.Nf3. The middle line shares
            // the first two plies but diverges on move three, proving it doesn't get folded in.
            var (keys, packedMoves, weights, _) = OpeningBookCompiler.Compile(
                "e2e4 c7c5 g1f3 | w=3\ne2e4 c7c5 b1c3\ne2e4 c7c5 g1f3 | w=2");

            var engine = new ChessEngineAdapter();
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            PlayToken(engine, board, "e2e4");
            PlayToken(engine, board, "c7c5");
            ulong hashBeforeNf3 = board.ZobristHash;
            var legalMoves = new List<MoveCommand>();
            engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
            MoveCommand nf3 = legalMoves.Single(m =>
                m.StartPosition == new Vector2Int(6, 0) && m.EndPosition == new Vector2Int(5, 2));
            uint nf3Packed = AlphaBetaSearch.PackMove(nf3);

            var matchingRows = Enumerable.Range(0, keys.Length)
                .Where(i => keys[i] == hashBeforeNf3 && packedMoves[i] == nf3Packed)
                .ToList();
            Assert.That(matchingRows, Has.Count.EqualTo(1), "Repeated (hash, move) pairs must merge into exactly one row.");
            Assert.That(weights[matchingRows[0]], Is.EqualTo(5), "Weights from both matching lines (3 + 2) must sum, not duplicate or overwrite.");

            // No duplicate rows for the same (hash, move) pair anywhere in the compiled book.
            var seen = new HashSet<(ulong, uint)>();
            for (int i = 0; i < keys.Length; i++)
                Assert.That(seen.Add((keys[i], packedMoves[i])), Is.True, "Duplicate (hash, move) entry found — merge failed.");
        }

        [Test]
        public void Compile_KeysAreSortedAscending_ForBinarySearch()
        {
            var (keys, _, _, _) = OpeningBookCompiler.Compile(
                "e2e4 e7e5 g1f3 b8c6 f1c4\nd2d4 d7d5 c2c4 e7e6\nc2c4 e7e5");

            // Non-decreasing, not strictly increasing: two different book lines can legitimately
            // reach the same position (same hash) while recommending different next moves, which
            // shows up as more than one row sharing a key — binary search only needs the keys
            // grouped together in order, not unique.
            for (int i = 1; i < keys.Length; i++)
                Assert.That(keys[i], Is.GreaterThanOrEqualTo(keys[i - 1]), $"Keys must be non-decreasing at index {i} for binary search to work.");
        }

        [Test]
        public void Parse_OmittedWeight_DefaultsToOne()
        {
            OpeningBookLine line = OpeningBookLine.Parse("e2e4 e7e5", sourceLineNumber: 1);
            Assert.That(line.Weight, Is.EqualTo(1));
        }

        [Test]
        public void Parse_MalformedWeight_ThrowsLoudly()
        {
            Assert.Throws<OpeningBookParseException>(() =>
                OpeningBookLine.Parse("e2e4 e7e5 | w=notanumber", sourceLineNumber: 1));

            Assert.Throws<OpeningBookParseException>(() =>
                OpeningBookLine.Parse("e2e4 e7e5 | w=0", sourceLineNumber: 1));
        }

        [Test]
        public void Parse_CommentAndBlankLines_ReturnNull()
        {
            Assert.That(OpeningBookLine.Parse("# just a comment", 1), Is.Null);
            Assert.That(OpeningBookLine.Parse("   ", 2), Is.Null);
            Assert.That(OpeningBookLine.Parse("", 3), Is.Null);
        }

        [Test]
        public void Compile_EmptyOrCommentOnlySource_ProducesEmptyValidBook()
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile(
                "# nothing here yet\n\n   \n# still nothing");

            Assert.That(keys, Is.Empty);
            Assert.That(packedMoves, Is.Empty);
            Assert.That(weights, Is.Empty);
            Assert.That(schemeVersion, Is.EqualTo(BoardState.ZobristSchemeVersion));
        }

        [Test]
        public void Compile_PromotionToken_ParsesPromotionPieceCorrectly()
        {
            // A minimal position where a White pawn on the 7th can promote immediately.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("a7", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

            var engine = new ChessEngineAdapter();
            var legalMoves = new List<MoveCommand>();
            engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
            MoveCommand queenPromotion = legalMoves.Single(m => m.PromotedTo == ChessPieceType.Queen);

            var line = OpeningBookLine.Parse("a7a8q", sourceLineNumber: 1);
            var entries = OpeningBookCompiler.ReplayLine(line, engine, board).ToList();

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].PackedMove, Is.EqualTo(AlphaBetaSearch.PackMove(queenPromotion)));
        }

        private static void PlayToken(IChessEngine engine, BoardState board, string token)
        {
            OpeningBookLine line = OpeningBookLine.Parse(token, sourceLineNumber: 1);
            OpeningBookCompiler.ReplayLine(line, engine, board).ToList();
        }
    }
}
