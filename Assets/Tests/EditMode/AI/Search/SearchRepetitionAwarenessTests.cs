using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The search has to see that a line returning to a position it has already been in is worth
    /// nothing, or a side that is already winning has no reason to prefer making progress over
    /// shuffling a piece back and forth. A real match ended up doing exactly that for twenty-nine
    /// consecutive moves in a position it had comprehensively won.
    /// </summary>
    [TestFixture]
    public class SearchRepetitionAwarenessTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup() => _engine = new ChessEngineAdapter();

        private static AISearchSettings Settings(int depth) =>
            new AISearchSettings(depth, TestTimeBudgets.Generous, BetrayalUsage.Full);

        private AlphaBetaSearch NewSearch() =>
            new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator());

        /// <summary>
        /// A won endgame with a pawn one square from promoting and a rook with nothing to do. The
        /// rook can shuffle between two squares forever; the pawn ends the game. Both were equal
        /// before the search could tell that shuffling goes nowhere.
        /// </summary>
        private static BoardState WonEndgameWithAShufflingRook() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("c3", Team.White, ChessPieceType.King)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("e7", Team.White, ChessPieceType.Pawn)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false)
                .WithComputedHash();

        [Test]
        public void APositionAlreadyPlayed_IsWorthNothingToEitherSide()
        {
            BoardState board = WonEndgameWithAShufflingRook();

            // The same position, recorded as though the game had already been through it once.
            board.PushPosition(board.ZobristHash, irreversible: false);

            Assert.That(board.CountPositionOccurrences(board.ZobristHash), Is.EqualTo(1),
                "One prior occurrence is what a search coming back to this position would find.");
        }

        [Test]
        public void PushAndPop_LeaveTheRecordExactlyAsItWasFound()
        {
            BoardState board = WonEndgameWithAShufflingRook();
            int before = board.PositionCount;

            board.PushPosition(0xABCDEF, irreversible: false);
            board.PopPosition();

            Assert.That(board.PositionCount, Is.EqualTo(before),
                "The search unmakes every move it makes, so anything recorded per move has to come "
                + "back off or a long search would drift further from the truth the deeper it went.");
        }

        /// <summary>
        /// The same symmetry, but through the engine's own apply and undo rather than through the
        /// record's methods directly — which is where it can actually break. The test above passes
        /// happily while the engine forgets to give a position back, because it never asks the engine
        /// to do anything; only this one fails.
        ///
        /// A missing give-back does not go wrong loudly. Leaf counts stay right, so nothing the perft
        /// suite measures moves; the record simply keeps positions from lines the search has already
        /// abandoned, and starts matching against them more the deeper it goes.
        /// </summary>
        [Test]
        public void ApplyingAndUndoingThroughTheEngine_LeavesTheRecordAsItWasFound()
        {
            BoardState board = LosingSideCanRepeat();
            MoveCommand move = FindMoveBetween(board, "h8", "g8");
            int before = board.PositionCount;

            _engine.ApplyMove(board, move);
            Assert.That(board.PositionCount, Is.EqualTo(before + 1),
                "Applying a move records the position it reached.");

            _engine.UndoMove(board, move);
            Assert.That(board.PositionCount, Is.EqualTo(before),
                "and unmaking it has to give that position back, or every line the search tries "
                + "leaves its positions behind for the next one to match against.");
        }

        [Test]
        public void APositionFromBeforeACaptureOrPawnMove_IsNeverCountedAgain()
        {
            BoardState board = WonEndgameWithAShufflingRook();
            ulong beforeTheBarrier = board.ZobristHash;

            board.PushPosition(beforeTheBarrier, irreversible: false);
            board.PushPosition(0x1111, irreversible: false);
            board.PushPosition(0x3333, irreversible: true); // a capture or a pawn move
            board.PushPosition(0x2222, irreversible: false);

            Assert.That(board.CountPositionOccurrences(beforeTheBarrier), Is.EqualTo(0),
                "A capture takes a piece off and a pawn only moves forward, so no position from "
                + "before one can occur again and counting it would claim a repetition that the game "
                + "has no way back to.");
        }

        [Test]
        public void ThePositionACaptureOrPawnMoveProduces_IsItselfStillRepeatable()
        {
            BoardState board = WonEndgameWithAShufflingRook();

            board.PushPosition(0x1111, irreversible: false);
            board.PushPosition(0x3333, irreversible: true); // the position the pawn move produced
            board.PushPosition(0x2222, irreversible: false);

            Assert.That(board.CountPositionOccurrences(0x3333), Is.EqualTo(1),
                "The barrier is what a position cannot be counted from BEFORE. The position the "
                + "irreversible move itself produced is on the near side of it, and pieces can "
                + "perfectly well shuffle back to it.");
        }

        [Test]
        public void PliesSinceIrreversibleMove_CountsFromTheLastCaptureOrPawnMove()
        {
            BoardState board = WonEndgameWithAShufflingRook();

            board.PushPosition(0x1, irreversible: true);
            board.PushPosition(0x2, irreversible: false);
            board.PushPosition(0x3, irreversible: false);

            Assert.That(board.PliesSinceIrreversibleMove, Is.EqualTo(2),
                "This is what the fifty-move rule counts, and it is also how far back a repetition "
                + "can possibly reach.");
        }

        /// <summary>
        /// A side a whole queen down, with a rook it can rock between two squares and a position it
        /// has already been through. Repeating is a draw and a draw is far better than the game it
        /// is otherwise losing, so a search that can see repetitions comes back near level while one
        /// that cannot comes back losing.
        ///
        /// Asserted on the score rather than on which move is chosen, and from the losing side
        /// rather than the winning one, because both of those close off explanations that have
        /// nothing to do with repetition. An earlier version of this had the winning side choosing
        /// between promoting a pawn and shuffling a rook, and passed just as happily with the whole
        /// mechanism disabled: promotion is worth eight hundred centipawns and material alone
        /// decided it. Nothing here can be explained by material - the material is unchanged
        /// whichever move is played.
        /// </summary>
        [Test]
        public void ALosingSide_TakesTheDrawAStepBackIntoAPlayedPositionOffers()
        {
            BoardState board = LosingSideCanRepeat();
            MoveCommand stepBack = FindMoveBetween(board, "h8", "g8");

            // Record the position that move leads to as one the game has already been through. Doing
            // it by playing the move and taking it straight back is what makes this the real hash of
            // the real resulting position rather than one written out here and hoped to match.
            _engine.ApplyMove(board, stepBack);
            ulong alreadyPlayed = board.ZobristHash;
            _engine.UndoMove(board, stepBack);
            board.PushPosition(alreadyPlayed, irreversible: false);

            AlphaBetaSearch search = NewSearch();
            MoveCommand best = search.FindBestMove(board, Settings(4), CancellationToken.None);
            int score = search.RootScores[search.BestRootIndex];

            TestContext.WriteLine($"chose {MoveNotation.Describe(best)} scoring {score} "
                + "(Black is a queen and a rook down; every move leaves it exactly that far behind)");

            Assert.That(score, Is.GreaterThan(-QueenIsWorthAboutThis / 2),
                "Stepping back into a position already played is a draw, and a draw is worth far more "
                + "to this side than the game it is otherwise losing. A losing score here means the "
                + "repetition was scored as though play simply carried on from it.");

            Assert.That(best.StartPosition, Is.EqualTo(stepBack.StartPosition),
                "and the move it chose should be the one that repeats");
            Assert.That(best.EndPosition, Is.EqualTo(stepBack.EndPosition));
        }

        /// <summary>Enough of a material gap that a losing score and a drawn one cannot be confused,
        /// without pinning the test to the evaluator's exact piece values.</summary>
        private const int QueenIsWorthAboutThis = 900;

        /// <summary>
        /// Black is a queen and a rook down with a spare rook move available. Nothing on the board
        /// attacks the black king, so every move Black has is legal and leaves the material exactly
        /// as it is - which is what stops material explaining anything the search decides here.
        /// </summary>
        private static BoardState LosingSideCanRepeat() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("c1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.Black)
                .WithBetrayalRight(false)
                .WithComputedHash();

        private MoveCommand FindMoveBetween(BoardState board, string from, string to)
        {
            Vector2Int fromSquare = TestBoardSetupUtility.AlgebraicToVector(from);
            Vector2Int toSquare = TestBoardSetupUtility.AlgebraicToVector(to);

            var legal = new List<MoveCommand>();
            _engine.GetLegalMoves(board, fromSquare, legal);

            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].EndPosition == toSquare) return legal[i];
            }

            Assert.Fail($"No legal move from {from} to {to} - the test position has changed.");
            return default;
        }

    }
}
