using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The authoring-time proof every EndgameConversionSuite entry must pass before it's trusted to
    /// judge the AI's endgame play — the same discipline YardstickPositionProofTests applies to
    /// single-move positions, adapted for a goal that spans many moves: a conversion position must
    /// actually be won (the attacking side has overwhelming, decisive material) and must not already
    /// be resolved (no side is already mated or stalemated at the starting position).
    /// </summary>
    [TestFixture]
    public class EndgameConversionProofTests
    {
        private static readonly IChessEngine Engine = new ChessEngineAdapter();

        private static IEnumerable<TestCaseData> AllPositions()
        {
            foreach (EndgameConversionPosition position in EndgameConversionSuite.All)
                yield return new TestCaseData(position).SetName(position.Name);
        }

        [TestCaseSource(nameof(AllPositions))]
        public void Position_StartsWithNormalGameStateForBothSides(EndgameConversionPosition position)
        {
            BoardState board = position.BuildBoard();

            GameState mover = Engine.EvaluateGameState(board, board.CurrentTurn);
            Assert.That(mover == GameState.Normal || mover == GameState.Check, Is.True,
                $"{position.Name}: the position must not already be decided (found {mover}) — a conversion probe needs a genuine multi-move task ahead of it.");
        }

        [TestCaseSource(nameof(AllPositions))]
        public void MatingPosition_AttackingSideHasDecisiveMaterialAdvantage(EndgameConversionPosition position)
        {
            // A material floor only makes sense for the mating goal — a lone extra pawn is never a
            // "decisive material advantage" by piece count (GetMaterialAdvantage uses standard 1/3/
            // 5/9 point values, not centipawns), even when it is completely unstoppable. The pawn
            // race's winning-ness is a positional fact, proven separately below.
            if (position.Goal != ConversionGoal.DriveLoneKingToMate) return;

            BoardState board = position.BuildBoard();

            int materialForWhite = Engine.GetMaterialAdvantage(board);
            int materialForAttacker = position.AttackingTeam == Team.White ? materialForWhite : -materialForWhite;

            // A bare rook (5) or queen (9) both clear this floor easily against a bare king (0); the
            // point is ruling out an accidentally balanced or losing fixture, not pinning an exact
            // number.
            Assert.That(materialForAttacker, Is.GreaterThanOrEqualTo(5),
                $"{position.Name}: the attacking side ({position.AttackingTeam}) must hold decisive mating material, but the material delta is only {materialForAttacker}.");
        }

        [Test]
        public void PawnRacePosition_DefendingKingCannotReachThePawnBeforeItPromotes()
        {
            EndgameConversionPosition position = Find("KingAndPawnRace");
            BoardState board = position.BuildBoard();

            board.TryFindKing(position.AttackingTeam == Team.White ? Team.Black : Team.White, out Vector2Int defenderKing);
            var pawnIndices = board.GetPieceIndices(position.AttackingTeam);
            Vector2Int pawnSquare = default;
            foreach (int idx in pawnIndices)
            {
                PieceData piece = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);
                if (piece.Type == ChessPieceType.Pawn) pawnSquare = new Vector2Int(idx % board.TileCountX, idx / board.TileCountX);
            }

            int promotionRank = position.AttackingTeam == Team.White ? board.TileCountY - 1 : 0;
            int pawnPliesToPromote = System.Math.Abs(promotionRank - pawnSquare.y);

            // Chebyshev distance is the true fastest a king can reach a square — one king step
            // covers one file and one rank simultaneously. The "square rule" for king-and-pawn races:
            // if the defending king cannot reach the pawn's promotion square in as many moves as the
            // pawn needs, the pawn queens uncontested regardless of whose move it is.
            int kingPliesToPromotionSquare = System.Math.Max(
                System.Math.Abs(defenderKing.x - pawnSquare.x),
                System.Math.Abs(promotionRank - defenderKing.y));

            Assert.That(kingPliesToPromotionSquare, Is.GreaterThan(pawnPliesToPromote + 1),
                $"KingAndPawnRace: the defending king (at distance {kingPliesToPromotionSquare}) can reach the pawn's promotion square before or around when the pawn (distance {pawnPliesToPromote}) arrives — this is not a clean, unstoppable race as authored.");
        }

        private static EndgameConversionPosition Find(string name)
        {
            foreach (EndgameConversionPosition position in EndgameConversionSuite.All)
                if (position.Name == name) return position;
            Assert.Fail($"No EndgameConversionSuite position named {name}.");
            return null;
        }

        [TestCaseSource(nameof(AllPositions))]
        public void Position_DefendingSideHasNoNonKingPieces_ExceptThePawnRace(EndgameConversionPosition position)
        {
            // KRK/KQK/the defected-rook case are all meant to be a LONE king defending — if the
            // fixture accidentally left the defender any material, the conversion runner's simple
            // "play the real search on both sides" driver stops being a clean test of the mating
            // technique specifically. The pawn race is the one deliberate exception: both sides keep
            // their own king, and the defender's king is the whole point of the race.
            if (position.Goal == ConversionGoal.PromoteThePawn) return;

            BoardState board = position.BuildBoard();
            Team defendingTeam = position.AttackingTeam == Team.White ? Team.Black : Team.White;

            var indices = board.GetPieceIndices(defendingTeam);
            foreach (int idx in indices)
            {
                PieceData piece = board.GetPiece(idx % board.TileCountX, idx / board.TileCountX);
                Assert.That(piece.IsEmpty || piece.Type == ChessPieceType.King, Is.True,
                    $"{position.Name}: expected the defending side to be a bare king, but found a {piece.Type} — this changes what the fixture is actually testing.");
            }
        }
    }
}
