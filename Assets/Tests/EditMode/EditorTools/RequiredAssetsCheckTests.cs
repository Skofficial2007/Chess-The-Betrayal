using ChessTheBetrayal.EditorTools;
using NUnit.Framework;

namespace ChessTheBetrayal.Tests.EditMode.EditorTools
{
    /// <summary>
    /// Who gets told about the missing Asset Store packages.
    ///
    /// The decision is tested rather than the callback because the case that matters most cannot
    /// be produced from inside a running editor: the art is absent and there is no screen, which
    /// is every automated checkout and no developer's machine.
    /// </summary>
    [TestFixture]
    public class RequiredAssetsCheckTests
    {
        [Test]
        public void AMachineRunningWithoutAScreen_IsNeverAsked()
        {
            Assert.That(
                RequiredAssetsCheck.ShouldPrompt(isBatchMode: true, alreadyAskedThisSession: false, missingCount: 3),
                Is.False,
                "The art cannot be in the repository, so it is missing on every build agent by design. " +
                "Opening a window there fails outright rather than being ignored.");
        }

        [Test]
        public void SomebodyWhoseCheckoutIsIncomplete_IsTold()
        {
            Assert.That(
                RequiredAssetsCheck.ShouldPrompt(isBatchMode: false, alreadyAskedThisSession: false, missingCount: 3),
                Is.True);
        }

        [Test]
        public void NobodyIsAskedTwiceInTheSameSession()
        {
            Assert.That(
                RequiredAssetsCheck.ShouldPrompt(isBatchMode: false, alreadyAskedThisSession: true, missingCount: 3),
                Is.False);
        }

        [Test]
        public void ACompleteCheckoutIsLeftAlone()
        {
            Assert.That(
                RequiredAssetsCheck.ShouldPrompt(isBatchMode: false, alreadyAskedThisSession: false, missingCount: 0),
                Is.False);
        }
    }
}
