using ChessTheBetrayal.View.Camera;
using NUnit.Framework;

namespace ChessTheBetrayal.Tests.EditMode.View.Camera
{
    /// <summary>
    /// Which lens the board is framed with, and on what.
    ///
    /// Worth testing away from the scene because the interesting case cannot be produced in one:
    /// the editor never reports itself as a handheld, so the branch that matters on every phone is
    /// the branch a desktop play session can never enter.
    /// </summary>
    [TestFixture]
    public class BoardFramingPolicyTests
    {
        private const float AuthoredFocalLengthMm = 20.78461f;

        [Test]
        public void AHandheldGetsTheWiderLens()
        {
            Assert.That(
                BoardFramingPolicy.FocalLengthMmFor(isHandheld: true, AuthoredFocalLengthMm),
                Is.EqualTo(BoardFramingPolicy.HandheldFocalLengthMm));
        }

        [Test]
        public void AnythingElseKeepsWhateverTheSceneWasAuthoredWith()
        {
            Assert.That(
                BoardFramingPolicy.FocalLengthMmFor(isHandheld: false, AuthoredFocalLengthMm),
                Is.EqualTo(AuthoredFocalLengthMm),
                "A lens tuned in the editor has to survive this untouched, or every desktop " +
                "adjustment would be silently undone at startup.");
        }

        [Test]
        public void TheHandheldLensIsWiderThanTheDesktopOne()
        {
            Assert.That(
                BoardFramingPolicy.HandheldFocalLengthMm,
                Is.LessThan(AuthoredFocalLengthMm),
                "Fitting more of the board on a narrower screen means a shorter focal length. " +
                "A longer one would crop harder, which is the problem rather than the fix.");
        }
    }
}
