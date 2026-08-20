using NUnit.Framework;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.EditMode.Core.Data
{
    /// <summary>
    /// The one place a square turns into coordinates and back. It exists because four callers had
    /// each written the arithmetic themselves and the copies no longer agreed — one lower-cased the
    /// file letter and another did not, one refused a square off the board and another returned it.
    /// What is pinned here is the agreement those callers now inherit.
    /// </summary>
    [TestFixture]
    public class SquareNotationTests
    {
        [TestCase("a1", 0, 0)]
        [TestCase("h8", 7, 7)]
        [TestCase("e4", 4, 3)]
        [TestCase("d7", 3, 6)]
        public void A_square_reads_as_its_file_and_rank(string algebraic, int x, int y)
        {
            Assert.That(SquareNotation.TryParse(algebraic, out Vector2Int square), Is.True);
            Assert.That(square.x, Is.EqualTo(x));
            Assert.That(square.y, Is.EqualTo(y));
        }

        [TestCase("E4")]
        [TestCase("A1")]
        [TestCase("H8")]
        public void An_upper_case_file_reads_the_same_as_a_lower_case_one(string algebraic)
        {
            Assert.That(SquareNotation.TryParse(algebraic, out Vector2Int upper), Is.True);
            SquareNotation.TryParse(algebraic.ToLowerInvariant(), out Vector2Int lower);

            Assert.That(upper, Is.EqualTo(lower), "the file letter is notation, not prose");
        }

        [TestCase("i1", TestName = "file past h")]
        [TestCase("a9", TestName = "rank past 8")]
        [TestCase("a0", TestName = "rank before 1")]
        [TestCase("`1", TestName = "file before a")]
        public void A_square_off_the_board_is_refused(string algebraic)
        {
            Assert.That(SquareNotation.TryParse(algebraic, out _), Is.False);
        }

        [TestCase("")]
        [TestCase("e")]
        [TestCase("e44")]
        [TestCase(null)]
        public void Anything_that_is_not_two_characters_is_refused(string algebraic)
        {
            Assert.That(SquareNotation.TryParse(algebraic, out _), Is.False);
        }

        [Test]
        public void A_refused_square_hands_back_no_coordinate()
        {
            SquareNotation.TryParse("z9", out Vector2Int square);

            Assert.That(square, Is.EqualTo(default(Vector2Int)),
                "a caller that ignores the false must not get a plausible-looking square");
        }

        [TestCase(0, 0, "a1")]
        [TestCase(7, 7, "h8")]
        [TestCase(4, 3, "e4")]
        public void A_square_writes_back_the_way_it_was_read(int x, int y, string expected)
        {
            Assert.That(SquareNotation.ToAlgebraic(new Vector2Int(x, y)), Is.EqualTo(expected));
        }

        [TestCase("a1")]
        [TestCase("h8")]
        [TestCase("c6")]
        public void Reading_then_writing_returns_the_original(string algebraic)
        {
            SquareNotation.TryParse(algebraic, out Vector2Int square);

            Assert.That(SquareNotation.ToAlgebraic(square), Is.EqualTo(algebraic));
        }

        [Test]
        public void A_square_split_into_characters_reads_the_same_as_a_string()
        {
            SquareNotation.TryParse("f5", out Vector2Int fromString);

            Assert.That(SquareNotation.TryParse('f', '5', out Vector2Int fromChars), Is.True);
            Assert.That(fromChars, Is.EqualTo(fromString));
        }
    }
}
