using System;
using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine
{
    /// <summary>
    /// The packing has two jobs, and both are load-bearing far away from here. It must tell any two
    /// different moves apart, because the transposition table and the opening book both use it as an
    /// identity — a collision silently mis-orders a search node or hands back the wrong book reply.
    /// And it must depend on nothing but the four fields that identify a move, because the packing
    /// stored in a book asset was produced from a compiler's move and gets matched against one the
    /// engine generated mid-game, with entirely different capture and clock state attached.
    /// </summary>
    [TestFixture]
    public class PackedMoveTests
    {
        [Test]
        public void Pack_TellsEveryDistinctMoveApart()
        {
            var seen = new Dictionary<uint, string>();

            foreach (ChessPieceType promotedTo in Enum.GetValues(typeof(ChessPieceType)))
            foreach (BetrayalStage stage in Enum.GetValues(typeof(BetrayalStage)))
            for (int fromSquare = 0; fromSquare < 64; fromSquare++)
            for (int toSquare = 0; toSquare < 64; toSquare++)
            {
                MoveCommand move = Move(fromSquare, toSquare, promotedTo, stage);
                uint packed = PackedMove.Pack(move);
                string identity = $"{fromSquare}->{toSquare} promo={promotedTo} stage={stage}";

                if (seen.TryGetValue(packed, out string collidesWith))
                {
                    Assert.Fail($"'{identity}' packs to the same value as '{collidesWith}'. Two " +
                                "different moves sharing a packed value would let a transposition " +
                                "table entry or a book move be applied to the wrong move.");
                }

                seen[packed] = identity;
            }

            int expected = 64 * 64
                * Enum.GetValues(typeof(ChessPieceType)).Length
                * Enum.GetValues(typeof(BetrayalStage)).Length;
            Assert.That(seen.Count, Is.EqualTo(expected));
        }

        [Test]
        public void Pack_StaysWithinNineteenBits()
        {
            const uint beyondNineteenBits = ~0u << 19;

            foreach (ChessPieceType promotedTo in Enum.GetValues(typeof(ChessPieceType)))
            foreach (BetrayalStage stage in Enum.GetValues(typeof(BetrayalStage)))
            {
                uint packed = PackedMove.Pack(Move(63, 63, promotedTo, stage));

                Assert.That(packed & beyondNineteenBits, Is.Zero,
                    $"promo={promotedTo} stage={stage} spilled past bit 18.");
            }
        }

        [Test]
        public void Pack_IgnoresEverythingButTheFourIdentifyingFields()
        {
            Vector2Int from = new Vector2Int(4, 1);
            Vector2Int to = new Vector2Int(4, 3);
            PieceData pawn = new PieceData(Team.White, ChessPieceType.Pawn, 1, 1, false);
            PieceData knight = new PieceData(Team.Black, ChessPieceType.Knight, -1, 6, true);

            // Same squares, same promotion, same stage — but every other field differs, the way a
            // book compiler's move differs from the one the engine generates during a real game.
            MoveCommand quiet = new MoveCommand(from, to, pawn);
            MoveCommand loaded = new MoveCommand(
                from, to, knight, knight,
                previousCastlingMask: 15,
                previousEnPassantFile: 3,
                previousBetrayalRightAvailable: false,
                whiteRemainingMsAtMove: 1234,
                blackRemainingMsAtMove: 5678);

            Assert.That(PackedMove.Pack(loaded), Is.EqualTo(PackedMove.Pack(quiet)),
                "Two moves sharing an origin, destination, promotion and stage packed differently, " +
                "so the packing has begun reading some fifth field. A book entry compiled offline " +
                "would stop matching the move the engine generates for that position in play.");
        }

        private static MoveCommand Move(
            int fromSquare, int toSquare, ChessPieceType promotedTo, BetrayalStage stage) =>
            new MoveCommand(
                new Vector2Int(fromSquare % 8, fromSquare / 8),
                new Vector2Int(toSquare % 8, toSquare / 8),
                default,
                promotedTo: promotedTo,
                stage: stage);
    }
}
