using NUnit.Framework;
using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Movement;
using ChessTheBetrayal.Tests.Utilities;

namespace ChessTheBetrayal.Tests.EditMode.Core.Engine.Betrayal
{
    /// <summary>
    /// Guards the performance rewrite of ChessEngine.GetBetrayalTargets — from an
    /// O(friendly-pieces²)-per-node "disguise trick" (flip each candidate victim to the enemy team,
    /// re-run the betrayer's raw moves, restore) to a single O(pieces) attack-map scan — by asserting
    /// the two produce the SAME set of Act-target squares across many randomized positions.
    ///
    /// The old algorithm is re-created here as an independent oracle (<see cref="ReferenceActTargets"/>)
    /// so any behavioural drift in the production single-scan path fails this test, not just a
    /// hand-picked example. This is deliberately black-box against the public GetBetrayalTargets seam.
    /// </summary>
    [TestFixture]
    public class BetrayalActTargetParityTests
    {
        private static readonly ChessPieceType[] BetrayerTypes =
        {
            ChessPieceType.Pawn, ChessPieceType.Knight, ChessPieceType.Bishop,
            ChessPieceType.Rook, ChessPieceType.Queen
        };

        /// <summary>
        /// The pre-rewrite semantics, reproduced exactly: for each friendly non-King candidate victim,
        /// disguise it as the enemy team, ask the betrayer's raw-move generator whether it can now
        /// reach that square, restore, and (if reachable) keep it when it doesn't self-check. Safe to
        /// mutate the board in place here because each test owns its board on a single thread.
        /// </summary>
        private static HashSet<Vector2Int> ReferenceActTargets(BoardState board, Vector2Int betrayerPos)
        {
            var targets = new HashSet<Vector2Int>();
            PieceData piece = board.GetPiece(betrayerPos);

            if (piece.Type == ChessPieceType.King || !board.BetrayalRightAvailable || board.CurrentTurn != piece.Team)
                return targets;

            IPieceMovement strategy = MovementFactory.GetStrategy(piece.Type);
            if (strategy == null) return targets;

            Team enemyTeam = piece.Team == Team.White ? Team.Black : Team.White;

            // Snapshot the friendly squares up front — mutating the board below edits the live index list.
            var friendlySquares = new List<Vector2Int>();
            foreach (int idx in board.GetPieceIndices(piece.Team))
                friendlySquares.Add(new Vector2Int(idx % board.TileCountX, idx / board.TileCountX));

            var raw = new List<MoveCommand>();

            foreach (Vector2Int victimPos in friendlySquares)
            {
                PieceData victim = board.GetPiece(victimPos);
                if (victim.Type == ChessPieceType.King || victimPos == betrayerPos) continue;

                board.SetPiece(victim.WithTeam(enemyTeam), victimPos.x, victimPos.y);
                bool reachable = false;
                try
                {
                    raw.Clear();
                    strategy.GetRawMoves(board, piece, betrayerPos, raw);
                    for (int j = 0; j < raw.Count; j++)
                    {
                        if (raw[j].EndPosition == victimPos) { reachable = true; break; }
                    }
                }
                finally
                {
                    board.SetPiece(victim, victimPos.x, victimPos.y);
                }

                if (!reachable) continue;

                MoveCommand actMove = MoveCommand.CreateStandardMove(betrayerPos, victimPos, piece, victim, board)
                                                 .WithStage(BetrayalStage.Act);

                // Mirror the production self-check filter via the public engine seam so the oracle and
                // the code-under-test share exactly one definition of "leaves own king in check".
                if (!LeavesKingInCheck(board, actMove))
                    targets.Add(victimPos);
            }

            return targets;
        }

        private static bool LeavesKingInCheck(BoardState board, MoveCommand move)
        {
            ChessEngine.ApplyMoveToBoard(board, move, recordHistory: false);
            bool inCheck = ChessEngine.IsKingInCheck(board, move.PieceTeam);
            ChessEngine.UndoMoveOnBoard(board, move, recordHistory: false);
            return inCheck;
        }

