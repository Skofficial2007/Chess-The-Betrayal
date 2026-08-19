using System;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.Events.Channels;

namespace ChessTheBetrayal.Tests.EditMode.Events.Channels
{
    /// <summary>
    /// What a payload-free channel promises the rest of the game: that a listener hears exactly the
    /// raises it registered for, once each, and that letting go mid-callback is safe.
    ///
    /// The last of those is the one worth pinning. A listener is free to unregister itself from
    /// inside its own handler — several of the board views do exactly that when they finish an
    /// animation — and the raise loop has to survive the list changing underneath it.
    /// </summary>
    [TestFixture]
    public class GameEventChannelTests
    {
        private GameEventChannel _channel;

        [SetUp]
        public void SetUp() => _channel = ScriptableObject.CreateInstance<GameEventChannel>();

        [TearDown]
        public void TearDown()
        {
            if (_channel != null) UnityEngine.Object.DestroyImmediate(_channel);
        }

        [Test]
        public void A_fresh_channel_has_no_listeners()
        {
            Assert.That(_channel.ListenerCount, Is.EqualTo(0));
        }

        [Test]
        public void A_registered_listener_hears_a_raise()
        {
            int heard = 0;
            _channel.Register(() => heard++);

            _channel.Raise();

            Assert.That(heard, Is.EqualTo(1));
        }

        [Test]
        public void Registering_the_same_listener_twice_still_calls_it_once()
        {
            int heard = 0;
            Action listener = () => heard++;

            _channel.Register(listener);
            _channel.Register(listener);
            _channel.Raise();

            Assert.That(heard, Is.EqualTo(1), "a double registration would double every callback");
            Assert.That(_channel.ListenerCount, Is.EqualTo(1));
        }

        [Test]
        public void An_unregistered_listener_hears_nothing()
        {
            int heard = 0;
            Action listener = () => heard++;
            _channel.Register(listener);

            _channel.Unregister(listener);
            _channel.Raise();

            Assert.That(heard, Is.EqualTo(0));
            Assert.That(_channel.ListenerCount, Is.EqualTo(0));
        }

        [Test]
        public void Unregistering_something_never_registered_is_harmless()
        {
            Assert.DoesNotThrow(() => _channel.Unregister(() => { }));
            Assert.That(_channel.ListenerCount, Is.EqualTo(0));
        }

        [Test]
        public void A_listener_may_unregister_itself_while_being_called()
        {
            int heard = 0;
            Action listener = null;
            listener = () =>
            {
                heard++;
                _channel.Unregister(listener);
            };
            _channel.Register(listener);

            Assert.DoesNotThrow(() => _channel.Raise());

            Assert.That(heard, Is.EqualTo(1));
            Assert.That(_channel.ListenerCount, Is.EqualTo(0), "it asked to be let go");

            _channel.Raise();
            Assert.That(heard, Is.EqualTo(1), "and should not be called again");
        }

        [Test]
        public void Every_registered_listener_hears_the_same_raise()
        {
            int first = 0, second = 0;
            _channel.Register(() => first++);
            _channel.Register(() => second++);

            _channel.Raise();

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(1));
            Assert.That(_channel.ListenerCount, Is.EqualTo(2));
        }

        [Test]
        public void Is_reachable_through_the_common_channel_base()
        {
            Assert.That(_channel, Is.InstanceOf<EventChannelBase>(),
                "editor tooling lists channels by this base, so one outside it goes unseen");
        }

        [Test]
        public void Tracing_is_off_until_someone_turns_it_on()
        {
            Assert.That(_channel.DebugTrace, Is.False);
        }

        [Test]
        public void Clearing_the_trace_log_is_safe_at_any_time()
        {
            Assert.DoesNotThrow(() => _channel.ClearDebugLog());
        }
    }
}
