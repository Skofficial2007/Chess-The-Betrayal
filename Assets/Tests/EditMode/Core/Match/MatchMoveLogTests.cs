using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Match
{
    /// <summary>
    /// MatchMoveLog is the ground truth handed to a user for bug reports and the only place a
    /// checkmate-that-should-have-happened investigation starts. These tests guard the two
    /// properties that matter most: entries stay in the exact order they were recorded, and
    /// Clear() actually empties it (this is what stops one match's history bleeding into the next
    /// via MatchDriver's reused, never-reconstructed MoveLog instance).
    /// </summary>
    [TestFixture]
    public class MatchMoveLogTests
    {
        private MatchMoveLog _log;

        [SetUp]
        public void Setup()
        {
            _log = new MatchMoveLog();
        }

        private static MoveCommand StandardMove(string from, string to, Team team, ChessPieceType type)
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty().WithPiece(from, team, type);
            PieceData piece = board.GetPiece(TestBoardSetupUtility.AlgebraicToVector(from));
            return MoveCommand.CreateStandardMove(
                TestBoardSetupUtility.AlgebraicToVector(from), TestBoardSetupUtility.AlgebraicToVector(to), piece, default, board);
        }

        [Test]
        public void Record_SingleMove_AppearsInEntries()
        {
            MoveCommand move = StandardMove("e2", "e4", Team.White, ChessPieceType.Pawn);

            _log.Record(move, 1, GameState.Normal);

            Assert.AreEqual(1, _log.Entries.Count);
            Assert.AreEqual("1. e2-e4", _log.Entries[0].Notation);
        }

        [Test]
        public void Record_MultipleMoves_PreservesInsertionOrder()
        {
            _log.Record(StandardMove("e2", "e4", Team.White, ChessPieceType.Pawn), 1, GameState.Normal);
            _log.Record(StandardMove("e7", "e5", Team.Black, ChessPieceType.Pawn), 1, GameState.Normal);
            _log.Record(StandardMove("g1", "f3", Team.White, ChessPieceType.Knight), 2, GameState.Normal);

            Assert.AreEqual(3, _log.Entries.Count);
            Assert.AreEqual("1. e2-e4", _log.Entries[0].Notation);
            Assert.AreEqual("1... e7-e5", _log.Entries[1].Notation);
            Assert.AreEqual("2. Ng1-f3", _log.Entries[2].Notation);
        }

        [Test]
        public void Record_CheckmatingMove_AppendsHashInNotation()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("h5", Team.White, ChessPieceType.Queen)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn);
            Vector2Int from = TestBoardSetupUtility.AlgebraicToVector("h5");
            Vector2Int to = TestBoardSetupUtility.AlgebraicToVector("f7");
            PieceData queen = board.GetPiece(from);
            PieceData victim = board.GetPiece(to);
            MoveCommand move = MoveCommand.CreateStandardMove(from, to, queen, victim, board);

            _log.Record(move, 8, GameState.Checkmate);

            Assert.AreEqual("8. Qh5xf7#", _log.Entries[0].Notation);
            Assert.AreEqual(GameState.Checkmate, _log.Entries[0].ResultingState);
        }

        [Test]
        public void Record_StoresResultingStateAlongsideMove()
        {
            MoveCommand move = StandardMove("e2", "e4", Team.White, ChessPieceType.Pawn);

            _log.Record(move, 1, GameState.Check);

            Assert.AreEqual(GameState.Check, _log.Entries[0].ResultingState);
            Assert.AreEqual(move.StartPosition, _log.Entries[0].Move.StartPosition);
            Assert.AreEqual(move.EndPosition, _log.Entries[0].Move.EndPosition);
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            _log.Record(StandardMove("e2", "e4", Team.White, ChessPieceType.Pawn), 1, GameState.Normal);
            _log.Record(StandardMove("e7", "e5", Team.Black, ChessPieceType.Pawn), 1, GameState.Normal);

            _log.Clear();

            Assert.AreEqual(0, _log.Entries.Count, "Clear() must fully empty the log — MatchDriver reuses one MoveLog instance across matches and relies on this to avoid bleeding history between games.");
        }

        [Test]
        public void Clear_ThenRecord_StartsFreshWithoutOldEntries()
        {
            _log.Record(StandardMove("e2", "e4", Team.White, ChessPieceType.Pawn), 1, GameState.Normal);
            _log.Clear();

            _log.Record(StandardMove("d2", "d4", Team.White, ChessPieceType.Pawn), 1, GameState.Normal);

            Assert.AreEqual(1, _log.Entries.Count);
            Assert.AreEqual("1. d2-d4", _log.Entries[0].Notation);
        }

        [Test]
        public void DumpToString_EmptyLog_ReturnsEmptyString()
        {
            string dump = _log.DumpToString();

            Assert.AreEqual(string.Empty, dump);
        }

        [Test]
        public void DumpToString_MultipleEntries_OneLinePerPlyInOrder()
        {
            _log.Record(StandardMove("e2", "e4", Team.White, ChessPieceType.Pawn), 1, GameState.Normal);
            _log.Record(StandardMove("e7", "e5", Team.Black, ChessPieceType.Pawn), 1, GameState.Normal);

            string dump = _log.DumpToString();
            string[] lines = dump.Split(new[] { System.Environment.NewLine }, System.StringSplitOptions.RemoveEmptyEntries);

            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual("1. e2-e4", lines[0]);
            Assert.AreEqual("1... e7-e5", lines[1]);
        }

        [Test]
        public void ToString_OnEntry_ReturnsItsNotation()
        {
            MoveCommand move = StandardMove("e2", "e4", Team.White, ChessPieceType.Pawn);
            _log.Record(move, 1, GameState.Normal);

            Assert.AreEqual("1. e2-e4", _log.Entries[0].ToString());
        }
    }
}
