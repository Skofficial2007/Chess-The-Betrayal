using ChessTheBetrayal.AI.Positions;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Tooling;

namespace ChessTheBetrayal.Tests.EditMode.AI
{
    /// <summary>
    /// The five boards the depth work measures against: two quiet, one tactical, one with a live
    /// Betrayal right, one endgame. Several fixtures need the same five, and they used to live on
    /// the recording harness that first built them — which is marked [Explicit] and so never runs
    /// in a normal pass, while three of the fixtures depending on it do. Nothing broke, since an
    /// [Explicit] class still compiles, but a harness nobody runs was owning positions that
    /// everyday tests could not do without.
    ///
    /// Two of them delegate to the shipping AI assembly rather than placing pieces here, because
    /// the on-device benchmark needs those same boards and runs where an editor-only test class
    /// does not exist. DepthWallPositionsTests pins that the delegation still hands back the same
    /// boards it always did.
    /// </summary>
    internal static class SearchProfilePositions
    {
        /// <summary>A developed middlegame with both sides castled, no captures pending — the "quiet"
        /// baseline. Betrayal right is live so the tree carries the real Act/Defection branching.
        /// Built in the AI assembly (DepthWallPositions) rather than here, because the on-device
        /// benchmark needs this same position and runs on a phone, where this editor-only fixture
        /// does not exist.</summary>
        internal static BoardState QuietMidgame() => DepthWallPositions.QuietMidgame();

        /// <summary>A sharp middlegame with pieces en prise and open lines — captures and recaptures
        /// available, so the quiescence tail is far larger than the quiet position's.</summary>
        internal static BoardState TacticalMidgame() =>
            BoardSetup.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d1", Team.White, ChessPieceType.Queen)
                .WithPiece("h4", Team.Black, ChessPieceType.Queen)
                .WithPiece("e1", Team.White, ChessPieceType.Rook)
                .WithPiece("e8", Team.Black, ChessPieceType.Rook)
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("e5", Team.Black, ChessPieceType.Knight)
                .WithPiece("c4", Team.White, ChessPieceType.Bishop)
                .WithPiece("c5", Team.Black, ChessPieceType.Bishop)
                .WithPiece("f3", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("d6", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        /// <summary>A Betrayal-live middlegame with the pieces packed near both kings, where an Act and
        /// its forced follow-up are genuinely on the table — the shape whose tree the earlier
        /// benchmarks understated because they measured a quiet opening instead.</summary>
        internal static BoardState BetrayalLiveMidgame() =>
            BoardSetup.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("e2", Team.White, ChessPieceType.Queen)
                .WithPiece("e7", Team.Black, ChessPieceType.Queen)
                .WithPiece("d1", Team.White, ChessPieceType.Rook)
                .WithPiece("d8", Team.Black, ChessPieceType.Rook)
                .WithPiece("f1", Team.White, ChessPieceType.Rook)
                .WithPiece("f8", Team.Black, ChessPieceType.Rook)
                .WithPiece("e3", Team.White, ChessPieceType.Bishop)
                .WithPiece("e6", Team.Black, ChessPieceType.Bishop)
                .WithPiece("d4", Team.White, ChessPieceType.Knight)
                .WithPiece("d5", Team.Black, ChessPieceType.Knight)
                .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        /// <summary>A reduced-material endgame with far fewer pieces than the three middlegame
        /// positions above — a king-and-pawns-plus-minor-pieces ending with no immediate captures.
        /// Fewer pieces means a different kind of tree: less material to trade off in quiescence, but
        /// a longer horizon before anything decisive happens, which the deep tiers meet just as often
        /// as a middlegame in real play.</summary>
        internal static BoardState QuietEndgame() =>
            BoardSetup.CreateEmpty()
                .WithPiece("g1", Team.White, ChessPieceType.King)
                .WithPiece("g8", Team.Black, ChessPieceType.King)
                .WithPiece("d3", Team.White, ChessPieceType.Rook)
                .WithPiece("d6", Team.Black, ChessPieceType.Rook)
                .WithPiece("e3", Team.White, ChessPieceType.Bishop)
                .WithPiece("e6", Team.Black, ChessPieceType.Knight)
                .WithPiece("a2", Team.White, ChessPieceType.Pawn)
                .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                .WithTurn(Team.White)
                .WithBetrayalRight(true)
                .WithComputedHash();

        /// <summary>A second quiet middlegame, structurally different from QuietMidgame above: an
        /// open f-file with rooks already facing off on it, unbalanced pawns (White has an isolated
        /// d-pawn, Black a backward one), no piece en prise. This exists to separate "quiet costs a
        /// lot at depth 9" from "this one specific quiet board costs a lot" — if both quiet positions
        /// show the same kind of blowup, the finding is about quiet play, not about one board.
        /// Built in the AI assembly (DepthWallPositions) for the same reason QuietMidgame is
        /// above.</summary>
        internal static BoardState SemiOpenMidgame() => DepthWallPositions.SemiOpenMidgame();
    }
}