        private static HashSet<Vector2Int> ProductionActTargets(BoardState board, Vector2Int betrayerPos)
        {
            var output = new List<MoveCommand>();
            ChessEngine.GetBetrayalTargets(board, betrayerPos, output);

            var set = new HashSet<Vector2Int>();
            foreach (MoveCommand m in output)
            {
                Assert.That(m.Stage, Is.EqualTo(BetrayalStage.Act), "Every generated target must be tagged as an Act.");
                set.Add(m.EndPosition);
            }
            return set;
        }

        /// <summary>
        /// A tiny deterministic LCG so the "random" positions are reproducible across runs and
        /// platforms (no reliance on System.Random's implementation).
        /// </summary>
        private sealed class Lcg
        {
            private uint _state;
            public Lcg(uint seed) => _state = seed == 0 ? 1u : seed;
            public int Next(int maxExclusive)
            {
                _state = _state * 1664525u + 1013904223u;
                return (int)(_state >> 8) % maxExclusive;
            }
        }

        private static BoardState RandomBetrayalPosition(Lcg rng, int pieceCount)
        {
            BoardState board = TestBoardSetupUtility.CreateEmpty();

            // Kings are mandatory for legality checks; place them on fixed, mutually safe squares.
            board = board.WithPiece("a1", Team.White, ChessPieceType.King)
                         .WithPiece("h8", Team.Black, ChessPieceType.King);

            var occupied = new HashSet<Vector2Int>
            {
                TestBoardSetupUtility.AlgebraicToVector("a1"),
                TestBoardSetupUtility.AlgebraicToVector("h8")
            };

            int placed = 0, guard = 0;
            while (placed < pieceCount && guard++ < 1000)
            {
                int x = rng.Next(8);
                int y = rng.Next(8);
                var pos = new Vector2Int(x, y);
                if (occupied.Contains(pos)) continue;

                // Never place a pawn on the promotion ranks (illegal resting square).
                Team team = rng.Next(2) == 0 ? Team.White : Team.Black;
                ChessPieceType type = BetrayerTypes[rng.Next(BetrayerTypes.Length)];
                if (type == ChessPieceType.Pawn && (y == 0 || y == 7)) continue;

                board.SetPiece(new PieceData(team, type, team == Team.White ? 1 : -1, y, hasMoved: true), x, y);
                occupied.Add(pos);
                placed++;
            }

            board = board.WithTurn(Team.White).WithBetrayalRight(true).WithComputedHash();
            return board;
        }

        [Test]
        public void GetBetrayalTargets_MatchesDisguiseTrickOracle_AcrossRandomPositions()
        {
            int positionsChecked = 0, betrayersChecked = 0;

            for (uint seed = 1; seed <= 300; seed++)
            {
                var rng = new Lcg(seed);
                BoardState board = RandomBetrayalPosition(rng, pieceCount: 10);
                positionsChecked++;

                // Snapshot White's squares (the side to move) — every friendly non-King piece is a
                // potential betrayer this turn.
                var whiteSquares = new List<Vector2Int>();
                foreach (int idx in board.GetPieceIndices(Team.White))
                    whiteSquares.Add(new Vector2Int(idx % board.TileCountX, idx / board.TileCountX));

                foreach (Vector2Int betrayerPos in whiteSquares)
                {
                    if (board.GetPiece(betrayerPos).Type == ChessPieceType.King) continue;

                    HashSet<Vector2Int> expected = ReferenceActTargets(board, betrayerPos);
                    HashSet<Vector2Int> actual = ProductionActTargets(board, betrayerPos);
                    betrayersChecked++;

                    Assert.That(actual, Is.EquivalentTo(expected),
                        $"Act-target mismatch (seed {seed}) for betrayer at ({betrayerPos.x},{betrayerPos.y}).");
                }
            }

            // Sanity: the sweep actually exercised meaningful volume, and left the board consistent.
            Assert.That(positionsChecked, Is.EqualTo(300));
            Assert.That(betrayersChecked, Is.GreaterThan(500),
                "The random sweep should have probed hundreds of betrayer piece placements.");
        }
    }
}
