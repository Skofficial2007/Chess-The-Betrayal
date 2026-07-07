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

        [Test]
        public void ApplyMove_ThenUndoMove_ActStage_RestoresPendingBetrayerState()
        {
            // Betrayer (Knight b1) targets a friendly victim (Pawn a3); the Act move relocates the
            // Knight and opens the Retribution window. Undo through the seam must restore both the
            // piece layout and the pending-Betrayer board state exactly.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(true);
            board.ComputeFullZobristHash();
            ulong initialHash = board.ZobristHash;

            var moveBuffer = new System.Collections.Generic.List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b1"), moveBuffer);
            MoveCommand actMove = moveBuffer[0];

            _engine.ApplyMove(board, actMove);
            Assert.That(board.BetrayalRightAvailable, Is.False, "Act must consume the betrayal right.");
            Assert.That(board.PendingBetrayerSquare, Is.Not.Null, "Act must open the pending-Betrayer window.");

            _engine.UndoMove(board, actMove);

            Assert.That(board.BetrayalRightAvailable, Is.True, "Undo must restore the unspent betrayal right.");
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Undo must clear the pending-Betrayer window.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("b1")).Type, Is.EqualTo(ChessPieceType.Knight), "Knight must return to its origin square.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).Type, Is.EqualTo(ChessPieceType.Pawn), "Victim pawn must be restored at a3.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain internally consistent after unwind.");
            Assert.That(board.ZobristHash, Is.EqualTo(initialHash), "Hash must return to its pre-Act value after undo.");
        }

        [Test]
        public void ApplyMove_ThenUndoMove_RetributionStage_RestoresExecutionerAndBetrayer()
        {
            // Rook a1 (executioner) captures the just-betrayed Knight at a3, closing the sequence.
            // Undo through the seam must restore the Rook, resurrect the Knight, and reopen the
            // pending-Betrayer window exactly as it stood before Retribution was applied.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("a3", Team.White, ChessPieceType.Pawn)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithBetrayalRight(true);
            board.ComputeFullZobristHash();

            var moveBuffer = new System.Collections.Generic.List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(board, TestBoardSetupUtility.AlgebraicToVector("b1"), moveBuffer);
            MoveCommand actMove = moveBuffer[0];
            _engine.ApplyMove(board, actMove);
            ulong hashAfterAct = board.ZobristHash;

            ChessEngine.GetRetributionMoves(board, Team.White, board.PendingBetrayerSquare.Value, moveBuffer);
            MoveCommand retMove = moveBuffer[0];

            _engine.ApplyMove(board, retMove);
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Retribution must close the pending-Betrayer window.");

            _engine.UndoMove(board, retMove);

            Assert.That(board.PendingBetrayerSquare, Is.Not.Null, "Undo must reopen the pending-Betrayer window Retribution closed.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a1")).Type, Is.EqualTo(ChessPieceType.Rook), "Rook must return to a1.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("a3")).Type, Is.EqualTo(ChessPieceType.Knight), "Executed Knight must be restored at a3.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain internally consistent after unwind.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashAfterAct), "Hash must return to its post-Act, pre-Retribution value after undo.");

            _engine.UndoMove(board, actMove);
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain internally consistent after unwinding both plies.");
        }

        [Test]
        public void ApplyMove_ThenUndoMove_DefensiveOverrideStage_RestoresKingAndBetrayerState()
        {
            // Pinned executioner (Rook e4) can't take Retribution, so the Knight at e3 defects and
            // immediately checks the King at e1 — forcing a Defensive Override (King escape). Undo
            // through the seam must reverse the King's escape and restore the pre-save board state.
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e4", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithPiece("e3", Team.White, ChessPieceType.Knight)
                .WithPiece("d3", Team.White, ChessPieceType.Pawn)
                .WithBetrayalRight(true);
            board.ComputeFullZobristHash();

            MoveCommand actMove = MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector("e3"), TestBoardSetupUtility.AlgebraicToVector("d3"),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e3")),
                board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("d3")), board)
                .WithStage(BetrayalStage.Act);

            _engine.ApplyMove(board, actMove);

            var moveBuffer = new System.Collections.Generic.List<MoveCommand>();
            ChessEngine.GetRetributionMoves(board, Team.White, board.PendingBetrayerSquare.Value, moveBuffer);
            Assert.That(moveBuffer.Count, Is.EqualTo(0), "Pinned Rook must have no legal Retribution.");

            DefectionOutcome outcome = ChessEngine.ResolveFailedRetribution(board);
            Assert.That(outcome.RequiresForcedSave, Is.True, "Defected Knight must check the King, forcing a save.");
            ulong hashAfterDefection = board.ZobristHash;

            ChessEngine.GetForcedSaveMoves(board, Team.White, moveBuffer);
            MoveCommand saveMove = moveBuffer[0];

            _engine.ApplyMove(board, saveMove);
            Assert.That(board.PendingBetrayerSquare, Is.Null, "Defensive Override must close the pending-Betrayer window.");

            _engine.UndoMove(board, saveMove);

            Assert.That(board.PendingBetrayerSquare, Is.Not.Null, "Undo must reopen the pending-Betrayer window the save closed.");
            Assert.That(board.GetPiece(TestBoardSetupUtility.AlgebraicToVector("e1")).Type, Is.EqualTo(ChessPieceType.King), "King must be restored to e1.");
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain internally consistent after unwind.");
            Assert.That(board.ZobristHash, Is.EqualTo(hashAfterDefection), "Hash must return to its post-Defection, pre-save value after undo.");

            _engine.UndoMove(board, outcome.DefectionMove);
            _engine.UndoMove(board, actMove);
            Assert.DoesNotThrow(() => board.AssertZobristConsistency(), "Hash must remain internally consistent after unwinding the full sequence.");
        }
    }
}
