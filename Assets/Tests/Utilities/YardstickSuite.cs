using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Hand-authored positions with provably correct answers — an absolute strength signal rather
    /// than only tier-vs-tier, and one that needs no games and runs in seconds. Every position here
    /// is proven admissible by YardstickPositionProofTests at authoring time: a mate really is a
    /// mate, a "forced" material gain really does refute every alternative. A position that can't
    /// pass its own proof doesn't belong in the suite — see YardstickProofClass's own doc comment
    /// for why an unprovable position is inadmissible by construction, not a looser case to keep.
    ///
    /// Deliberately small. A large suite of positions nobody actually verified is worse than a
    /// small one that's genuinely airtight — the whole point of this class of test.
    /// </summary>
    public static class YardstickSuite
    {
        private static Vector2Int At(string algebraic) => TestBoardSetupUtility.AlgebraicToVector(algebraic);

        public static IReadOnlyList<YardstickPosition> All { get; } = new List<YardstickPosition>
        {
            new YardstickPosition(
                "BackRankMateInOne",
                YardstickProofClass.ForcedMate,
                "Black's own pawns wall the king in on the back rank — a rook on the open file mates immediately.",
                () => TestBoardSetupUtility.CreateEmpty()
                    .WithPiece("d1", Team.White, ChessPieceType.Rook)
                    .WithPiece("h1", Team.White, ChessPieceType.King)
                    .WithPiece("g8", Team.Black, ChessPieceType.King)
                    .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                    .WithPiece("g7", Team.Black, ChessPieceType.Pawn)
                    .WithPiece("h7", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("d1"), At("d8")),

            new YardstickPosition(
                "BackRankMateInOneOtherCorner",
                YardstickProofClass.ForcedMate,
                "A second walled-in back-rank mate, mirroring the first one's proven shape from the opposite side of the board — deliberately NOT a bare king-and-rook endgame technique, where several different first moves can all lead to eventual forced mate and score misleadingly close to each other.",
                () => TestBoardSetupUtility.CreateEmpty()
                    .WithPiece("h5", Team.White, ChessPieceType.Rook)
                    .WithPiece("g4", Team.White, ChessPieceType.King)
                    .WithPiece("b8", Team.Black, ChessPieceType.King)
                    .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                    .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                    .WithPiece("c7", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("h5"), At("h8")),

            new YardstickPosition(
                "OnlyCleanCaptureWinsAPiece",
                YardstickProofClass.ForcedMaterialGain,
                "A rook can take an undefended knight cleanly; every other legal move leaves the knight escaping or the rook hanging instead.",
                () => TestBoardSetupUtility.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("a5", Team.White, ChessPieceType.Rook)
                    .WithPiece("h8", Team.Black, ChessPieceType.King)
                    .WithPiece("e5", Team.Black, ChessPieceType.Knight)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("a5"), At("e5")),

            new YardstickPosition(
                "OnlyCleanCaptureWinsTheBishop",
                YardstickProofClass.ForcedMaterialGain,
                "A different geometry from the knight-vs-rook case above: a bishop takes an undefended enemy bishop cleanly, with kings far enough apart that no alternative move comes close to matching it.",
                () => TestBoardSetupUtility.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("b2", Team.White, ChessPieceType.Bishop)
                    .WithPiece("h8", Team.Black, ChessPieceType.King)
                    .WithPiece("f6", Team.Black, ChessPieceType.Bishop)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("b2"), At("f6")),
        };
    }
}
