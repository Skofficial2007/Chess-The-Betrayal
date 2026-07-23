using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// BetrayalAwareEvaluator scales its non-material terms through an EvaluationWeights struct.
    /// This is the safety net proving that default/identity weights (every tier except Aggressive
    /// and Extreme, since their AttackDefenseBias == 1 and BetrayalAggression == 0) score
    /// bit-identically to an unweighted evaluator — weighting must be genuinely inert at identity,
    /// not merely close. Reuses EvaluatorTests.cs's exact positions plus one shelter-pawn position.
    ///
    /// The golden-score assertions below exist for a different, stricter reason: comparing three
    /// constructions of the evaluator to each other (as the fixture above already does) cannot
    /// catch a change to the scoring itself, since every construction would drift together. Any
    /// evaluator rework — a piece-square blend, a new term, anything that changes what a position
    /// is worth — must hold these exact numbers unless the position sits genuinely closer to the
    /// endgame, which none of these do. FullMaterialAsymmetricOpening exists specifically because
    /// the other four fixtures are all sparse boards that would already read close to the endgame
    /// end of any phase scale, which would let a scoring change hide there undetected.
    /// </summary>
    [TestFixture]
    public class EvaluatorWeightsRegressionTests
    {
        private static BoardState SymmetricQueens() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen);

        private static BoardState MirroredRookKnight() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("a4", Team.White, ChessPieceType.Knight);

        private static BoardState ExtraQueen() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen);

        private static BoardState ShelteredKing() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("e1", Team.White, ChessPieceType.King)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e2", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn);

        /// <summary>
        /// Exactly full non-pawn material on both sides (identical piece COUNTS, both full
        /// opening sets — 2 rooks/2 knights/2 bishops/1 queen each), so this genuinely sits at
        /// MaterialPhase.FullPhaseWeight and is a real full-phase pin, not merely close to one. The
        /// asymmetry (one White knight developed to g5 instead of its home square, both sides
        /// pushed their e-pawn) keeps the score a real, table-value-sensitive number rather than
        /// cancelling out by symmetry the way SymmetricQueens and a mirrored full setup would. This
        /// is the fixture a tapered evaluator has the most room to get wrong, since it is the one
        /// closest to what an opening move actually looks like.
        /// </summary>
        private static BoardState FullMaterialAsymmetricOpening() =>
            TestBoardSetupUtility.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.Rook)
                .WithPiece("b1", Team.White, ChessPieceType.Knight)
                .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("f1", Team.White, ChessPieceType.Bishop)
                .WithPiece("g5", Team.White, ChessPieceType.Knight)
                .WithPiece("h1", Team.White, ChessPieceType.Rook)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a8", Team.Black, ChessPieceType.Rook)
                .WithPiece("b8", Team.Black, ChessPieceType.Knight)
                .WithPiece("c8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d8", Team.Black, ChessPieceType.Queen)
                .WithPiece("e8", Team.Black, ChessPieceType.King)
                .WithPiece("f8", Team.Black, ChessPieceType.Bishop)
                .WithPiece("g8", Team.Black, ChessPieceType.Knight)
                .WithPiece("h8", Team.Black, ChessPieceType.Rook)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("d7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("e5", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn);

        private static void AssertIdenticalAcrossConstructions(BoardState board, Team perspective)
        {
            var bareCtor = new BetrayalAwareEvaluator();
            var explicitIdentity = new BetrayalAwareEvaluator(new EvaluationWeights(1f, 1f, 1f));
            var fromNormalProfile = new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(new AIProfileTableProvider().Resolve("normal"))); // bias=1, aggression=0

            int a = bareCtor.Evaluate(board, perspective);
            int b = explicitIdentity.Evaluate(board, perspective);
            int c = fromNormalProfile.Evaluate(board, perspective);

            Assert.That(b, Is.EqualTo(a), "Explicit identity weights must match the bare (no-arg) constructor exactly.");
            Assert.That(c, Is.EqualTo(a), "The 'normal' profile (bias=1, aggression=0) must map to identity weights exactly.");
        }

        [Test]
        public void SymmetricQueens_IdenticalAcrossConstructions()
        {
            BoardState board = SymmetricQueens();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
            Assert.That(new BetrayalAwareEvaluator().Evaluate(board, Team.White), Is.EqualTo(0));
        }

        [Test]
        public void MirroredRookKnight_IdenticalAcrossConstructions()
        {
            BoardState board = MirroredRookKnight();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
        }

        [Test]
        public void ExtraQueen_IdenticalAcrossConstructions()
        {
            BoardState board = ExtraQueen();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
            Assert.That(new BetrayalAwareEvaluator().Evaluate(board, Team.White), Is.GreaterThan(900));
            Assert.That(new BetrayalAwareEvaluator().Evaluate(board, Team.Black), Is.LessThan(-900));
        }

        [Test]
        public void ShelteredKing_IdenticalAcrossConstructions()
        {
            BoardState board = ShelteredKing();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
        }

        [Test]
        public void FullMaterialAsymmetricOpening_IdenticalAcrossConstructions()
        {
            BoardState board = FullMaterialAsymmetricOpening();
            AssertIdenticalAcrossConstructions(board, Team.White);
            AssertIdenticalAcrossConstructions(board, Team.Black);
        }

        /// <summary>
        /// Exact scores captured from today's evaluator, before any piece-square-table reshaping.
        /// Unlike every test above (which only proves the three constructions agree with EACH
        /// OTHER, so a scoring change would move all three together and slip past undetected),
        /// these pin the actual number. A future change to piece-square values, material, or how
        /// they combine must reproduce these exactly for a position at full material — if a
        /// legitimate change needs to move one of these, that is the signal to capture a fresh
        /// baseline deliberately, not to adjust the number quietly.
        /// </summary>
        [Test]
        public void GoldenScores_AtFullMaterial_MatchCapturedBaseline()
        {
            var evaluator = new BetrayalAwareEvaluator();

            Assert.That(evaluator.Evaluate(SymmetricQueens(), Team.White), Is.EqualTo(0));
            Assert.That(evaluator.Evaluate(SymmetricQueens(), Team.Black), Is.EqualTo(0));

            Assert.That(evaluator.Evaluate(MirroredRookKnight(), Team.White), Is.EqualTo(795));
            Assert.That(evaluator.Evaluate(MirroredRookKnight(), Team.Black), Is.EqualTo(-795));

            Assert.That(evaluator.Evaluate(ExtraQueen(), Team.White), Is.EqualTo(970));
            Assert.That(evaluator.Evaluate(ExtraQueen(), Team.Black), Is.EqualTo(-970));

            // Rose from 300 to 336 when king safety landed (AI-53): Black's bare king has all three
            // of its files fully open (no Black pawns anywhere), while White's three sheltering pawns
            // also close White's own king's files, so only Black's exposure is penalized (+36).
            Assert.That(evaluator.Evaluate(ShelteredKing(), Team.White), Is.EqualTo(336));
            Assert.That(evaluator.Evaluate(ShelteredKing(), Team.Black), Is.EqualTo(-336));

            Assert.That(evaluator.Evaluate(FullMaterialAsymmetricOpening(), Team.White), Is.EqualTo(85));
            Assert.That(evaluator.Evaluate(FullMaterialAsymmetricOpening(), Team.Black), Is.EqualTo(-85));
        }

        /// <summary>
        /// EvaluateCheap exists as a home for a lazy caller to skip whatever full-only terms exist.
        /// The three PAWNLESS golden fixtures have nothing for a pawn term to add, so cheap and full
        /// must match EXACTLY on these regardless of what full-only terms exist now or later — this
        /// is the identity the lazy-evaluation stand-pat cut leans on for any position with no pawns.
        /// </summary>
        [Test]
        public void EvaluateCheap_MatchesFull_OnEveryPawnlessGoldenFixture()
        {
            var evaluator = new BetrayalAwareEvaluator();

            foreach (BoardState board in new[] { SymmetricQueens(), MirroredRookKnight(), ExtraQueen() })
            {
                Assert.That(evaluator.EvaluateCheap(board, Team.White), Is.EqualTo(evaluator.Evaluate(board, Team.White)));
                Assert.That(evaluator.EvaluateCheap(board, Team.Black), Is.EqualTo(evaluator.Evaluate(board, Team.Black)));
            }
        }

        /// <summary>
        /// FullMaterialAsymmetricOpening still lands at a zero pawn-structure AND zero king-safety
        /// delta today (pawn-symmetric except for one isolated pawn on each side that exactly cancels;
        /// neither king has an open file or an enemy piece in its zone). That is a property of this
        /// SPECIFIC board, not a guarantee — this test pins it explicitly so a future change to the
        /// fixture's pawns or king exposure can't silently start diverging unnoticed.
        /// </summary>
        [Test]
        public void EvaluateCheap_MatchesFull_OnFullMaterialAsymmetricOpeningToday()
        {
            var evaluator = new BetrayalAwareEvaluator();
            BoardState board = FullMaterialAsymmetricOpening();

            Assert.That(evaluator.EvaluateCheap(board, Team.White), Is.EqualTo(evaluator.Evaluate(board, Team.White)));
            Assert.That(evaluator.EvaluateCheap(board, Team.Black), Is.EqualTo(evaluator.Evaluate(board, Team.Black)));
        }

        /// <summary>
        /// ShelteredKing now diverges cheap != full by design: its zero pawn-structure delta (three
        /// pawns all on their home rank, before the passed-pawn bonus ramps up) is unchanged from
        /// AI-52, but AI-53's king-safety term adds a real, nonzero open-file penalty for Black's
        /// completely bare king that White's own sheltering pawns spare White from. This pins that the
        /// full-minus-cheap gap on this board is EXACTLY the king-safety contribution (36 from White's
        /// perspective) and nothing else — proving the full path adds precisely what the two new terms
        /// are supposed to add, not something else leaking in.
        /// </summary>
        [Test]
        public void EvaluateCheap_MatchesFull_PlusKingSafety_OnShelteredKing()
        {
            var evaluator = new BetrayalAwareEvaluator();
            BoardState board = ShelteredKing();

            int cheapWhite = evaluator.EvaluateCheap(board, Team.White);
            int fullWhite = evaluator.Evaluate(board, Team.White);
            int cheapBlack = evaluator.EvaluateCheap(board, Team.Black);
            int fullBlack = evaluator.Evaluate(board, Team.Black);

            Assert.That(fullWhite - cheapWhite, Is.EqualTo(36));
            Assert.That(fullBlack - cheapBlack, Is.EqualTo(-36));
        }
    }
}
