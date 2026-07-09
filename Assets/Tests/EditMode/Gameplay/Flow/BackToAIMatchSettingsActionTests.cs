using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Match;
using ChessTheBetrayal.Gameplay.Flow;

namespace ChessTheBetrayal.Tests.EditMode.Gameplay.Flow
{
    /// <summary>
    /// Validates BackToAIMatchSettingsAction against a mock IMatchFlow, independent of
    /// GameManager/UIManager. This is the practice-match IPostGameAction: Replay must tear the
    /// finished match down and land on the AI Settings screen, never Mode Select — a practice match
    /// never went through Mode Select in the first place (it's hardcoded Ultimate).
    /// </summary>
    [TestFixture]
    public class BackToAIMatchSettingsActionTests
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

            public void ReturnToAIMatchSettings() => Calls.Add(nameof(ReturnToAIMatchSettings));

            public void AcknowledgeGameOver() => Calls.Add(nameof(AcknowledgeGameOver));
        }

        [Test]
        public void Execute_TearsDownMatchBeforeReturningToAIMatchSettings()
        {
            var flow = new RecordingMatchFlow();
            var action = new BackToAIMatchSettingsAction();
            var result = new MatchResult(Team.White, isTimeout: false, GameModePresets.Unlimited);

            action.Execute(flow, result);

            Assert.That(flow.Calls, Is.EqualTo(new[]
            {
                nameof(IMatchFlow.TearDownCurrentMatch),
                nameof(IMatchFlow.ReturnToAIMatchSettings)
            }), "Replay for a practice match must tear down the finished match before showing AI Settings.");
        }

        [Test]
        public void Execute_NeverRoutesThroughModeSelect()
        {
            var flow = new RecordingMatchFlow();
            var action = new BackToAIMatchSettingsAction();
            var result = new MatchResult(null, isTimeout: false, GameModePresets.Unlimited);

            action.Execute(flow, result);

            Assert.That(flow.Calls, Does.Not.Contain(nameof(IMatchFlow.ReturnToModeSelect)),
                "A practice match never went through Mode Select, so Replay must not send it there either.");
        }
    }
}
