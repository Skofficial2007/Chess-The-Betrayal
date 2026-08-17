using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.View.Input;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.View
{
    /// <summary>
    /// A finger and a mouse produce different frames, and the difference is easy to miss because
    /// only one of them can be tried in the editor. These pin the touch frames specifically: the
    /// one that matters reports itself as not pressed, because it is the frame the finger lifts,
    /// and it is also the only frame that can ever complete a tap.
    ///
    /// Nothing here touches real hardware — that lives behind IPointerDevice, which is a straight
    /// mirror of the engine's API and cannot be tested. What is asserted below is every rule
    /// applied to whatever that mirror reports.
    /// </summary>
    [TestFixture]
    public class PointerTapRecognizerTests
    {
        private static readonly Vector2Int TileA = new Vector2Int(2, 3);
        private static readonly Vector2Int TileB = new Vector2Int(5, 1);

        #region Which frames carry a position worth reading

        [Test]
        public void AFingerThatJustLiftedStillCarriesThePositionItLiftedFrom()
        {
            // The regression this whole seam exists for. A phone has no mouse, so
            // reportsPositionWhileIdle is false, and the lift frame already reads as not pressed.
            // Judging the frame on those two alone discards it — and with it every tap the game
            // could ever receive by touch.
            Assert.That(PointerTapRecognizer.HasUsablePosition(
                reportsPositionWhileIdle: false, isPressed: false, wasReleased: true), Is.True);
        }

        [Test]
        public void AFingerRestingOnTheGlassCarriesAPosition()
        {
            Assert.That(PointerTapRecognizer.HasUsablePosition(
                reportsPositionWhileIdle: false, isPressed: true, wasReleased: false), Is.True);
        }

        [Test]
        public void AScreenWithNoFingerOnItCarriesNothing()
        {
            // Where a finger last touched is stale data, not a hover.
            Assert.That(PointerTapRecognizer.HasUsablePosition(
                reportsPositionWhileIdle: false, isPressed: false, wasReleased: false), Is.False);
        }

        [Test]
        public void AMouseAlwaysCarriesAPositionEvenWithNoButtonDown()
        {
            Assert.That(PointerTapRecognizer.HasUsablePosition(
                reportsPositionWhileIdle: true, isPressed: false, wasReleased: false), Is.True);
        }

        #endregion

        #region Taps

        [Test]
        public void ATouchGestureActivatesTheTileExactlyOnceOnTheFrameTheFingerLifts()
        {
            var device = new FakePointerDevice { ReportsPositionWhileIdle = false };
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);
            var frames = new PointerFrameDriver(device, recognizer);

            device.BeginTouch();
            Assert.That(frames.Pump(TileA, 1.00f), Is.False, "Pressing down is not yet a tap.");

            device.HoldTouch();
            Assert.That(frames.Pump(TileA, 1.02f), Is.False, "Holding still is not a tap.");
            Assert.That(frames.Pump(TileA, 1.04f), Is.False);

            device.EndTouch();
            Assert.That(frames.Pump(TileA, 1.06f), Is.True, "Lifting on the same tile is the tap.");

            device.Idle();
            Assert.That(frames.Pump(TileA, 1.08f), Is.False, "A tap must not repeat after the finger is gone.");
        }

        [Test]
        public void AFingerDraggedToAnotherTileBeforeLiftingActivatesNothing()
        {
            var device = new FakePointerDevice { ReportsPositionWhileIdle = false };
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);
            var frames = new PointerFrameDriver(device, recognizer);

            device.BeginTouch();
            frames.Pump(TileA, 1.00f);

            device.EndTouch();
            Assert.That(frames.Pump(TileB, 1.06f), Is.False);
        }

        [Test]
        public void LiftingOffTheBoardActivatesNothing()
        {
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);

            recognizer.Observe(wasPressed: true, wasReleased: false, TileA, 1.00f);
            Assert.That(recognizer.Observe(wasPressed: false, wasReleased: true, Vector2Int.Invalid, 1.06f), Is.False);
        }

        [Test]
        public void ATapCompletedWithinOneFrameStillCounts()
        {
            // A quick stab can report press and release on the same frame.
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);

            Assert.That(recognizer.Observe(wasPressed: true, wasReleased: true, TileA, 1.00f), Is.True);
        }

        [Test]
        public void ALiftWithNoPressBehindItActivatesNothing()
        {
            // Reachable for real: a finger that went down while the UI was blocking, or before the
            // game became active, releases into a recognizer that never saw the press.
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);

            Assert.That(recognizer.Observe(wasPressed: false, wasReleased: true, TileA, 1.00f), Is.False);
        }

        #endregion

        #region Debounce

        [Test]
        public void ASecondTapArrivingTooFastIsDropped()
        {
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);

            Assert.That(recognizer.Observe(wasPressed: true, wasReleased: true, TileA, 1.00f), Is.True);
            Assert.That(recognizer.Observe(wasPressed: true, wasReleased: true, TileA, 1.10f), Is.False);
        }

        [Test]
        public void ASecondTapArrivingAfterTheWindowIsAccepted()
        {
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);

            Assert.That(recognizer.Observe(wasPressed: true, wasReleased: true, TileA, 1.00f), Is.True);
            Assert.That(recognizer.Observe(wasPressed: true, wasReleased: true, TileA, 1.16f), Is.True);
        }

        [Test]
        public void TheFirstTapOfAMatchIsNeverDebouncedAwayByTheClockStartingAtZero()
        {
            var recognizer = new PointerTapRecognizer(minSecondsBetweenActivations: 0.15f);

            Assert.That(recognizer.Observe(wasPressed: true, wasReleased: true, TileA, 0f), Is.True);
        }

        #endregion

        /// <summary>
        /// Walks a fake device through the same two steps Update does — decide whether the frame
        /// carries a position, then hand the frame to the recognizer. It mirrors that composition
        /// rather than sharing it, so it proves the rules but not that Update still calls them.
        /// </summary>
        private sealed class PointerFrameDriver
        {
            private readonly FakePointerDevice _device;
            private readonly PointerTapRecognizer _recognizer;

            public PointerFrameDriver(FakePointerDevice device, PointerTapRecognizer recognizer)
            {
                _device = device;
                _recognizer = recognizer;
            }

            public bool Pump(Vector2Int tileUnderPointer, float realtimeSeconds)
            {
                if (!PointerTapRecognizer.HasUsablePosition(
                        _device.ReportsPositionWhileIdle, _device.IsPressed, _device.WasReleased))
                {
                    return false;
                }

                return _recognizer.Observe(_device.WasPressed, _device.WasReleased, tileUnderPointer, realtimeSeconds);
            }
        }

        private sealed class FakePointerDevice : IPointerDevice
        {
            public bool ReportsPositionWhileIdle { get; set; }
            public bool IsPressed { get; private set; }
            public bool WasPressed { get; private set; }
            public bool WasReleased { get; private set; }
            public Vector2 Position { get; set; }

            public void BeginTouch()
            {
                IsPressed = true;
                WasPressed = true;
                WasReleased = false;
            }

            public void HoldTouch()
            {
                IsPressed = true;
                WasPressed = false;
                WasReleased = false;
            }

            /// <summary>
            /// The lift frame as real hardware reports it: no longer pressed, released this frame.
            /// </summary>
            public void EndTouch()
            {
                IsPressed = false;
                WasPressed = false;
                WasReleased = true;
            }

            public void Idle()
            {
                IsPressed = false;
                WasPressed = false;
                WasReleased = false;
            }
        }
    }
}
