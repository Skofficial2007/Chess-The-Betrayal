using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Gameplay.Interaction;
using ChessTheBetrayal.Tooling;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Interaction
{
    /// <summary>
    /// The ordinary turn, where no Betrayal is under way and the executor is deciding what a tap on
    /// two squares meant. Two of the answers it gives cannot be read off the move that reaches the
    /// board afterwards, which is what makes them worth pinning here rather than at engine level: a
    /// pawn arriving on the last rank plays nothing at all until the player has said what it
    /// becomes, and a castle is requested by tapping the rook but played onto a square the player
    /// never named.
    /// </summary>
    [TestFixture]
    public class LocalMoveExecutorRequestTests
    {
        private static Vector2Int Sq(string algebraic) => TestBoardSetupUtility.AlgebraicToVector(algebraic);

        private BoardState _board;
        private IChessEngine _engine;
        private LocalMoveExecutor _executor;
        private TurnPhase _phase;

        private readonly List<MoveCommand> _confirmed = new List<MoveCommand>();
        private readonly List<(Vector2Int from, Vector2Int to)> _rejected = new List<(Vector2Int, Vector2Int)>();
        private readonly List<(Vector2Int from, Vector2Int to, bool isCapture)> _asked =
            new List<(Vector2Int, Vector2Int, bool)>();

        [SetUp]
        public void SetUp()
        {
            _confirmed.Clear();
            _rejected.Clear();
            _asked.Clear();
            _phase = TurnPhase.Normal;
            _engine = new ChessEngineAdapter();
        }

        private void UseBoard(BoardState board)
        {
            _board = board;
            // No clock: these are untimed turns, so nothing here is stamped with a snapshot and the
            // moves that come back carry only what the request itself decided.
            _executor = new LocalMoveExecutor(_board, _engine, () => _phase, logMoves: false);
            _executor.OnMoveConfirmed += move => _confirmed.Add(move);
            _executor.OnMoveRejected += (from, to) => _rejected.Add((from, to));
            _executor.OnPromotionRequired += (from, to, isCapture) => _asked.Add((from, to, isCapture));
        }

        /// <summary>White's pawn on e7 is one step from the last rank, with the square ahead empty.</summary>
        private static BoardState APawnOneStepFromTheLastRank() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.King)
                .WithPiece("e7", Team.White, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithComputedHash();

        /// <summary>
        /// Nothing may reach the board here. The engine offers four moves to e8 that differ only in
        /// what the pawn turns into, so playing one without asking would be choosing for the player.
        /// </summary>
        [Test]
        public void RequestMove_ForAPawnReachingTheLastRank_AsksWhatItBecomes()
        {
            UseBoard(APawnOneStepFromTheLastRank());

            _executor.RequestMove(Sq("e7"), Sq("e8"));

            Assert.That(_asked.Count, Is.EqualTo(1), "The player must be asked to choose a piece.");
            Assert.That(_asked[0].from, Is.EqualTo(Sq("e7")));
            Assert.That(_asked[0].to, Is.EqualTo(Sq("e8")));
            Assert.That(_asked[0].isCapture, Is.False, "Nothing is standing on e8.");
            Assert.That(_confirmed, Is.Empty, "The move waits on the answer.");
            Assert.That(_rejected, Is.Empty, "Being asked is not being turned down.");
        }

        /// <summary>
        /// Both of White's castles are available, which is what makes a mix-up here visible: get the
        /// destination file wrong and the other castle is a legal move too, so the king quietly ends
        /// up on the wrong side of the board rather than the request being refused.
        /// </summary>
        private static BoardState BothCastlesAvailable() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King, hasMoved: false)
                .WithPiece("a1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("h1", Team.White, ChessPieceType.Rook, hasMoved: false)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithCastlingRights(BoardState.CastlingWhiteKingside | BoardState.CastlingWhiteQueenside)
                .WithComputedHash();

        /// <summary>
        /// A castle is asked for by tapping the rook, because that is the piece the player can see is
        /// involved. The king's real destination is two files along, and the executor is the only
        /// thing that knows it.
        /// </summary>
        [Test]
        public void RequestMove_TappingTheKingsideRook_CastlesOntoTheGFile()
        {
            UseBoard(BothCastlesAvailable());

            _executor.RequestMove(Sq("e1"), Sq("h1"));

            Assert.That(_rejected, Is.Empty);
            Assert.That(_confirmed.Count, Is.EqualTo(1));
            Assert.That(_confirmed[0].EndPosition, Is.EqualTo(Sq("g1")), "Kingside puts the king on g1.");
            Assert.That(_confirmed[0].IsCastling, Is.True);
            Assert.That(_confirmed[0].RookStartPosition, Is.EqualTo(Sq("h1")));
            Assert.That(_confirmed[0].RookEndPosition, Is.EqualTo(Sq("f1")));
        }

        [Test]
        public void RequestMove_TappingTheQueensideRook_CastlesOntoTheCFile()
        {
            UseBoard(BothCastlesAvailable());

            _executor.RequestMove(Sq("e1"), Sq("a1"));

            Assert.That(_rejected, Is.Empty);
            Assert.That(_confirmed.Count, Is.EqualTo(1));
            Assert.That(_confirmed[0].EndPosition, Is.EqualTo(Sq("c1")),
                "Queenside puts the king on c1, not beside the rook it was tapped on.");
            Assert.That(_confirmed[0].IsCastling, Is.True);
            Assert.That(_confirmed[0].RookStartPosition, Is.EqualTo(Sq("a1")));
            Assert.That(_confirmed[0].RookEndPosition, Is.EqualTo(Sq("d1")));
        }

        /// <summary>
        /// Black's rook on d8 could make this move on Black's turn, and the empty square on b4 is not
        /// a piece at all. Neither request survives being asked on White's turn.
        /// </summary>
        private static BoardState BlackHasARookAndItIsWhitesTurn() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithPiece("d8", Team.Black, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithComputedHash();

        [Test]
        public void RequestMove_ForAPieceThatIsNotYours_IsRefused()
        {
            UseBoard(BlackHasARookAndItIsWhitesTurn());

            _executor.RequestMove(Sq("d8"), Sq("d5"));

            Assert.That(_confirmed, Is.Empty, "Moving for the other side would hand away the turn.");
            Assert.That(_rejected, Is.EqualTo(new[] { (Sq("d8"), Sq("d5")) }));
        }

        [Test]
        public void RequestMove_FromAnEmptySquare_IsRefused()
        {
            UseBoard(BlackHasARookAndItIsWhitesTurn());

            _executor.RequestMove(Sq("b4"), Sq("b5"));

            Assert.That(_confirmed, Is.Empty);
            Assert.That(_rejected, Is.EqualTo(new[] { (Sq("b4"), Sq("b5")) }));
        }
    }
}
