using NUnit.Framework;
using ChessTheBetrayal.AI;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// Pins the identity of every curated position as a literal.
    ///
    /// These twenty positions are the shared input to the strength ladder, the capped-depth harness, the
    /// agreement harness, the tournament tooling and the on-device benchmark. Every recorded baseline in
    /// the project was measured over them, so a change in what they are is a change in what all of those
    /// numbers mean — and it would be completely silent, because a different-but-still-legal position
    /// produces a perfectly good measurement of something else. A Zobrist hash covers the whole board
    /// state at once (placement, side to move, castling rights, en passant, Betrayal right), which makes
    /// it the cheapest honest way to say "still exactly this position".
    ///
    /// The values were captured from the builder before it moved into the AI assembly, so they also pin
    /// that the move did not change any position on the way.
    /// </summary>
    [TestFixture]
    public class CuratedOpeningLinesTests
    {
        /// <summary>Zobrist hash and plies-played for each position, index-aligned with the line list.
        /// Plies is carried alongside because it is what the opening book measures a tier's allowance
        /// against, so it decides whether a real game would still be reciting theory here.</summary>
        private static readonly (ulong hash, int plies)[] Golden =
        {
            (4202767804471039198UL, 5),
            (2496418762452455734UL, 5),
            (17581979011470124154UL, 4),
            (13755586992210996777UL, 7),
            (12853772350428926720UL, 4),
            (7849640420098140804UL, 4),
            (10740714307125965927UL, 4),
            (15930454992706021537UL, 4),
            (17107156672439267041UL, 4),
            (3739843519993386853UL, 4),
            (412582822069403401UL, 6),
            (11818306820633387216UL, 6),
            (8310285169476758218UL, 2),
            (559353666291968866UL, 5),
            (16914756930552142698UL, 4),
            (4088759620773088389UL, 4),
            (10263618868174579811UL, 3),
            (5604956361039125982UL, 8),
            (5071107699565707937UL, 4),
            (10697119341055899974UL, 4),
        };

        [Test]
        public void TheSuiteStillHoldsExactlyTheNumberOfPositionsEveryBaselineWasMeasuredOver()
        {
            Assert.That(CuratedOpeningLines.Count, Is.EqualTo(Golden.Length),
                "Adding or removing a position changes the sample size of every recorded result. If that " +
                "is intended, the baselines have to be re-measured and these literals replaced together.");
        }

        [Test]
        public void EveryPositionIsStillTheExactPositionItWas()
        {
            for (int i = 0; i < CuratedOpeningLines.Count; i++)
            {
                BoardState board = CuratedOpeningLines.BuildPosition(i);

                Assert.That(board.ZobristHash, Is.EqualTo(Golden[i].hash),
                    $"Position {i} ({CuratedOpeningLines.Line(i)}) is no longer the board it was when the " +
                    "baselines were measured.");
                Assert.That(board.FullMoveNumber, Is.EqualTo(Golden[i].plies),
                    $"Position {i} is now reached in a different number of plies.");
            }
        }

        [Test]
        public void EveryPositionIsLegalAndReachedWithBothSidesIntactEnoughToPlayOn()
        {
            for (int i = 0; i < CuratedOpeningLines.Count; i++)
            {
                BoardState board = CuratedOpeningLines.BuildPosition(i);

                Assert.That(board.BetrayalRightAvailable, Is.True,
                    $"Position {i} must be handed over with the Betrayal right live — a search measured " +
                    "on a board where it is already spent is measuring a different game.");
                Assert.That(board.GetPieceIndices(Team.White).Count, Is.GreaterThan(0));
                Assert.That(board.GetPieceIndices(Team.Black).Count, Is.GreaterThan(0));
            }
        }

        [Test]
        public void EveryLineIsReplayedFromTheSharedStandardStart()
        {
            // The harness the strength ladder calls and the shared source the phone reads are the same
            // code path, so comparing them to each other proves nothing. What is worth pinning is that
            // the replay starts from the shared standard board and not a private copy of it — the
            // golden hashes above would all shift together if it ever did, and this says which of the
            // two moved.
            BoardState start = StandardChessPosition.Create(betrayalRightAvailable: true);

            Assert.That(start.FullMoveNumber, Is.EqualTo(0));
            Assert.That(CuratedOpeningLines.BuildPosition(12).FullMoveNumber, Is.EqualTo(2),
                "The shortest line is two plies, so a position reporting anything else is not being " +
                "replayed from a fresh standard board.");
        }
    }
}
