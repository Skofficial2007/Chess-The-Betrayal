using NUnit.Framework;
using UnityEngine;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.View.Board;

namespace ChessTheBetrayal.Tests.EditMode.View.Board
{
    /// <summary>
    /// The death piles used to be a pair of private lists inside a MonoBehaviour, so the ordering
    /// rule a takeback depends on could not be exercised at all. Standing occupants in as plain
    /// strings keeps these tests to what the class actually owns — who is in the pile, in what
    /// order, and where each one is standing — with no scene and no spawned pieces involved.
    /// </summary>
    [TestFixture]
    public class GraveyardStackTests
    {
        // Board measurements matching an ordinary 8x8 set-up: origin at the bottom-left corner of
        // a board centred on the world origin.
        private static GraveyardLayout StandardLayout() =>
            new GraveyardLayout(
                boardOrigin: new Vector3(-4f, 0f, -4f),
                tileSize: 1f,
                tileCountX: 8,
                tileCountY: 8,
                tilesYOffset: 0f,
                pieceYOffset: 0.05f,
                deathSpacing: 0.35f);

        private static GraveyardStack<string> NewStack()
        {
            var stack = new GraveyardStack<string>();
            stack.SetLayout(StandardLayout());
            return stack;
        }

        [Test]
        public void Push_HandsOutADistinctSlotPerOccupant()
        {
            GraveyardStack<string> stack = NewStack();

            GraveyardSlot first = stack.Push(Team.White, "a");
            GraveyardSlot second = stack.Push(Team.White, "b");

            Assert.That(second.Position, Is.Not.EqualTo(first.Position),
                "Two captured pieces must never be sent to the same place.");
            Assert.That(stack.Count(Team.White), Is.EqualTo(2));
        }

        [Test]
        public void Push_KeepsEachSidesPileSeparate()
        {
            GraveyardStack<string> stack = NewStack();

            GraveyardSlot white = stack.Push(Team.White, "w");
            GraveyardSlot black = stack.Push(Team.Black, "b");

            Assert.That(stack.Count(Team.White), Is.EqualTo(1));
            Assert.That(stack.Count(Team.Black), Is.EqualTo(1));
            Assert.That(white.Position.x, Is.GreaterThan(black.Position.x),
                "The two piles sit off opposite edges, so the first slot of each is nowhere near the other.");
        }

        /// <summary>
        /// The invariant the whole capture-reversal design rests on. A slot is handed out by
        /// position in the pile, so taking the last occupant back off must leave every remaining
        /// slot describing exactly where its occupant already stands — and the next piece captured
        /// must be given the slot that was just vacated, not a new one further out.
        /// </summary>
        [Test]
        public void TryPopLast_ReturnsMostRecentAndFreesExactlyThatSlot()
        {
            GraveyardStack<string> stack = NewStack();

            GraveyardSlot firstSlot = stack.Push(Team.White, "first");
            GraveyardSlot secondSlot = stack.Push(Team.White, "second");
            GraveyardSlot thirdSlot = stack.Push(Team.White, "third");

            Assert.That(stack.TryPopLast(Team.White, out string popped), Is.True);
            Assert.That(popped, Is.EqualTo("third"), "A takeback puts back the piece taken most recently.");

            Assert.That(stack.TryPopLast(Team.White, out popped), Is.True);
            Assert.That(popped, Is.EqualTo("second"));

            // What is left must still describe the board as it stands.
            Assert.That(stack.Count(Team.White), Is.EqualTo(1));
            Assert.That(stack.Occupants(Team.White)[0], Is.EqualTo("first"));

            // And the pile refills over exactly the ground it gave up.
            Assert.That(stack.Push(Team.White, "second again").Position, Is.EqualTo(secondSlot.Position));
            Assert.That(stack.Push(Team.White, "third again").Position, Is.EqualTo(thirdSlot.Position));
            Assert.That(firstSlot.Position, Is.Not.EqualTo(secondSlot.Position));
        }

        [Test]
        public void TryPopLast_EmptyPile_ReturnsFalse()
        {
            GraveyardStack<string> stack = NewStack();

            Assert.That(stack.TryPopLast(Team.Black, out string popped), Is.False);
            Assert.That(popped, Is.Null);
        }

        [Test]
        public void TryPopLast_OnlyTouchesTheSideAskedFor()
        {
            GraveyardStack<string> stack = NewStack();
            stack.Push(Team.White, "w");
            stack.Push(Team.Black, "b");

            Assert.That(stack.TryPopLast(Team.White, out string popped), Is.True);
            Assert.That(popped, Is.EqualTo("w"));
            Assert.That(stack.Count(Team.Black), Is.EqualTo(1), "The other side's pile is untouched.");
        }

        [Test]
        public void All_ReturnsBothPilesForTeardown()
        {
            GraveyardStack<string> stack = NewStack();
            stack.Push(Team.White, "w1");
            stack.Push(Team.Black, "b1");
            stack.Push(Team.White, "w2");

            Assert.That(stack.All, Is.EquivalentTo(new[] { "w1", "w2", "b1" }));
        }

        [Test]
        public void Clear_EmptiesBothPiles()
        {
            GraveyardStack<string> stack = NewStack();
            stack.Push(Team.White, "w");
            stack.Push(Team.Black, "b");

            stack.Clear();

            Assert.That(stack.Count(Team.White), Is.EqualTo(0));
            Assert.That(stack.Count(Team.Black), Is.EqualTo(0));
        }

        [Test]
        public void SlotFor_PlacesEachPileOffItsOwnEdgeFacingTheBoard()
        {
            GraveyardLayout layout = StandardLayout();

            GraveyardSlot white = layout.SlotFor(Team.White, 0);
            GraveyardSlot black = layout.SlotFor(Team.Black, 0);

            // Board spans x -4..4, so the piles sit just outside opposite edges.
            Assert.That(white.Position.x, Is.EqualTo(4.5f).Within(0.0001f));
            Assert.That(black.Position.x, Is.EqualTo(-4.5f).Within(0.0001f));

            Assert.That(white.LookDirection.x, Is.LessThan(0f), "White's pile faces back in toward the board.");
            Assert.That(black.LookDirection.x, Is.GreaterThan(0f), "Black's pile faces back in from the other side.");
            Assert.That(white.LookDirection.y, Is.EqualTo(0f), "Pieces stand level, however far the pile sits below the board.");
        }
    }
}
