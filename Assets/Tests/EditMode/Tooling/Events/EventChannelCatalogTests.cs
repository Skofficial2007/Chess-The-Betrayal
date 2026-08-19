using System.Linq;
using NUnit.Framework;
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
    }
}
