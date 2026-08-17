using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.View.Board;
using Vector2Int = ChessTheBetrayal.Core.Data.Vector2Int;

namespace ChessTheBetrayal.Tests.EditMode.View
{
    /// <summary>
    /// Which marker a square shows used to be decided inline while assigning render layers inside a
    /// MonoBehaviour, so the precedence between two states landing on the same square could not be
    /// checked at all. These tests pin that precedence down, with no scene and nothing drawn.
    /// </summary>
    [TestFixture]
    public class BoardHighlightMapTests
    {
        private static Vector2Int Sq(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void AnEmptyMapShowsNothingAnywhere()
        {
            var map = new BoardHighlightMap();

            Assert.That(map.Resolve(Sq(3, 3)).IsEmpty, Is.True,
                "A board with no selection and no move played must be completely unmarked.");
        }

        [Test]
        public void AQuietDestinationGetsTheDotAndACapturingOneGetsTheRing()
        {
            var map = new BoardHighlightMap();

            map.AddDestination(Sq(1, 1), isCapture: false, isBetrayal: false);
            map.AddDestination(Sq(2, 2), isCapture: true, isBetrayal: false);

            Assert.That(map.Resolve(Sq(1, 1)).Marker, Is.EqualTo(SquareMarker.QuietMove));
            Assert.That(map.Resolve(Sq(2, 2)).Marker, Is.EqualTo(SquareMarker.Capture));
        }

        [Test]
        public void ABetrayalTargetOutranksAPlainCaptureOnTheSameSquare()
        {
            var map = new BoardHighlightMap();

            map.AddDestination(Sq(4, 4), isCapture: true, isBetrayal: false);
            map.AddDestination(Sq(4, 4), isCapture: true, isBetrayal: true);

            Assert.That(map.Resolve(Sq(4, 4)).Marker, Is.EqualTo(SquareMarker.BetrayalTarget),
                "A square reachable both as a capture and as a Betrayal must announce the Betrayal — " +
                "it is the more consequential of the two.");
        }

        [Test]
        public void RegisteringACaptureAfterABetrayalDoesNotDemoteTheSquare()
        {
            var map = new BoardHighlightMap();

            map.AddDestination(Sq(4, 4), isCapture: true, isBetrayal: true);
            map.AddDestination(Sq(4, 4), isCapture: true, isBetrayal: false);

            Assert.That(map.Resolve(Sq(4, 4)).Marker, Is.EqualTo(SquareMarker.BetrayalTarget),
                "Precedence must not depend on the order the destinations happen to arrive in.");
        }

        [Test]
        public void CheckOutranksEveryOtherMarker()
        {
            var map = new BoardHighlightMap();
            Vector2Int kingSquare = Sq(4, 0);

            map.CheckSquare = kingSquare;
            map.BetrayerSquare = kingSquare;
            map.Selected = kingSquare;
            map.AddDestination(kingSquare, isCapture: true, isBetrayal: true);

            Assert.That(map.Resolve(kingSquare).Marker, Is.EqualTo(SquareMarker.Check),
                "A king in danger must never be hidden behind another marker — including a " +
                "Betrayer that something is currently aiming at.");
        }

        [Test]
        public void TheBetrayerSquareOutranksACaptureDestinationButReportsBeingTargeted()
        {
            var map = new BoardHighlightMap();
            Vector2Int betrayerSquare = Sq(5, 3);

            map.BetrayerSquare = betrayerSquare;
            map.AddDestination(betrayerSquare, isCapture: true, isBetrayal: false);

            Assert.That(map.Resolve(betrayerSquare).Marker, Is.EqualTo(SquareMarker.BetrayerTargeted),
                "The ordinary capture marker must still lose to the Betrayer's own square, but the " +
                "player has to be told that the piece they picked can carry out the Retribution.");
        }

        [Test]
        public void AnUnreachableBetrayerIsAtLargeRatherThanTargeted()
        {
            var map = new BoardHighlightMap();
            Vector2Int betrayerSquare = Sq(5, 3);

            map.BetrayerSquare = betrayerSquare;
            map.AddDestination(Sq(2, 2), isCapture: true, isBetrayal: false);

            Assert.That(map.Resolve(betrayerSquare).Marker, Is.EqualTo(SquareMarker.BetrayerAtLarge),
                "A capture somewhere else on the board says nothing about reaching the Betrayer.");
        }

        [Test]
        public void PuttingTheExecutionerBackDownUntargetsTheBetrayer()
        {
            var map = new BoardHighlightMap();
            Vector2Int betrayerSquare = Sq(5, 3);

            map.BetrayerSquare = betrayerSquare;
            map.AddDestination(betrayerSquare, isCapture: true, isBetrayal: false);
            Assert.That(map.Resolve(betrayerSquare).Marker, Is.EqualTo(SquareMarker.BetrayerTargeted));

            map.ClearDestinations();

            Assert.That(map.Resolve(betrayerSquare).Marker, Is.EqualTo(SquareMarker.BetrayerAtLarge),
                "Cancelling the selection has to release the lock — the Betrayer is still at large, " +
                "just no longer in anyone's sights.");
        }

        [Test]
        public void TheSelectedSquareYieldsItsMarkerToADestination()
        {
            var map = new BoardHighlightMap();
            Vector2Int square = Sq(6, 6);

            map.Selected = square;

            Assert.That(map.Resolve(square).Marker, Is.EqualTo(SquareMarker.Selected));

            map.AddDestination(square, isCapture: false, isBetrayal: false);

            Assert.That(map.Resolve(square).Marker, Is.EqualTo(SquareMarker.QuietMove),
                "What a tap would do matters more than restating which square is already picked up.");
        }

        [Test]
        public void TintRanksSelectionOverHoverAndHoverOverTheLastMoveTo()
        {
            var map = new BoardHighlightMap();
            Vector2Int square = Sq(5, 5);

            map.LastMoveTo = square;
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.LastMoveTo));

