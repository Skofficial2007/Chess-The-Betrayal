using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    /// <summary>
    /// ApplyMove/UndoMove are the interface seam the AI search depends on exclusively — it never
    /// calls the static ChessEngine class directly (see IChessEngine's doc comment on why: an
    /// instantiable engine, not a static reach-through). If ChessEngineAdapter's forwarding ever
    /// drifts from the static methods it wraps, make/unmake silently breaks for every consumer of
    /// IChessEngine, and it would look like a search bug rather than an adapter bug. These tests
    /// pin that ApplyMove/UndoMove behave identically to ChessEngine.ApplyMoveToBoard/UndoMoveOnBoard.
    /// </summary>
    [TestFixture]
    public class ChessEngineAdapterTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        [Test]
        public void ApplyMove_StandardMove_MovesPieceToDestination()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("e2", Team.White, ChessPieceType.Pawn);
            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e2");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e4");
            PieceData pawn = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, pawn, default, board);

            _engine.ApplyMove(board, move);

            Assert.That(board.GetPiece(to).Type, Is.EqualTo(ChessPieceType.Pawn));
            Assert.That(board.GetPiece(from).IsEmpty, Is.True);
        }

        [Test]
        public void ApplyMove_ThenUndoMove_RestoresOriginalPosition()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece("d5", Team.White, ChessPieceType.Knight, hasMoved: true);
            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("d5");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("f6");
            PieceData knight = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, knight, default, board);

            _engine.ApplyMove(board, move);
            _engine.UndoMove(board, move);

            Assert.That(board.GetPiece(from).Type, Is.EqualTo(ChessPieceType.Knight), "Piece must return to its origin square.");
            Assert.That(board.GetPiece(from).HasMoved, Is.True, "HasMoved must be restored to its pre-move value, not reset.");
            Assert.That(board.GetPiece(to).IsEmpty, Is.True, "Destination must be empty again after undo.");
        }

        [Test]
        public void ApplyMove_ThenUndoMove_RestoresCapturedPiece()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn);
            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e4");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("d5");
            PieceData attacker = board.GetPiece(from);
            PieceData victim = board.GetPiece(to);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, attacker, victim, board);

            _engine.ApplyMove(board, move);
            _engine.UndoMove(board, move);

            PieceData restoredVictim = board.GetPiece(to);
            Assert.That(restoredVictim.IsEmpty, Is.False, "Captured piece must be restored by undo.");
            Assert.That(restoredVictim.Team, Is.EqualTo(Team.Black));
            Assert.That(restoredVictim.Type, Is.EqualTo(ChessPieceType.Pawn));
        }

        [Test]
        public void ApplyMove_ThenUndoMove_RestoresZobristHash()
        {
            // Search relies on the Zobrist hash for transposition lookups — if make/unmake through
            // the interface seam ever desynced the hash from the static path, a TT built on top of
            // AlphaBetaSearch would silently corrupt itself the moment AI work resumes.
            BoardState board = TestBoardSetupUtility.CreateStandard();
            ulong hashBefore = board.ZobristHash;
            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e2");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e4");
            PieceData pawn = board.GetPiece(from);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, pawn, default, board);

            _engine.ApplyMove(board, move);
            _engine.UndoMove(board, move);

            Assert.That(board.ZobristHash, Is.EqualTo(hashBefore), "Undo must restore the exact pre-move Zobrist hash.");
        }

        [Test]
        public void ApplyMove_MatchesStaticChessEngineApplyMoveToBoard()
        {
            // Two boards, identical starting state: one driven through the interface seam, one
            // through the static method it wraps. Their resulting piece layout must be identical —
            // this is the whole adapter contract.
            BoardState boardViaInterface = TestBoardSetupUtility.CreateEmpty().WithPiece("e2", Team.White, ChessPieceType.Pawn);
            BoardState boardViaStatic = TestBoardSetupUtility.CreateEmpty().WithPiece("e2", Team.White, ChessPieceType.Pawn);

            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("e2");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("e4");
            PieceData pawn = boardViaInterface.GetPiece(from);

            MoveCommand moveA = MoveCommand.CreateStandardMove(from, to, pawn, default, boardViaInterface);
            MoveCommand moveB = MoveCommand.CreateStandardMove(from, to, pawn, default, boardViaStatic);

            _engine.ApplyMove(boardViaInterface, moveA);
            ChessEngine.ApplyMoveToBoard(boardViaStatic, moveB);

            Assert.That(boardViaInterface.GetPiece(to).Type, Is.EqualTo(boardViaStatic.GetPiece(to).Type));
            Assert.That(boardViaInterface.GetPiece(from).IsEmpty, Is.EqualTo(boardViaStatic.GetPiece(from).IsEmpty));
        }

        [Test]
        public void UndoMove_DefectionMove_RevertsTeamFlipInPlace()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("d5", Team.White, ChessPieceType.Knight)
                .WithPendingBetrayer("d5", Team.White);
            Vector2Int square = TestBoardSetupUtility.AlgebraicToVector("d5");
            PieceData betrayer = board.GetPiece(square);
            MoveCommand defection = MoveCommand.CreateDefectionMove(square, betrayer, board);

            _engine.ApplyMove(board, defection);
            Assert.That(board.GetPiece(square).Team, Is.EqualTo(Team.Black), "Defection must flip the piece to the opposing team.");

            _engine.UndoMove(board, defection);
            Assert.That(board.GetPiece(square).Team, Is.EqualTo(Team.White), "Undo must flip the defected piece back to its original team.");
        }
    }
}
