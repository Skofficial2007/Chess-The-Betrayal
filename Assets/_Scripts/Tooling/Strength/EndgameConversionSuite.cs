using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tooling.Strength
{
    /// <summary>
    /// Hand-authored won-but-not-immediate endgames — the instrument that answers "does the AI
    /// actually finish games it has already won," which YardstickSuite's single-move tactics can't:
    /// those prove the AI finds a mate or a clean capture when one exists on the board right now,
    /// never whether it can drive a lone king across the board over dozens of moves, or walk a pawn
    /// home. Every position here is proven won by EndgameConversionProofTests at authoring time.
    ///
    /// Deliberately small, the same discipline YardstickSuite documents: one genuinely airtight KRK,
    /// KQK, and KPK position each, plus one Defection-specific case, is enough to answer the question
    /// it exists to answer — a large suite nobody verified would be worse than none.
    /// </summary>
    public static class EndgameConversionSuite
    {
        private static Vector2Int At(string algebraic) => BoardSetup.AlgebraicToVector(algebraic);

        public static IReadOnlyList<EndgameConversionPosition> All { get; } = new List<EndgameConversionPosition>
        {
            new EndgameConversionPosition(
                "KingAndRookVsKing",
                ConversionGoal.DriveLoneKingToMate,
                Team.White,
                "Textbook King+Rook vs bare King, both kings centralized and the lone king with the whole board to run in — the hardest starting shape for this technique, not a king already boxed in a corner.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("b3", Team.White, ChessPieceType.Rook)
                    .WithPiece("e5", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithBetrayalRight(false)
                    .WithComputedHash()),

            new EndgameConversionPosition(
                "KingAndQueenVsKing",
                ConversionGoal.DriveLoneKingToMate,
                Team.White,
                "King+Queen vs bare King — the fastest-converting mating technique, included as the easier control case alongside the harder Rook ending.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("b3", Team.White, ChessPieceType.Queen)
                    .WithPiece("e5", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithBetrayalRight(false)
                    .WithComputedHash()),

            new EndgameConversionPosition(
                "KingAndPawnRace",
                ConversionGoal.PromoteThePawn,
                Team.White,
                "White's passed a-pawn is far enough advanced and far enough from Black's king that no legal defense catches it — a clean, unstoppable king-and-pawn promotion race with kings out of each other's way.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("a5", Team.White, ChessPieceType.Pawn)
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("h8", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithBetrayalRight(false)
                    .WithComputedHash()),

            new EndgameConversionPosition(
                "DefectedRookVsKing",
                ConversionGoal.DriveLoneKingToMate,
                Team.White,
                "The same King+Rook shape as KingAndRookVsKing, but the rook started the position on Black's side of a spent Betrayal right and DefectPiece flipped it to White mid-setup — proves the winning side's material is read live from the piece's CURRENT team, not a fixture that only ever places pieces on their 'natural' side.",
                () => BuildDefectedRookPosition()),
        };

        private static BoardState BuildDefectedRookPosition()
        {
            BoardState board = BoardSetup.CreateEmpty()
                .WithPiece("a1", Team.White, ChessPieceType.King)
                .WithPiece("b3", Team.Black, ChessPieceType.Rook)
                .WithPiece("e5", Team.Black, ChessPieceType.King)
                .WithTurn(Team.White)
                .WithBetrayalRight(false);

            // The right is already spent (BetrayalRightAvailable = false above) — this fixture is
            // about material bookkeeping after a defection resolved, not about a live Betrayal
            // sequence, so DefectPiece is applied directly rather than played through an Act.
            board.DefectPiece(At("b3"));
            board.ComputeFullZobristHash();
            return board;
        }
    }
}
