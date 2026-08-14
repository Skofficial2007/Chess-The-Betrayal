using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Utils;
using ChessTheBetrayal.EditorTools.OpeningBook;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tests.EditMode.AI.OpeningBook
{
    /// <summary>
    /// Checks the positions the shipped book hands over to the search when its theory runs out.
    ///
    /// The other book fixtures check that the book is legal, fresh, varied and stops cleanly. None
    /// of them would notice a line that is all of those things and still terrible — a losing gambit
    /// typed in from a source that recommended it for the other colour, say. That failure is
    /// invisible until someone watches the AI emerge from a "known" opening already lost.
    ///
    /// WHAT THIS IS NOT: it is not evidence the book makes the AI stronger, and it cannot be. The
    /// positions are scored by the same evaluator that plays the game, so this can only ever say
    /// that the book does not hand the search a position the engine itself considers lost. That is
    /// a narrower claim than "good opening theory", but it is the one that maps onto what a player
    /// would actually see, and it costs milliseconds to check. Measuring whether the book adds
    /// playing strength would need a harness that plays whole games through the real agent, which
    /// does not exist — the tournament harness drives the search directly and never opens a book.
    /// </summary>
    [TestFixture]
    public class OpeningBookExitBalanceTests
    {
        private const string CompiledAssetPath = OpeningBookBuilder.DefaultAssetPath;
        private const int SeedCount = 60;
        private const int WalkSafetyLimit = 200;

        /// <summary>
        /// The widest score, in centipawns, any opening in the book may leave behind — the same 300
        /// the match harness treats as a decided game.
        ///
        /// Chosen after measuring rather than in advance. Across the openings sampled here the
        /// spread runs from -93 to 205 with a median of 70, so this leaves real headroom instead of
        /// sitting flush against the current book and failing the next time a line is added. It is
        /// deliberately generous: theory genuinely does end in positions one side prefers, and a
        /// gambit is supposed to be down material for a while. The claim is "no opening the book
        /// knows walks the AI into a lost game", not "every opening comes out level".
        /// </summary>
        private const int MaxAcceptableExitScoreCp = 300;

        private ChessEngineAdapter _engine;
        private OpeningBookAsset _book;
        private IPositionEvaluator _evaluator;

        [SetUp]
        public void Setup()
        {
            _engine = new ChessEngineAdapter();
            _book = AssetDatabase.LoadAssetAtPath<OpeningBookAsset>(CompiledAssetPath);
            Assert.That(_book, Is.Not.Null, $"No compiled opening book found at '{CompiledAssetPath}'.");

            // The neutral tier's weights: no Betrayal aggression, no attack/defense bias. A biased
            // evaluator would rate an opening by how well it suits one tier's personality, which is
            // not what is being asked here.
            AIProfile neutral = AIProfileTable.BuiltIn[5];
            _evaluator = new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(neutral));
        }

        /// <summary>
        /// Follows the book from the starting position until it stops answering and returns the
        /// position it left behind, along with how many plies of theory it played to get there.
        /// </summary>
        private (BoardState Board, int Plies) WalkToExit(IRandomSource rng)
        {
            BoardState board = OpeningBookCompiler.CreateStandardStartingPosition();

            for (int ply = 0; ply < WalkSafetyLimit; ply++)
            {
                MoveCommand? bookMove = OpeningBookLookup.TryGetBookMove(_book, board, _engine, rng);
                if (bookMove == null) return (board, ply);

                new TurnResolver().Advance(board, bookMove.Value);
            }

            Assert.Fail($"The book never stopped answering within {WalkSafetyLimit} plies.");
            return (board, 0);
        }

        [Test]
        public void ShippedBook_NoOpeningItKnows_HandsTheSearchAPositionItAlreadyConsidersLost()
        {
            var scores = new List<int>(SeedCount);
            int worstScore = 0;
            int worstSeed = -1;
            int worstPlies = 0;

            for (int seed = 0; seed < SeedCount; seed++)
            {
                (BoardState board, int plies) = WalkToExit(new SystemRandomSource(seed));

                // Scored for whoever is about to move, since that is the side the search takes
                // over for. A book that reliably left the side to move in trouble would be a book
                // that plays one colour's openings well and the other colour's badly.
                int score = _evaluator.Evaluate(board, board.CurrentTurn);
                scores.Add(score);

                if (System.Math.Abs(score) > System.Math.Abs(worstScore))
                {
                    worstScore = score;
                    worstSeed = seed;
                    worstPlies = plies;
                }
            }

            scores.Sort();
            TestContext.Out.WriteLine(
                $"Book exit scores over {SeedCount} openings (centipawns, from the mover's side): " +
                $"min {scores[0]}, p25 {scores[SeedCount / 4]}, median {scores[SeedCount / 2]}, " +
                $"p75 {scores[3 * SeedCount / 4]}, max {scores[SeedCount - 1]}. " +
                $"Widest was {worstScore} on seed {worstSeed} after {worstPlies} plies of theory.");

            Assert.That(System.Math.Abs(worstScore), Is.LessThanOrEqualTo(MaxAcceptableExitScoreCp),
                $"An opening in the book (seed {worstSeed}, {worstPlies} plies) ends at {worstScore}cp, past the " +
                $"{MaxAcceptableExitScoreCp}cp the match harness treats as decided. Either that line is wrong or it " +
                "is a gambit deep enough to need its own justification.");
        }

        [Test]
        public void ShippedBook_TheOpeningsItPlays_AreBalancedOnAverageRatherThanFavouringOneColour()
        {
            // A book whose lines each stay inside the margin above could still lean the same way
            // every time — every White line comfortable, every Black line slightly worse. That
            // reads as the AI being weaker with Black, which is a book problem wearing an engine
            // problem's clothes, so it is worth separating out.
            long whiteTotal = 0, blackTotal = 0;
            int whiteCount = 0, blackCount = 0;

            for (int seed = 0; seed < SeedCount; seed++)
            {
                (BoardState board, _) = WalkToExit(new SystemRandomSource(seed));
                int scoreForWhite = _evaluator.Evaluate(board, Team.White);

                if (board.CurrentTurn == Team.White) { whiteTotal += scoreForWhite; whiteCount++; }
                else { blackTotal += scoreForWhite; blackCount++; }
            }

            double whiteMean = whiteCount == 0 ? 0 : (double)whiteTotal / whiteCount;
            double blackMean = blackCount == 0 ? 0 : (double)blackTotal / blackCount;

            TestContext.Out.WriteLine(
                $"Mean exit score from White's side: {whiteMean:F1}cp over {whiteCount} openings ending on White's " +
                $"move, {blackMean:F1}cp over {blackCount} ending on Black's.");

            Assert.That(System.Math.Abs(whiteMean), Is.LessThanOrEqualTo(MaxAcceptableExitScoreCp),
                "On average the book leaves White somewhere it should not.");
            Assert.That(System.Math.Abs(blackMean), Is.LessThanOrEqualTo(MaxAcceptableExitScoreCp),
                "On average the book leaves Black somewhere it should not.");
        }
    }
}
