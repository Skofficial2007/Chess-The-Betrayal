using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.EditorTools.Events;
using ChessTheBetrayal.Events.Channels;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Events
{
    /// <summary>
    /// The event monitor is only as good as what it can find, and for a long time it could not find
    /// most of it. Channels come in two shapes that share no ancestry beyond the base added for
    /// this purpose, and a search for the payload-free type silently matched none of the ones that
    /// carry data — twelve of the eighteen assets in the project.
    ///
    /// These run against the real assets rather than fixtures, because the thing that went wrong
    /// was the search, not the channels.
    /// </summary>
    [TestFixture]
    public class EventChannelCatalogTests
    {
        [Test]
        public void It_finds_channels_at_all()
        {
            Assert.That(EventChannelCatalog.FindAll(), Is.Not.Empty,
                "the project ships event channel assets; finding none means the search is wrong");
        }

        [Test]
        public void It_finds_channels_that_carry_no_payload()
        {
            var voidChannels = EventChannelCatalog.FindAll().OfType<GameEventChannel>();

            Assert.That(voidChannels, Is.Not.Empty);
        }

        [Test]
        public void It_finds_channels_that_carry_a_payload()
        {
            var typedChannels = EventChannelCatalog.FindAll()
                .Where(channel => !(channel is GameEventChannel));

            Assert.That(typedChannels, Is.Not.Empty,
                "these were the ones the monitor used to miss entirely");
        }

        [Test]
        public void It_finds_more_of_them_than_the_payload_free_ones_alone()
        {
            var all = EventChannelCatalog.FindAll();
            int voidCount = all.Count(channel => channel is GameEventChannel);

            Assert.That(all.Count, Is.GreaterThan(voidCount),
                "if these are equal the search has fallen back to matching one shape only");
        }

        [Test]
        public void It_hands_back_no_holes()
        {
            Assert.That(EventChannelCatalog.FindAll(), Has.No.Null);
        }

        [Test]
        public void It_lists_them_in_name_order()
        {
            var names = EventChannelCatalog.FindAll().Select(channel => channel.name).ToList();

            Assert.That(names, Is.Ordered, "the window draws them in the order it is given");
        }

        [Test]
        public void Turning_tracing_on_reaches_both_channel_shapes()
        {
            var channels = TwoOfEachShape();
            try
            {
                EventChannelCatalog.SetTraceOnAll(channels, true);

                Assert.That(channels.Select(c => c.DebugTrace), Is.All.True);
            }
            finally
            {
                foreach (var c in channels) Object.DestroyImmediate(c);
            }
        }

        [Test]
        public void Turning_tracing_off_reaches_both_channel_shapes()
        {
            var channels = TwoOfEachShape();
            try
            {
                // Set directly rather than through the method under test: turning them on with
                // the same call would let a version that reaches only one shape leave the other
                // at its default of false, and the assertion below would pass on that.
                foreach (var c in channels) c.DebugTrace = true;

                EventChannelCatalog.SetTraceOnAll(channels, false);

                Assert.That(channels.Select(c => c.DebugTrace), Is.All.False);
            }
            finally
            {
                foreach (var c in channels) Object.DestroyImmediate(c);
            }
        }

        [Test]
        public void A_gap_in_the_list_does_not_stop_the_rest()
        {
            var channels = new List<EventChannelBase> { null, ScriptableObject.CreateInstance<GameEventChannel>() };
            try
            {
                Assert.DoesNotThrow(() => EventChannelCatalog.SetTraceOnAll(channels, true));

                Assert.That(channels[1].DebugTrace, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(channels[1]);
            }
        }

        /// <summary>One channel of each shape, because reaching only one of them is the whole
        /// defect this fixture exists for.</summary>
        private static List<EventChannelBase> TwoOfEachShape() => new List<EventChannelBase>
        {
            ScriptableObject.CreateInstance<GameEventChannel>(),
            ScriptableObject.CreateInstance<TeamSelectedEventChannel>(),
        };
    }
}
