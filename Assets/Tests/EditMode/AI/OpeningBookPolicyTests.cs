using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.EditorTools.OpeningBook;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the rule that decides how long a difficulty tier keeps answering from the opening book.
    ///
    /// The interesting cases are all boundaries: the exact ply a capped tier stops trusting the
    /// book, that the count means the same thing for White and for Black, and that it follows the
    /// board rather than a private tally — the last one is why taking a move back also takes the
    /// AI back into its repertoire instead of leaving it stranded outside.
    /// </summary>
    [TestFixture]
    public class OpeningBookPolicyTests
    {
        private ChessEngineAdapter _engine;
        private TurnResolver _resolver;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _resolver = new TurnResolver();
        }

        private static AIProfile Profile(bool useOpeningBook, int openingBookDepthPlies) =>
            new AIProfile("test", maxDepth: 5, timeBudget: new AITimeBudget(1000, 1500),
                blunderRate: 0f, blunderMarginCp: 0, betrayalAggression: 0f, attackDefenseBias: 1f,
                tieBreakWindowCp: 0, useOpeningBook: useOpeningBook,
                openingBookDepthPlies: openingBookDepthPlies);

        /// <summary>
        /// Plays <paramref name="plies"/> ordinary moves from the standard start, always taking the
        /// first legal non-Betrayal move so the resulting position is arbitrary but the ply count is
        /// exact. Moves go through the turn resolver rather than the raw engine so the turn actually
        /// passes and the board records the move the way a real game does.
        /// </summary>
        private BoardState BoardAfterPlies(int plies)
        {
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();
            var legalMoves = new List<MoveCommand>();

            for (int ply = 0; ply < plies; ply++)
            {
                legalMoves.Clear();
                _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

                MoveCommand? plain = null;
                foreach (MoveCommand candidate in legalMoves)
                {
                    if (candidate.Stage == BetrayalStage.None) { plain = candidate; break; }
                }

                Assert.That(plain, Is.Not.Null, $"No ordinary move available at ply {ply}.");
                _resolver.Advance(board, plain.Value);
            }

            return board;
        }

        [Test]
        public void ShouldConsult_ProfileThatOptedOut_IsFalseFromTheVeryFirstMove()
        {
            BoardState start = BoardAfterPlies(0);

            Assert.That(OpeningBookPolicy.ShouldConsult(Profile(useOpeningBook: false, 0), start), Is.False);
            Assert.That(OpeningBookPolicy.ShouldConsult(Profile(useOpeningBook: false, 20), start), Is.False,
                "A depth allowance must not resurrect the book for a tier that opted out of it entirely.");
        }

        [Test]
        public void ShouldConsult_NoDepthLimit_StaysTrueFarBeyondAnyBookLine()
        {
            // 40 plies is past the longest line the shipped book holds, so this is asking whether
            // the policy itself ever stops — the lookup declining on its own is a separate matter.
            Assert.That(OpeningBookPolicy.ShouldConsult(Profile(true, 0), BoardAfterPlies(40)), Is.True);
        }

        [Test]
        public void ShouldConsult_CappedTier_SwitchesOffExactlyAtTheCap()
        {
            AIProfile capped = Profile(useOpeningBook: true, openingBookDepthPlies: 4);

            Assert.That(OpeningBookPolicy.ShouldConsult(capped, BoardAfterPlies(3)), Is.True,
                "One ply short of the cap the tier is still inside its repertoire.");
            Assert.That(OpeningBookPolicy.ShouldConsult(capped, BoardAfterPlies(4)), Is.False,
                "A cap of 4 plies means 4 plies have been played and the book is done, not that a 5th is allowed.");
            Assert.That(OpeningBookPolicy.ShouldConsult(capped, BoardAfterPlies(5)), Is.False);
        }

        [Test]
        public void ShouldConsult_CappedTier_CountsGamePlies_SoBothColoursGetTheSameAllowance()
        {
            AIProfile capped = Profile(useOpeningBook: true, openingBookDepthPlies: 4);

            // Plies 0 and 2 are White's first two moves; 1 and 3 are Black's. All four are inside a
            // 4-ply cap, so whichever colour the AI has it plays exactly two moves from the book.
            for (int ply = 0; ply < 4; ply++)
            {
                Assert.That(OpeningBookPolicy.ShouldConsult(capped, BoardAfterPlies(ply)), Is.True,
                    $"Ply {ply} is inside a 4-ply cap regardless of whose move it is.");
            }
        }

        [Test]
        public void ShouldConsult_AfterAMoveIsTakenBack_TheBookComesBack()
        {
            // The whole reason the count is read off the board instead of tallied by the agent: an
            // undo has to put the AI back where it was. A private counter would keep running and
            // leave the AI searching in a position it had been answering from memory a moment ago.
            AIProfile capped = Profile(useOpeningBook: true, openingBookDepthPlies: 4);

            BoardState board = BoardAfterPlies(3);
            var legalMoves = new List<MoveCommand>();
            _engine.GetAllLegalMovesIncludingBetrayal(board, board.CurrentTurn, legalMoves);

            MoveCommand fourth = legalMoves[0];
            _engine.ApplyMove(board, fourth);
            Assert.That(OpeningBookPolicy.ShouldConsult(capped, board), Is.False,
                "Four plies in, the cap has been reached.");

            _engine.UndoMove(board, fourth);
            Assert.That(OpeningBookPolicy.ShouldConsult(capped, board), Is.True,
                "Undoing the fourth ply must put the tier back inside its repertoire.");
        }

        [Test]
        public void ShouldConsult_NoBoard_IsFalseRatherThanThrowing()
        {
            Assert.That(OpeningBookPolicy.ShouldConsult(Profile(true, 0), null), Is.False);
        }
    }
}
