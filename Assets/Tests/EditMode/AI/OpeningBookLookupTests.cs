using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Tests.Utilities;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Every test compiles a real book from source text through OpeningBookCompiler (same as
    /// OpeningBookImporterTests) so the lookup is always exercised against a book shaped exactly
    /// like the one OpeningBookImportMenu would actually produce, never a hand-faked asset.
    /// </summary>
    [TestFixture]
    public class OpeningBookLookupTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private static OpeningBookAsset AssetFrom(string sourceText)
        {
            var (keys, packedMoves, weights, schemeVersion) = OpeningBookCompiler.Compile(sourceText);
            var asset = ScriptableObject.CreateInstance<OpeningBookAsset>();
            asset.SetEntries(keys, packedMoves, weights, schemeVersion);
            return asset;
        }

        /// <summary>Always returns 0 — forces any weighted pick to land on the first candidate.</summary>
        private sealed class ZeroRandomSource : IRandomSource
        {
            public bool NextBool() => false;
            public int NextInt(int maxExclusive) => 0;
            public float NextFloat() => 0f;
        }

        /// <summary>Always returns maxExclusive - 1 — forces a weighted pick to land on the last candidate.</summary>
        private sealed class MaxRandomSource : IRandomSource
        {
            public bool NextBool() => true;
            public int NextInt(int maxExclusive) => System.Math.Max(0, maxExclusive - 1);
            public float NextFloat() => 1f;
        }

        [Test]
        public void TryGetBookMove_StartingPosition_ReturnsCompiledReply()
        {
            OpeningBookAsset book = AssetFrom("e2e4 e7e5");
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            var legalMoves = new List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);
            MoveCommand e2e4 = legalMoves.Single(m =>
                m.StartPosition == new Vector2Int(4, 1) && m.EndPosition == new Vector2Int(4, 3));

            MoveCommand? result = OpeningBookLookup.TryGetBookMove(book, board, _engine, new ZeroRandomSource());

            Assert.That(result, Is.Not.Null);
            Assert.That(AlphaBetaSearch.PackMove(result.Value), Is.EqualTo(AlphaBetaSearch.PackMove(e2e4)));
        }

        [Test]
        public void TryGetBookMove_PositionNotInBook_ReturnsNull()
        {
            OpeningBookAsset book = AssetFrom("e2e4 e7e5");
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            board.CurrentTurn = Team.Black; // arbitrary mutation so the hash no longer matches any book entry
            board.ComputeFullZobristHash();

            MoveCommand? result = OpeningBookLookup.TryGetBookMove(book, board, _engine, new ZeroRandomSource());

            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryGetBookMove_EmptyBook_ReturnsNullWithoutTouchingEngine()
        {
            var asset = ScriptableObject.CreateInstance<OpeningBookAsset>();
            asset.SetEntries(System.Array.Empty<ulong>(), System.Array.Empty<uint>(), System.Array.Empty<ushort>(), BoardState.ZobristSchemeVersion);
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            MoveCommand? result = OpeningBookLookup.TryGetBookMove(asset, board, _engine, new ZeroRandomSource());

            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryGetBookMove_SchemeVersionMismatch_ReturnsNull()
        {
            OpeningBookAsset book = AssetFrom("e2e4 e7e5");
            var mismatched = ScriptableObject.CreateInstance<OpeningBookAsset>();
            var keys = new ulong[book.EntryCount];
            var moves = new uint[book.EntryCount];
            var weights = new ushort[book.EntryCount];
            for (int i = 0; i < book.EntryCount; i++)
            {
                keys[i] = book.KeyAt(i);
                moves[i] = book.PackedMoveAt(i);
                weights[i] = book.WeightAt(i);
            }
            mismatched.SetEntries(keys, moves, weights, schemeVersion: book.SchemeVersion ^ 0xFFFFFFFFFFFFFFFFUL);

            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            MoveCommand? result = OpeningBookLookup.TryGetBookMove(mismatched, board, _engine, new ZeroRandomSource());

            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryGetBookMove_BetrayalSequenceInProgress_ReturnsNull()
        {
            // A hand-built position where a Betrayal Act is already pending — the book must never
            // answer here, regardless of whether the underlying hash happens to collide with an
            // entry, since it only ever covers plain opening theory (see OpeningBookCompiler's own
            // rejection of Act moves at compile time).
            OpeningBookAsset book = AssetFrom("e2e4 e7e5");
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();
            board.BetrayalInitiator = Team.White;
            board.PendingBetrayerSquare = new Vector2Int(3, 1);

            MoveCommand? result = OpeningBookLookup.TryGetBookMove(book, board, _engine, new ZeroRandomSource());

            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryGetBookMove_MultipleTransposingLines_WeightedPickHonorsRng()
        {
            // Two lines share the same position after 1.e4 c5 but recommend different replies —
            // 2.Nf3 (weight 3) and 2.Nc3 (weight 2) — so the run at that hash has two candidates.
            // A zero-roll RNG must always land on the first candidate in weighted order; a
            // max-roll RNG must always land on the last.
            OpeningBookAsset book = AssetFrom("e2e4 c7c5 g1f3 | w=3\ne2e4 c7c5 b1c3 | w=2");

            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            PlayToken(board, "e2e4");
            PlayToken(board, "c7c5");

            MoveCommand? lowRollResult = OpeningBookLookup.TryGetBookMove(book, board, _engine, new ZeroRandomSource());
            MoveCommand? highRollResult = OpeningBookLookup.TryGetBookMove(book, board, _engine, new MaxRandomSource());

            Assert.That(lowRollResult, Is.Not.Null);
            Assert.That(highRollResult, Is.Not.Null);
            Assert.That(AlphaBetaSearch.PackMove(lowRollResult.Value), Is.Not.EqualTo(AlphaBetaSearch.PackMove(highRollResult.Value)),
                "A zero-roll and a max-roll RNG must land on different candidates when a run has more than one entry.");
        }

        private void PlayToken(BoardState board, string token)
        {
            OpeningBookLine line = OpeningBookLine.Parse(token, sourceLineNumber: 1);
            OpeningBookCompiler.ReplayLine(line, _engine, board).ToList();
        }
    }
}
