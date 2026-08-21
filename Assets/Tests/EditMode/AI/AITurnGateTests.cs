using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins AITurnGate's decision table — the guard GameManager.TryRequestAIMove uses to decide
    /// whether to kick off a background search right now.
    /// </summary>
    [TestFixture]
    public class AITurnGateTests
    {
        [Test]
        public void ShouldRequestMove_NoAgent_ReturnsFalse()
        {
            Assert.That(AITurnGate.ShouldRequestMove(hasAgent: false, Team.White, Team.White, isGameActive: true), Is.False);
        }

        [Test]
        public void ShouldRequestMove_NotAiTeamsTurn_ReturnsFalse()
        {
            Assert.That(AITurnGate.ShouldRequestMove(hasAgent: true, currentTurn: Team.White, aiTeam: Team.Black, isGameActive: true), Is.False);
        }

        [Test]
        public void ShouldRequestMove_GameNotActive_ReturnsFalse()
        {
            Assert.That(AITurnGate.ShouldRequestMove(hasAgent: true, Team.White, Team.White, isGameActive: false), Is.False);
        }

        [Test]
        public void ShouldRequestMove_AgentPresentCorrectTurnGameActive_ReturnsTrue()
        {
            Assert.That(AITurnGate.ShouldRequestMove(hasAgent: true, Team.Black, Team.Black, isGameActive: true), Is.True);
        }

        [Test]
        public void ShouldRequestMove_AllConditionsFail_ReturnsFalse()
        {
            Assert.That(AITurnGate.ShouldRequestMove(hasAgent: false, Team.White, Team.Black, isGameActive: false), Is.False);
        }
    }
}
