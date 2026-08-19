using System;
using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Events.Channels;

namespace ChessTheBetrayal.Tests.EditMode.Events.Channels
{
    /// <summary>
    /// The same promises as the payload-free channel, made again for the shape that carries data.
    /// They are worth checking twice because the two shapes share no code — the generic channel is
    /// not a subclass of the plain one, it is a second implementation of the same idea, and nothing
    /// but a test says the two agree.
    ///
    /// Driven through a real channel rather than a test-only subclass, so what is pinned is what
    /// the game actually raises. Team is the simplest payload any of them carries.
    /// </summary>
    [TestFixture]
    public class TypedEventChannelTests
    {
        private TeamSelectedEventChannel _channel;

        [SetUp]
        public void SetUp() => _channel = ScriptableObject.CreateInstance<TeamSelectedEventChannel>();

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
        public void A_listener_receives_the_payload_it_was_raised_with()
        {
            Team received = Team.None;
            _channel.Register(team => received = team);

            _channel.Raise(Team.Black);

            Assert.That(received, Is.EqualTo(Team.Black));
        }

        [Test]
        public void Registering_the_same_listener_twice_still_calls_it_once()
        {
            int heard = 0;
            Action<Team> listener = _ => heard++;

            _channel.Register(listener);
            _channel.Register(listener);
            _channel.Raise(Team.White);

            Assert.That(heard, Is.EqualTo(1));
            Assert.That(_channel.ListenerCount, Is.EqualTo(1));
        }

        [Test]
        public void An_unregistered_listener_hears_nothing()
        {
            int heard = 0;
            Action<Team> listener = _ => heard++;
            _channel.Register(listener);

            _channel.Unregister(listener);
            _channel.Raise(Team.White);

            Assert.That(heard, Is.EqualTo(0));
            Assert.That(_channel.ListenerCount, Is.EqualTo(0));
        }

        [Test]
        public void A_listener_may_unregister_itself_while_being_called()
        {
            int heard = 0;
            Action<Team> listener = null;
            listener = _ =>
            {
                heard++;
                _channel.Unregister(listener);
            };
            _channel.Register(listener);

            Assert.DoesNotThrow(() => _channel.Raise(Team.White));

            Assert.That(heard, Is.EqualTo(1));
            Assert.That(_channel.ListenerCount, Is.EqualTo(0));

            _channel.Raise(Team.White);
            Assert.That(heard, Is.EqualTo(1));
        }

        [Test]
        public void Every_registered_listener_receives_the_same_payload()
        {
            Team first = Team.None, second = Team.None;
            _channel.Register(team => first = team);
            _channel.Register(team => second = team);

            _channel.Raise(Team.Black);

            Assert.That(first, Is.EqualTo(Team.Black));
            Assert.That(second, Is.EqualTo(Team.Black));
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
