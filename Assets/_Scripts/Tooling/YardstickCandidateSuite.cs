using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;

namespace ChessTheBetrayal.Tooling
{
    /// <summary>
    /// Positions proposed for the yardstick but not yet admitted to it, together with the screening
    /// probe's verdict on each.
    ///
    /// This exists because a rejected candidate is a result worth keeping. Screening is expensive
    /// and its answers are not obvious in advance — whether a position's best move holds still
    /// across depths, and whether the search can reach the depth that proves it, are things only a
    /// measurement can tell you. Deleting the rejects would mean the next person to grow the suite
    /// pays for the same measurement again and re-authors the same unusable positions.
    ///
    /// Nothing here is trusted by any strength test. A candidate earns its way into YardstickSuite
    /// by passing both gates; until then it lives here and is only ever read by the screening probe.
    ///
    /// MEASURED OUTCOME, across all sixteen candidates below: none is usable, and the reason is a
    /// property of the method rather than of the positions. A proof standard has to be blind to the
    /// evaluation terms it is vetting positions for, or it proves only that the evaluator agrees
    /// with itself. Material is the one standard that qualifies. But a position whose answer turns
    /// on pawn structure or king safety is precisely one where material is level, so a material-only
    /// search rates every reasonable move identically — eleven of the first twelve candidates came
    /// back with an exact 0cp margin across many moves.
    ///
    /// Searching deeper does not rescue it. Once a pawn queens in every line the scores converge on
    /// the promotion value instead of separating, and the only candidate that ever did separate did
    /// so on a hanging pawn — a tactic the top tier already solves in 21ms, which by the suite's own
    /// standard adds no resolution at all.
    ///
    /// Kept in full because that is a real and reusable result: it says this class of position does
    /// not exist for this engine under a material proof standard, and anyone proposing to grow the
    /// yardstick that way can see the measurement rather than repeat it.
    /// </summary>
    public static class YardstickCandidateSuite
    {
        private static Vector2Int At(string algebraic) => BoardSetup.AlgebraicToVector(algebraic);

        /// <summary>One proposed position and the reasoning behind proposing it.</summary>
        public sealed class Candidate
        {
            public readonly string Name;
            public readonly string Rationale;
            public readonly System.Func<BoardState> BuildBoard;
            public readonly Vector2Int ExpectedFrom;
            public readonly Vector2Int ExpectedTo;

            /// <summary>The depth the material-only proof needs in order to see this move's payoff.
            /// A guess at authoring time; the probe reports whether it was right.</summary>
            public readonly int ProposedProvingDepth;

            public Candidate(string name, string rationale, System.Func<BoardState> buildBoard,
                Vector2Int expectedFrom, Vector2Int expectedTo, int proposedProvingDepth)
            {
                Name = name;
                Rationale = rationale;
                BuildBoard = buildBoard;
                ExpectedFrom = expectedFrom;
                ExpectedTo = expectedTo;
                ProposedProvingDepth = proposedProvingDepth;
            }
        }

