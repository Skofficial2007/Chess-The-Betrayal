using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.BookImport
{
    /// <summary>
    /// Covers the trap book source format and its compiler against inline text, so the format is
    /// proven before any real trap data depends on it.
    ///
    /// The checks worth having here are the ones that would otherwise produce a book that compiles
    /// and is wrong: a record whose position is a ply out, two records disagreeing about the same
    /// position, or a losing move that isn't actually available where the record claims. None of
    /// those look like errors in a text file.
    /// </summary>
    [TestFixture]
    public class TrapBookCompilerTests
    {
        // The Legal Trap, the shortest well-known trap that reaches a real position: after
        // 1.e4 e5 2.Nf3 d6 3.Bc4 Nc6 4.Nc3 Bg4 5.h3 Bh5 6.Nxe5, taking the queen with 6...Bxd1
        // is mated in two, while 6...Nxe5 is fine.
        private const string LegalTrap =
            "e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5 | avoid=h5d1 best=c6e5 | Legal Trap";

        [Test]
        public void Compile_ValidRecord_ProducesOneEntryNamingTheTrap()
        {
            var (keys, blunders, bests, names, scheme) = TrapBookCompiler.Compile(LegalTrap);

            Assert.That(keys.Length, Is.EqualTo(1));
            Assert.That(names[0], Is.EqualTo("Legal Trap"));
            Assert.That(scheme, Is.EqualTo(BoardState.ZobristSchemeVersion));
            Assert.That(blunders[0], Is.Not.EqualTo(bests[0]));

            // The recorded key must be the position a search would actually present after those
            // moves, or every lookup silently misses.
            Assert.That(keys[0], Is.EqualTo(ReplayForHash(
                "e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5")));
        }

        [Test]
        public void Compile_RecordedMoves_AreTheOnesNamedInTheSource()
        {
            var (_, blunders, bests, _, _) = TrapBookCompiler.Compile(LegalTrap);

            BoardState board = ReplayForBoard("e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5");
            var legal = new List<MoveCommand>();
            new ChessEngineAdapter().GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legal);

            MoveCommand blunder = legal.Single(m => PackedMove.Pack(m) == blunders[0]);
            MoveCommand best = legal.Single(m => PackedMove.Pack(m) == bests[0]);

            Assert.That(blunder.StartPosition, Is.EqualTo(new Vector2Int(7, 4)), "avoid should be the bishop on h5");
            Assert.That(blunder.EndPosition, Is.EqualTo(new Vector2Int(3, 0)), "avoid should capture on d1");
            Assert.That(best.StartPosition, Is.EqualTo(new Vector2Int(2, 5)), "best should be the knight on c6");
            Assert.That(best.EndPosition, Is.EqualTo(new Vector2Int(4, 4)), "best should recapture on e5");
        }

        [Test]
        public void Compile_SetupMoveIllegal_ThrowsNamingThePly()
        {
            var ex = Assert.Throws<TrapBookParseException>(() =>
                TrapBookCompiler.Compile("e2e4 e7e5 e2e4 | avoid=g1f3 best=b1c3 | Nonsense"));

            Assert.That(ex.Message, Does.Contain("Ply 3"));
            Assert.That(ex.Message, Does.Contain("e2e4"));
        }

        [Test]
        public void Compile_AvoidMoveNotLegalInThatPosition_Throws()
        {
            // A record one ply out of step is the failure this format is most likely to suffer,
            // and it is invisible on inspection.
            var ex = Assert.Throws<TrapBookParseException>(() =>
                TrapBookCompiler.Compile("e2e4 e7e5 | avoid=h5d1 best=g1f3 | Off by one"));

            Assert.That(ex.Message, Does.Contain("avoid"));
            Assert.That(ex.Message, Does.Contain("not legal"));
        }

        [Test]
        public void Compile_BestMoveNotLegalInThatPosition_Throws()
        {
            var ex = Assert.Throws<TrapBookParseException>(() =>
                TrapBookCompiler.Compile("e2e4 e7e5 | avoid=g1f3 best=h5d1 | Off by one"));

            Assert.That(ex.Message, Does.Contain("best"));
            Assert.That(ex.Message, Does.Contain("not legal"));
        }

        [Test]
        public void Compile_AvoidAndBestAreTheSameMove_Throws()
        {
            var ex = Assert.Throws<TrapBookParseException>(() =>
                TrapBookCompiler.Compile("e2e4 e7e5 | avoid=g1f3 best=g1f3 | Contradiction"));

            Assert.That(ex.Message, Does.Contain("same move"));
        }

        [Test]
        public void Compile_TwoRecordsAgreeingOnAPosition_MergeIntoOne()
        {
            // Real traps do this: two named traps can reach one identical position, and both
            // records are correct. Keeping both would double-count the same fact.
            string source = LegalTrap + "\n" +
                "e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5 | avoid=h5d1 best=c6e5 | Legal Trap (other name)";

            var (keys, _, _, names, _) = TrapBookCompiler.Compile(source);

            Assert.That(keys.Length, Is.EqualTo(1));
            Assert.That(names[0], Is.EqualTo("Legal Trap"), "The first record's name should win.");
        }

        [Test]
        public void Compile_TwoRecordsDisagreeingOnWhichMoveLoses_ThrowsNamingBoth()
        {
            // Whichever was read last would otherwise win silently.
            string source = LegalTrap + "\n" +
                "e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5 | avoid=g8f6 best=c6e5 | Rival Trap";

            var ex = Assert.Throws<TrapBookParseException>(() => TrapBookCompiler.Compile(source));

            Assert.That(ex.Message, Does.Contain("Rival Trap"));
            Assert.That(ex.Message, Does.Contain("Legal Trap"));
            Assert.That(ex.Message, Does.Contain("which move loses"));
        }

        [Test]
        public void Compile_TwoRecordsNamingDifferentGoodReplies_MergeRatherThanFail()
        {
            // A position with one losing move usually has several sound replies, so two sources
            // naming different ones are both right. Two separate research runs did exactly this
            // for the Mortimer and the Fishing Pole, agreeing on the mistake and differing on the
            // developing move to prefer instead.
            string source = LegalTrap + "\n" +
                "e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5 | avoid=h5d1 best=g8f6 | Same Trap, Other Source";

            var (keys, _, bests, names, _) = TrapBookCompiler.Compile(source);

            Assert.That(keys.Length, Is.EqualTo(1));
            Assert.That(names[0], Is.EqualTo("Legal Trap"));

            var (_, _, expectedBests, _, _) = TrapBookCompiler.Compile(LegalTrap);
            Assert.That(bests[0], Is.EqualTo(expectedBests[0]), "The first record's reply should win.");
        }

        [Test]
        public void Compile_KeysAreSortedAscending_ForBinarySearch()
        {
            string source = string.Join("\n",
                LegalTrap,
                "e2e4 e7e5 g1f3 b8c6 f1c4 g8f6 f3g5 d7d5 e4d5 | avoid=f6d5 best=c6a5 | Fried Liver",
                "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c1g5 b8d7 | avoid=c4d5 best=e2e3 | Elephant Trap");

            var (keys, _, _, _, _) = TrapBookCompiler.Compile(source);

            Assert.That(keys.Length, Is.EqualTo(3));
            Assert.That(keys, Is.Ordered.Ascending);
        }

        [Test]
        public void Compile_BetrayalMove_IsRejected()
        {
            // Reaching a Betrayal-eligible position through ordinary opening moves would take far
            // more plies than is practical here, so the position is built directly: White's queen
            // attacks its own knight, a textbook legal Act target.
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d2", Team.White, ChessPieceType.Knight)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

            var engine = new ChessEngineAdapter();

            // Built directly rather than parsed: the board above is already at the position the
            // record describes, and the source format requires at least one move to get there.
            var line = new TrapBookLine(
                sourceLineNumber: 1,
                setupMoves: new List<(Vector2Int, Vector2Int, ChessPieceType)>(),
                blunderMove: (new Vector2Int(3, 0), new Vector2Int(3, 1), ChessPieceType.None),
                bestMove: (new Vector2Int(0, 0), new Vector2Int(0, 1), ChessPieceType.None),
                name: "Act offered as a trap move");

            var ex = Assert.Throws<TrapBookParseException>(() =>
                TrapBookCompiler.Replay(line, engine, board));

            Assert.That(ex.Message, Does.Contain("Betrayal"));
        }

        [Test]
        public void Parse_CommentAndBlankLines_ReturnNull()
        {
            Assert.That(TrapBookLine.Parse("# just a heading", 1), Is.Null);
            Assert.That(TrapBookLine.Parse("   ", 2), Is.Null);
            Assert.That(TrapBookLine.Parse("", 3), Is.Null);
        }

        [Test]
        public void Compile_EmptyOrCommentOnlySource_ProducesEmptyValidBook()
        {
            var (keys, _, _, _, scheme) = TrapBookCompiler.Compile("# nothing here\n\n");

            Assert.That(keys, Is.Empty);
            Assert.That(scheme, Is.EqualTo(BoardState.ZobristSchemeVersion),
                "An empty book still has to carry the scheme, or it reads as compiled against another.");
        }

        [Test]
        public void Parse_MissingName_Throws()
        {
            var ex = Assert.Throws<TrapBookParseException>(() =>
                TrapBookLine.Parse("e2e4 | avoid=e7e5 best=d7d5 |   ", 1));

            Assert.That(ex.Message, Does.Contain("no name"));
        }

        [Test]
        public void Parse_WrongSectionCount_Throws()
        {
            Assert.Throws<TrapBookParseException>(() =>
                TrapBookLine.Parse("e2e4 | avoid=e7e5 best=d7d5", 1));
            Assert.Throws<TrapBookParseException>(() =>
                TrapBookLine.Parse("e2e4 e7e5 g1f3", 1));
        }

        [Test]
        public void Parse_UnknownOrDuplicateField_Throws()
        {
            var unknown = Assert.Throws<TrapBookParseException>(() =>
                TrapBookLine.Parse("e2e4 | avoid=e7e5 best=d7d5 worst=g8f6 | Name", 1));
            Assert.That(unknown.Message, Does.Contain("worst"));

            var duplicate = Assert.Throws<TrapBookParseException>(() =>
                TrapBookLine.Parse("e2e4 | avoid=e7e5 avoid=d7d5 best=g8f6 | Name", 1));
            Assert.That(duplicate.Message, Does.Contain("more than once"));
        }

        [Test]
        public void Parse_MissingAvoidOrBest_Throws()
        {
            Assert.That(
                Assert.Throws<TrapBookParseException>(() => TrapBookLine.Parse("e2e4 | best=e7e5 | Name", 1)).Message,
                Does.Contain("avoid"));
            Assert.That(
                Assert.Throws<TrapBookParseException>(() => TrapBookLine.Parse("e2e4 | avoid=e7e5 | Name", 1)).Message,
                Does.Contain("best"));
        }

        [Test]
        public void Parse_MalformedMoveToken_Throws()
        {
            Assert.Throws<TrapBookParseException>(() =>
                TrapBookLine.Parse("e2e4 z9z9 | avoid=e7e5 best=d7d5 | Name", 1));
            Assert.Throws<TrapBookParseException>(() =>
                TrapBookLine.Parse("e2e4 | avoid=e7 best=d7d5 | Name", 1));
        }

        private static BoardState ReplayForBoard(string moves)
        {
            var engine = new ChessEngineAdapter();
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            var legal = new List<MoveCommand>();

            foreach (string token in moves.Split(' '))
            {
                var from = new Vector2Int(token[0] - 'a', token[1] - '1');
                var to = new Vector2Int(token[2] - 'a', token[3] - '1');

                legal.Clear();
                engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legal);
                MoveCommand move = legal.Single(m => m.StartPosition == from && m.EndPosition == to);

                new TurnResolver().Advance(board, move);
            }

            return board;
        }

        private static ulong ReplayForHash(string moves) => ReplayForBoard(moves).ZobristHash;
    }
}
