using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The thirteenth capture round in the profile-benchmark baseline showed aggressive reaching a
    /// minimum completed depth of 1, and several other tiers a minimum of 2, in real games. The
    /// standing hypothesis was that these are forced positions (few or one legal reply, so the
    /// search returns almost instantly regardless of configured depth) rather than a search bug —
    /// never actually verified. This pins the mechanism directly: a position with exactly one
    /// legal reply completes EVERY configured depth almost instantly (not just depth 1), because a
    /// branching factor of 1 keeps the whole tree tiny at every ply. If a future capture round ever
    /// shows a shallow completed depth on a position that is NOT this constrained, that stops being
    /// explainable by this mechanism and becomes a real bug worth its own investigation.
    /// </summary>
    [TestFixture]
    public class ForcedPositionDepthTests
    {
        private ChessEngineAdapter _engine;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
        }

        private static BoardState SingleLegalReplyPosition()
        {
            // White king on a1 in check from a Black rook on a8 along the a-file. Of the king's
            // three adjacent non-a-file squares (b1, b2 — a2 stays on the checking file), b2 is
            // additionally covered by a second Black rook on h2, leaving b1 as the ONLY legal move.
            return TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("h2", Team.Black, ChessPieceType.Rook)
                .WithPiece("h8", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithComputedHash();
        }

        [Test]
        public void SingleLegalReplyPosition_HasExactlyOneLegalMove()
        {
            BoardState board = SingleLegalReplyPosition();
            var moves = new List<MoveCommand>();
            ChessEngine.GetAllLegalMovesIncludingBetrayal(board, Team.White, moves);

            Assert.That(moves, Has.Count.EqualTo(1),
                "fixture must genuinely be forced — this test is meaningless otherwise.");
        }

        [Test]
        public void ForcedPosition_UnderARealTierBudget_CompletesToFullConfiguredDepthNotJustDepthOne()
        {
            // A genuinely forced position does NOT explain a shallow completed depth on its own —
            // with only one legal move at every node the tree stays tiny at every depth, so the
            // search should sail through to the tier's full MaxDepth well inside its soft budget,
            // not stall at depth 1 or 2. This is the mechanism check the depth-min caveat needed:
            // if forced positions behaved the OTHER way (stalling shallow), that would BE the bug.
            AIProfile aggressive = FindProfile("aggressive");
            BoardState board = SingleLegalReplyPosition();

            var search = new AlphaBetaSearch(_engine, new BetrayalAwareEvaluator(),
                transpositionTable: new TranspositionTable(log2Size: 16));
            var settings = new AISearchSettings(aggressive.MaxDepth, aggressive.TimeBudget, BetrayalUsage.Full);

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(aggressive.TimeBudget.HardMs);
            search.FindBestMove(board, settings, cts.Token, enableInstabilityTimeManagement: true);

            Assert.That(search.Stats.LastCompletedDepth, Is.EqualTo(aggressive.MaxDepth),
                "a single-legal-move position should reach the tier's full configured depth, not stall shallow.");
        }

        private static AIProfile FindProfile(string id)
        {
            foreach (AIProfile profile in AIProfileTable.BuiltIn)
                if (profile.Id == id) return profile;
            Assert.Fail($"No built-in profile named '{id}'.");
            return default;
        }
    }
}