            map.Hover = square;
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.Hover));

            map.Selected = square;
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.Selected));
        }

        [Test]
        public void TintRanksSelectionOverHoverAndHoverOverTheLastMoveFrom()
        {
            var map = new BoardHighlightMap();
            Vector2Int square = Sq(2, 6);

            map.LastMoveFrom = square;
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.LastMoveFrom));

            map.Hover = square;
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.Hover));

            map.Selected = square;
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.Selected));
        }

        [Test]
        public void TheLastMoveFromAndToSquaresGetDistinctTints()
        {
            var map = new BoardHighlightMap();

            map.LastMoveFrom = Sq(4, 1);
            map.LastMoveTo = Sq(4, 3);

            Assert.That(map.Resolve(Sq(4, 1)).Tint, Is.EqualTo(SquareTint.LastMoveFrom),
                "The square a piece left draws a hollow outline, not the same fill as where it landed.");
            Assert.That(map.Resolve(Sq(4, 3)).Tint, Is.EqualTo(SquareTint.LastMoveTo));
        }

        [Test]
        public void ASquareCanCarryATintAndAMarkerAtOnce()
        {
            var map = new BoardHighlightMap();
            Vector2Int square = Sq(3, 5);

            map.LastMoveTo = square;
            map.AddDestination(square, isCapture: false, isBetrayal: false);

            SquareHighlight highlight = map.Resolve(square);

            Assert.That(highlight.Tint, Is.EqualTo(SquareTint.LastMoveTo));
            Assert.That(highlight.Marker, Is.EqualTo(SquareMarker.QuietMove),
                "The tint and the marker are independent slots — a quiet move onto a just-played " +
                "square must show both, because the dot sits in the middle and the last-move mark " +
                "sits in the corners.");
        }

        [Test]
        public void AMarkerThatOwnsTheCornersStandsTheLastMoveDestinationDown()
        {
            var map = new BoardHighlightMap();
            Vector2Int square = Sq(3, 5);
            map.LastMoveTo = square;

            map.AddDestination(square, isCapture: true, isBetrayal: false);
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.None),
                "A capture draws in the same corners the last-move mark does, and what you can do " +
                "now outranks what already happened.");

            map.ClearDestinations();
            map.AddDestination(square, isCapture: true, isBetrayal: true);
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.None),
                "So does a betrayal target.");
        }

        [Test]
        public void TheBetrayerStandsTheLastMoveDestinationDownInBothOfItsStates()
        {
            var map = new BoardHighlightMap();
            Vector2Int square = Sq(5, 3);

            // The Act moved the Betrayer here, so its square IS the last move's destination.
            map.LastMoveTo = square;
            map.BetrayerSquare = square;

            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.None),
                "A Betrayer at large owns the corners.");

            map.AddDestination(square, isCapture: true, isBetrayal: false);

            Assert.That(map.Resolve(square).Marker, Is.EqualTo(SquareMarker.BetrayerTargeted));
            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.None),
                "And still owns them once a piece is aimed at it — the Retribution must not have " +
                "the last move drawn through it.");
        }

        [Test]
        public void TheLastMoveOriginSurvivesAMarkerOnItsSquare()
        {
            var map = new BoardHighlightMap();
            Vector2Int square = Sq(4, 1);

            map.LastMoveFrom = square;
            map.AddDestination(square, isCapture: false, isBetrayal: false);

            Assert.That(map.Resolve(square).Tint, Is.EqualTo(SquareTint.LastMoveFrom),
                "The origin draws along the edges, not the corners, so nothing displaces it.");
        }

        [Test]
        public void ClearingDestinationsKeepsCheckSelectionAndTheLastMove()
        {
            var map = new BoardHighlightMap();
            map.CheckSquare = Sq(4, 0);
            map.LastMoveTo = Sq(4, 3);
            map.Selected = Sq(1, 1);
            map.AddDestination(Sq(2, 2), isCapture: false, isBetrayal: false);

            map.ClearDestinations();

            Assert.That(map.Resolve(Sq(2, 2)).IsEmpty, Is.True, "Destinations should be gone.");
            Assert.That(map.Resolve(Sq(4, 0)).Marker, Is.EqualTo(SquareMarker.Check),
                "Putting a piece back down does not resolve a check.");
            Assert.That(map.Resolve(Sq(4, 3)).Tint, Is.EqualTo(SquareTint.LastMoveTo),
                "Putting a piece back down does not un-play the previous move.");
        }

        [Test]
        public void CollectActiveSquaresReturnsEveryMarkedSquareExactlyOnce()
        {
            var map = new BoardHighlightMap();
            map.Selected = Sq(1, 1);
            map.Hover = Sq(1, 1);           // same square, two reasons
            map.LastMoveFrom = Sq(0, 0);
            map.LastMoveTo = Sq(0, 1);
            map.CheckSquare = Sq(7, 7);
            map.BetrayerSquare = Sq(6, 2);
            map.AddDestination(Sq(2, 2), isCapture: false, isBetrayal: false);
            map.AddDestination(Sq(3, 3), isCapture: true, isBetrayal: false);
            map.AddDestination(Sq(4, 4), isCapture: true, isBetrayal: true);

            var active = new List<Vector2Int>();
            map.CollectActiveSquares(active);

            Assert.That(active, Is.Unique, "A square listed twice would be drawn twice.");
            Assert.That(active.Count, Is.EqualTo(8));
            Assert.That(active, Contains.Item(Sq(1, 1)));
            Assert.That(active, Contains.Item(Sq(7, 7)));
            Assert.That(active, Contains.Item(Sq(6, 2)));
            Assert.That(active, Contains.Item(Sq(4, 4)));
        }

        [Test]
        public void CollectActiveSquaresReplacesWhateverWasInTheList()
        {
            var map = new BoardHighlightMap();
            map.Selected = Sq(2, 2);

            var active = new List<Vector2Int> { Sq(6, 6), Sq(7, 7) };
            map.CollectActiveSquares(active);

            Assert.That(active, Is.EqualTo(new[] { Sq(2, 2) }),
                "Stale entries would leave highlights drawn on squares that no longer have any.");
        }

        [Test]
        public void ClearLeavesTheWholeBoardUnmarked()
        {
            var map = new BoardHighlightMap();
            map.Selected = Sq(1, 1);
            map.CheckSquare = Sq(4, 0);
            map.LastMoveTo = Sq(4, 3);
            map.BetrayerSquare = Sq(6, 2);
            map.AddDestination(Sq(2, 2), isCapture: false, isBetrayal: false);

            map.Clear();

            var active = new List<Vector2Int>();
            map.CollectActiveSquares(active);

            Assert.That(active, Is.Empty);
        }
    }
}
