using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins MakeNullMove/UndoNullMove's own make/unmake contract in isolation, independent of
    /// whether the guard ever lets NMP fire in a real search: the pair must restore the hash,
    /// CurrentTurn, and EnPassantFile EXACTLY, both when an en-passant right existed and when it
    /// didn't (the no-EP path must not spuriously toggle the hash).
    /// </summary>
    [TestFixture]
    public class NullMoveZobristSymmetryTests
    {
        private ChessEngineAdapter _engine;
        private AlphaBetaSearch _search;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());
        }

        [Test]
        public void MakeThenUndo_NoEnPassantRight_RestoresHashTurnAndEnPassantExactly()
        {
            BoardState board = TestBoardSetupUtility.CreateStandard();
            ulong hashBefore = board.ZobristHash;
            Team turnBefore = board.CurrentTurn;

            _search.MakeNullMove(board, out int? saved);

            Assert.That(board.CurrentTurn, Is.Not.EqualTo(turnBefore), "MakeNullMove must pass the turn.");
            Assert.That(board.ZobristHash, Is.Not.EqualTo(hashBefore), "The turn-hash toggle must change the hash.");
            Assert.That(board.EnPassantFile, Is.Null);

            _search.UndoNullMove(board, saved);

            Assert.That(board.CurrentTurn, Is.EqualTo(turnBefore));
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
            Assert.That(board.EnPassantFile, Is.Null);
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }

        [Test]
        public void MakeThenUndo_WithEnPassantRight_RestoresHashTurnAndEnPassantExactly()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.Black)
                .WithEnPassantFile(3) // d-file
                .WithComputedHash();

            ulong hashBefore = board.ZobristHash;
            Team turnBefore = board.CurrentTurn;
            int? enPassantBefore = board.EnPassantFile;

            _search.MakeNullMove(board, out int? saved);

            Assert.That(board.EnPassantFile, Is.Null,
                "En passant does not survive a passed turn — MakeNullMove must clear it.");
            Assert.That(board.CurrentTurn, Is.Not.EqualTo(turnBefore));
            Assert.That(board.ZobristHash, Is.Not.EqualTo(hashBefore));

            _search.UndoNullMove(board, saved);

            Assert.That(board.EnPassantFile, Is.EqualTo(enPassantBefore));
            Assert.That(board.CurrentTurn, Is.EqualTo(turnBefore));
            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore));
            Assert.DoesNotThrow(() => board.AssertZobristConsistency());
        }
    }
}
