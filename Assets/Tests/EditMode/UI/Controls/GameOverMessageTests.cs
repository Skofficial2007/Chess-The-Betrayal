using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Events.Payloads;
using ChessTheBetrayal.UI.Controls;

namespace ChessTheBetrayal.Tests.EditMode.UI
{
    /// <summary>
    /// The one line a player reads when the game stops. Worth pinning rather than eyeballing:
    /// a repetition, a fifty-move draw and a stalemate are three different things arrived at
    /// through positions that look the same, so the wording is the only thing distinguishing them,
    /// and a mapping that quietly collapses two of them together still renders a plausible panel.
    /// </summary>
    [TestFixture]
    public class GameOverMessageTests
    {
        [Test]
        public void Build_ARepetitionDraw_SaysSoRatherThanClaimingStalemate()
        {
            Assert.That(GameOverMessage.Build(null, GameEndReason.Repetition),
                Is.EqualTo("Draw by Repetition."));
        }

        [Test]
        public void Build_AFiftyMoveDraw_NamesWhatRanOut()
        {
            Assert.That(GameOverMessage.Build(null, GameEndReason.FiftyMoveRule),
                Is.EqualTo("Draw. Fifty moves without a capture or a pawn move."));
        }

        [Test]
        public void Build_AStalemate_StillReadsTheWayItAlwaysDid()
        {
            Assert.That(GameOverMessage.Build(null, GameEndReason.Stalemate),
                Is.EqualTo("Stalemate! Draw."));
        }

        /// <summary>
        /// The property the panel actually has to hold, stated on its own so it cannot be satisfied
        /// by the three lines above happening to be right today: no two ways of drawing may produce
        /// the same sentence, or a player is told one ending when they got another.
        /// </summary>
        [Test]
        public void Build_EveryKindOfDraw_ReadsDifferentlyFromEveryOther()
        {
            var reasons = new[] { GameEndReason.Stalemate, GameEndReason.Repetition, GameEndReason.FiftyMoveRule };
            var seen = new Dictionary<string, GameEndReason>();

            foreach (GameEndReason reason in reasons)
            {
                string text = GameOverMessage.Build(null, reason);

                Assert.That(seen.TryGetValue(text, out GameEndReason alreadyUsing), Is.False,
                    $"{reason} and {alreadyUsing} both end the game with \"{text}\", so the panel "
                    + "cannot tell a player which one happened.");

                seen[text] = reason;
            }
        }

        /// <summary>
        /// A draw whose reason nobody passed has to keep reading as a stalemate. The reason travels
        /// through several hands to get here and the panel is the last place that could notice it
        /// went missing — landing on a repetition claim by default would invent an ending.
        /// </summary>
        [Test]
        public void Build_ADrawWithNoReasonGiven_FallsBackToStalemate()
        {
            Assert.That(GameOverMessage.Build(null, GameEndReason.Checkmate),
                Is.EqualTo("Stalemate! Draw."));
        }

        [Test]
        public void Build_AWin_SaysWhoWonWhateverEndedIt()
        {
            Assert.That(GameOverMessage.Build(Team.White, GameEndReason.Checkmate), Is.EqualTo("White Team Won!"));
            Assert.That(GameOverMessage.Build(Team.Black, GameEndReason.Checkmate), Is.EqualTo("Black Team Won!"));
            Assert.That(GameOverMessage.Build(Team.White, GameEndReason.Repetition), Is.EqualTo("White Team Won!"),
                "A won game is won; the draw reasons must not leak into it.");
        }

        [Test]
        public void Build_OnTheClock_LeadsWithTheTimeoutWhicheverWayItWent()
        {
            Assert.That(GameOverMessage.Build(Team.Black, GameEndReason.Timeout, byTimeout: true),
                Is.EqualTo("Time Out!\nBlack Team Won!"));
            Assert.That(GameOverMessage.Build(null, GameEndReason.Timeout, byTimeout: true),
                Is.EqualTo("Time Out!\nDraw (Insufficient Material)"));
        }

        /// <summary>
        /// Running out of time is what ended the game, so it outranks whatever the position was.
        /// </summary>
        [Test]
        public void Build_ADrawOnTimeInARepeatedPosition_ReportsTheClock()
        {
            Assert.That(GameOverMessage.Build(null, GameEndReason.Repetition, byTimeout: true),
                Is.EqualTo("Time Out!\nDraw (Insufficient Material)"));
        }
    }
}