        public static IReadOnlyList<Candidate> All { get; } = new List<Candidate>
        {
            // --- Passed-pawn races. The pawn-structure term's whole job is to notice that an
            // unstoppable pawn is worth more than the material on the board says. A search that
            // could not read structure would have to see the promotion itself to prefer these.
            new Candidate(
                "OutsidePassedPawnOutruns",
                "White's a-pawn is passed and far from the black king; the h-pawn is not. Pushing the "
                + "outside passer queens by force while any other move lets the king catch it.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e1", Team.White, ChessPieceType.King)
                    .WithPiece("a4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("h4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("e8", Team.Black, ChessPieceType.King)
                    .WithPiece("h5", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("a4"), At("a5"), 8),

            new Candidate(
                "ProtectedPasserMustAdvance",
                "A protected passed pawn on d5 supported by c4. Advancing it is decisive; anything "
                + "else lets Black blockade with the king.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e2", Team.White, ChessPieceType.King)
                    .WithPiece("d5", Team.White, ChessPieceType.Pawn)
                    .WithPiece("c4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("f7", Team.Black, ChessPieceType.King)
                    .WithPiece("b6", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("d5"), At("d6"), 8),

            new Candidate(
                "PawnBreakCreatesAPasser",
                "The b5 break cracks Black's structure and manufactures a passer out of a blocked "
                + "position; every quiet alternative leaves the position sealed.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("g1", Team.White, ChessPieceType.King)
                    .WithPiece("a4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("b4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("c4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("g8", Team.Black, ChessPieceType.King)
                    .WithPiece("a5", Team.Black, ChessPieceType.Pawn)
                    .WithPiece("b6", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("b4"), At("b5"), 8),

            new Candidate(
                "RookPawnAndWrongBishopAvoided",
                "Pushing the f-pawn rather than the h-pawn keeps a queening square the bishop "
                + "controls — the h-pawn draws outright.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e4", Team.White, ChessPieceType.King)
                    .WithPiece("c1", Team.White, ChessPieceType.Bishop)
                    .WithPiece("f4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("h4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("g7", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("f4"), At("f5"), 8),

            // --- King approach. The endgame king-approach term rewards walking the king toward a
            // confined enemy king. Without it a search sees every king move as equal until the mate
            // is actually in view.
            new Candidate(
                "KingMustApproachToConvert",
                "Rook endgame where only marching the king forward makes progress; rook shuffles "
                + "keep the material but never convert.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e3", Team.White, ChessPieceType.King)
                    .WithPiece("a7", Team.White, ChessPieceType.Rook)
                    .WithPiece("e6", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("e3"), At("e4"), 8),

            new Candidate(
                "OppositionWinsThePawnEnding",
                "A king-and-pawn ending decided purely by taking the opposition; the natural pawn "
                + "push throws the win away.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e5", Team.White, ChessPieceType.King)
                    .WithPiece("e4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("e7", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("e5"), At("d6"), 8),

            // --- King safety. The king-safety term prices an exposed king and open files near it.
            // These positions ask the search to spend a move on safety with no material in sight.
            new Candidate(
                "ShelterTheKingBeforeItIsTooLate",
                "White must close the open file in front of the king; grabbing the loose pawn "
                + "instead loses to the rook coming in.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("g1", Team.White, ChessPieceType.King)
                    .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("g3", Team.White, ChessPieceType.Pawn)
                    .WithPiece("d1", Team.White, ChessPieceType.Rook)
                    .WithPiece("g8", Team.Black, ChessPieceType.King)
                    .WithPiece("g6", Team.Black, ChessPieceType.Rook)
                    .WithPiece("a6", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("g3"), At("g4"), 8),

            new Candidate(
                "BlockTheOpenFileAgainstTheRook",
                "Interposing on the open file is the only move that holds; every alternative allows "
                + "a decisive invasion on the back rank.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("h1", Team.White, ChessPieceType.King)
                    .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("h2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("a2", Team.White, ChessPieceType.Rook)
                    .WithPiece("h8", Team.Black, ChessPieceType.King)
                    .WithPiece("e5", Team.Black, ChessPieceType.Rook)
                    .WithPiece("b7", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("a2"), At("e2"), 8),

            // --- Structure quality without an immediate passer. These test the doubled/isolated
            // half of the pawn term rather than the passed-pawn half.
            new Candidate(
                "RecaptureTowardTheCentre",
                "Two recaptures are available and only one avoids leaving doubled isolated pawns.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e1", Team.White, ChessPieceType.King)
                    .WithPiece("b2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("d2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("c4", Team.Black, ChessPieceType.Knight)
                    .WithPiece("e8", Team.Black, ChessPieceType.King)
                    .WithPiece("f7", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("b2"), At("c3"), 8),

            new Candidate(
                "AvoidSaddlingYourselfWithDoubledPawns",
                "A capture that doubles White's pawns versus a quiet move that keeps the structure "
                + "whole; the quiet move is worth more than the pawn.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e1", Team.White, ChessPieceType.King)
                    .WithPiece("c2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("d3", Team.White, ChessPieceType.Pawn)
                    .WithPiece("e8", Team.Black, ChessPieceType.King)
                    .WithPiece("c5", Team.Black, ChessPieceType.Pawn)
                    .WithPiece("d5", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("c2"), At("c4"), 8),

            // --- Betrayal-flavoured. A defector near the friendly king is one of the things the
            // king-safety term explicitly prices, so a position turning on it exercises a term no
            // ordinary chess position can reach.
            new Candidate(
                "KeepTheDefectorAwayFromTheKing",
                "With the Betrayal right live, the move that keeps a potential defector out of the "
                + "king's zone is worth more than the material alternative.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("g1", Team.White, ChessPieceType.King)
                    .WithPiece("f2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("e3", Team.White, ChessPieceType.Knight)
                    .WithPiece("g8", Team.Black, ChessPieceType.King)
                    .WithPiece("d8", Team.Black, ChessPieceType.Rook)
                    .WithPiece("a7", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithBetrayalRight(true)
                    .WithComputedHash(),
                At("e3"), At("d5"), 8),

            // --- Near-promotion races. The candidates above put their pawns on the fourth rank,
            // which needs roughly ten plies just to reach the queening square — further than a
            // material-only proof can see, so every move tied. These start much closer, on the
            // sixth and seventh ranks, so the promotion itself lands inside a reachable depth and
            // the proof has a material fact to hold on to. The structural point is unchanged: a
            // search blind to pawn structure has to see the queening to prefer these.
            new Candidate(
                "SeventhRankPasserQueensByForce",
                "A passer one square from promotion with the enemy king a tempo too far away. "
                + "Pushing queens by force; any other move lets the king step across.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("b7", Team.White, ChessPieceType.Pawn)
                    .WithPiece("g4", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("b7"), At("b8"), 6),

            new Candidate(
                "RaceTheRightPawnHome",
                "Two passers, one a tempo ahead of the other. Only the leading pawn queens before "
                + "the king arrives; pushing the wrong one throws the win away.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("a1", Team.White, ChessPieceType.King)
                    .WithPiece("b6", Team.White, ChessPieceType.Pawn)
                    .WithPiece("g2", Team.White, ChessPieceType.Pawn)
                    .WithPiece("f4", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("b6"), At("b7"), 8),

            new Candidate(
                "OutrunTheKingWithTheDistantPasser",
                "The b-pawn is outside the black king's square and the e-pawn is not. Only the "
                + "distant passer promotes.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("h1", Team.White, ChessPieceType.King)
                    .WithPiece("b6", Team.White, ChessPieceType.Pawn)
                    .WithPiece("e5", Team.White, ChessPieceType.Pawn)
                    .WithPiece("e7", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("b6"), At("b7"), 8),

            new Candidate(
                "PushPastTheBlockaderNotAroundIt",
                "A sixth-rank passer with the defending king one file off. The straight push "
                + "promotes; the king move allows the blockade.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("c4", Team.White, ChessPieceType.King)
                    .WithPiece("d6", Team.White, ChessPieceType.Pawn)
                    .WithPiece("f6", Team.Black, ChessPieceType.King)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("d6"), At("d7"), 8),

            new Candidate(
                "CentralisedKnightBeatsTheRimGrab",
                "A knight on the rim versus a central outpost, with a pawn on offer for the "
                + "wrong-side knight.",
                () => BoardSetup.CreateEmpty()
                    .WithPiece("e1", Team.White, ChessPieceType.King)
                    .WithPiece("c3", Team.White, ChessPieceType.Knight)
                    .WithPiece("d4", Team.White, ChessPieceType.Pawn)
                    .WithPiece("e8", Team.Black, ChessPieceType.King)
                    .WithPiece("a6", Team.Black, ChessPieceType.Pawn)
                    .WithPiece("f6", Team.Black, ChessPieceType.Pawn)
                    .WithTurn(Team.White)
                    .WithComputedHash(),
                At("c3"), At("e4"), 8),
        };
    }
}
