using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Flow;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Flow
{
    /// <summary>
    /// Validates BackToModeSelectAction against a mock IMatchFlow, independent of GameManager/UIManager.
    /// This is the prototype's bound IPostGameAction: Replay must tear the finished match down and
    /// land on Mode Select, never silently start a new match under a hidden default mode.
    /// </summary>
    [TestFixture]
    public class BackToModeSelectActionTests
    {
        private class RecordingMatchFlow : IMatchFlow
        {
            public readonly List<string> Calls = new List<string>();
            public GameModeConfig? StartedMode;

            public void TearDownCurrentMatch() => Calls.Add(nameof(TearDownCurrentMatch));

            public void StartNewMatch(GameModeConfig mode)
            {
                Calls.Add(nameof(StartNewMatch));
                StartedMode = mode;
            }

            public void ReturnToModeSelect() => Calls.Add(nameof(ReturnToModeSelect));
        }

        [Test]
        public void Execute_TearsDownMatchBeforeReturningToModeSelect()
        {
            var flow = new RecordingMatchFlow();
            var action = new BackToModeSelectAction();
            var result = new MatchResult(Team.White, isTimeout: false, GameModePresets.Bullet1_0);

            action.Execute(flow, result);

            Assert.That(flow.Calls, Is.EqualTo(new[]
            {
                nameof(IMatchFlow.TearDownCurrentMatch),
                nameof(IMatchFlow.ReturnToModeSelect)
            }), "Replay must tear down the finished match before showing Mode Select.");
        }

        [Test]
        public void Execute_NeverStartsANewMatchDirectly()
        {
            var flow = new RecordingMatchFlow();
            var action = new BackToModeSelectAction();
            var result = new MatchResult(null, isTimeout: false, GameModePresets.Unlimited);

            action.Execute(flow, result);

            Assert.That(flow.StartedMode, Is.Null,
                "The prototype's post-game action must always route through Mode Select, never auto-start a match under a carried-over or default mode.");
        }
    }
}
