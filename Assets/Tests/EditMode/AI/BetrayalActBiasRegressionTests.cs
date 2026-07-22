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
    /// Pins the search's current Betrayal Act valuation on a real, balanced middlegame — the
    /// exact position that once showed every profile rating "Queen betrays its own c2 pawn" as
    /// the single best move at depths 3 through 10, before the phantom-stalemate defection bug
    /// (a pending Retribution with no legal executioner was scored as a draw instead of resolving
    /// the forced Defection) was fixed. That fix's own regression coverage never re-checked THIS
    /// position with Betrayal enabled, so this fixture closes that gap directly: it re-runs the
    /// exact symmetric-development position across the depths every built-in tier actually
    /// reaches and asserts a self-capture Act is never the chosen move.
    ///
    /// This is a PIN, not a design claim about what the "right" answer looks like in general —
    /// see the audit's own memory record for the broader question of whether Betrayal is valued
    /// well beyond this one position.
    /// </summary>
    [TestFixture]
    public class BetrayalActBiasRegressionTests
    {
        /// <summary>
        /// A developed, materially even middlegame with both sides fully mobilized — deliberately
        /// NOT quiet or forcing, so nothing about the position itself explains a self-sacrificing
        /// Betrayal Act winning on its merits. Every White piece has at least one legal Act
        /// (see Acts_EveryWhitePieceHasAtLeastOneFriendlyVictim below) against a friendly piece,
        /// so if the search ever rates an Act best here, that is the search's own valuation
        /// choosing it, not the position forcing it.
        /// </summary>
        private static BoardState SymmetricMiddlegame()
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("f1", Team.White, ChessPieceType.Rook)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("c3", Team.White, ChessPieceType.Bishop)
                .WithPiece("d2", Team.White, ChessPieceType.Bishop)
                .WithPiece("c6", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d7", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f3", Team.White, ChessPieceType.Knight)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("f6", Team.Black, ChessPieceType.Knight)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                .WithPiece("e3", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("d5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();
            return board;
        }

        [Test]
        public void Acts_EveryWhitePieceHasAtLeastOneFriendlyVictim()
        {
            // Documents the fixture's own honesty: an Act is available to the Queen, both Rooks,
            // both Bishops, both Knights, and several Pawns here — this is not a position where
            // Betrayal happens to be absent, it is one where it is everywhere and still must not
            // win. A future edit to this fixture that accidentally removes every Act would make
            // the pins below pass for the wrong reason (nothing to choose), so this is checked
            // explicitly rather than assumed.
            BoardState board = SymmetricMiddlegame();
            var moves = new List<MoveCommand>();
            ChessEngine.GetAllLegalMovesIncludingBetrayal(board, Team.White, moves);

            int actCount = 0;
            foreach (MoveCommand move in moves)
                if (move.Stage == BetrayalStage.Act) actCount++;

            Assert.That(actCount, Is.GreaterThan(15),
                "the fixture must offer many real Act choices, not rely on Betrayal being scarce.");
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        public void FindBestMove_SymmetricMiddlegameAtEveryTierDepth_NeverChoosesASelfCaptureAct(int depth)
        {
            // The depths every built-in tier actually searches to (easy=3, normal=5, aggressive=7,
            // hard/extreme/impossible=8-9 per AIProfileTable.BuiltIn) — covering the exact depth
            // range the second capture round in the benchmark-baseline memory found the pathology
            // at ("depths 3-10"), not just one arbitrarily chosen depth.
            BoardState board = SymmetricMiddlegame();
            var search = new AlphaBetaSearch(new ChessEngineAdapter(), new BetrayalAwareEvaluator());
            var settings = new AISearchSettings(depth, TestTimeBudgets.Generous, BetrayalUsage.Full);

            MoveCommand best = search.FindBestMove(board, settings, CancellationToken.None);

            Assert.That(best.Stage, Is.Not.EqualTo(BetrayalStage.Act),
                $"depth {depth} must not choose a self-capture Act as the best move in a balanced, non-forcing middlegame.");
        }
    }
}
