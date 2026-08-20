using System.Collections.Generic;
using NUnit.Framework;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Tooling;
using ChessTheBetrayal.Tooling.Strength;

namespace ChessTheBetrayal.Tests.EditMode.Tooling.Strength
{
    /// <summary>
    /// The authoring-time proof every YardstickSuite entry must pass before it's trusted to judge
    /// the AI. A position that fails here is a fixture-authoring bug, not something to accommodate
    /// — an unprovable position gets fixed or deleted, never kept with a looser standard (see
    /// YardstickProofClass's own doc comment). This is what makes "known-correct" mean something
    /// concrete for a variant with no external engine to defer to: mate is verified by the move
    /// generator actually reaching checkmate, and a "forced" material gain is verified by checking
    /// every legal alternative, not asserted by inspection.
    /// </summary>
    [TestFixture]
    public class YardstickPositionProofTests
    {
        private static readonly IChessEngine Engine = new ChessEngineAdapter();

        private static IEnumerable<TestCaseData> ForcedMatePositions()
        {
            foreach (YardstickPosition position in YardstickSuite.All)
                if (position.ProofClass == YardstickProofClass.ForcedMate)
                    yield return new TestCaseData(position).SetName($"ForcedMate_{position.Name}");
        }

        [TestCaseSource(nameof(ForcedMatePositions))]
        public void ForcedMatePosition_ExpectedMove_ActuallyDeliversCheckmate(YardstickPosition position)
        {
            BoardState board = position.BuildBoard();
            Team mover = board.CurrentTurn;
            Team opponent = mover == Team.White ? Team.Black : Team.White;

            bool found = TryFindLegalMove(board, mover, position, out MoveCommand move);
            Assert.That(found, Is.True,
                $"{position.Name}: the expected move {position.ExpectedMoveDescription} isn't in the legal move list — the position or the expected move is wrong.");

            // Advance, not the raw ApplyMove/UndoMove make-half a search uses internally — Advance
            // is what actually flips CurrentTurn, and EvaluateGameState's legality checks read
            // CurrentTurn. A raw ApplyMove leaves the turn unflipped, which silently makes every
            // legal-move check below query the wrong side and can misreport a real mate as merely
            // Check — this bit an early draft of this exact test.
            Engine.Advance(board, move);
            GameState resultingState = Engine.EvaluateGameState(board, opponent);
            Assert.That(resultingState, Is.EqualTo(GameState.Checkmate),
                $"{position.Name}: {position.ExpectedMoveDescription} was claimed as a forced mate but the resulting position is {resultingState}, not Checkmate.");
        }

        [TestCaseSource(nameof(ForcedMatePositions))]
        public void ForcedMatePosition_ExpectedMove_IsTheOnlyMoveThatMates(YardstickPosition position)
        {
            // A yardstick position asserting an EXACT move must have exactly one answer — if a
            // second legal move also mates immediately, matching the AI to one specific move by
            // coordinates is unfair: it might correctly choose the OTHER mate and fail this
            // position for no real reason. This is the check that would have caught that before the
            // AI ever ran against it.
            BoardState originalBoard = position.BuildBoard();
            Team mover = originalBoard.CurrentTurn;
            Team opponent = mover == Team.White ? Team.Black : Team.White;

            var legalMoves = new List<MoveCommand>(32);
            Engine.GetAllLegalMovesIncludingBetrayal(originalBoard, mover, legalMoves);

            var otherMatingMoves = new List<string>();
            foreach (MoveCommand candidate in legalMoves)
            {
                if (position.Matches(candidate)) continue;

                // A fresh clone per candidate — Advance isn't cheaply reversible the way the
                // search's raw ApplyMove/UndoMove pair is, so isolate each check on its own copy
                // rather than trying to undo a full turn resolution.
                BoardState candidateBoard = originalBoard.CloneForSnapshot();
                Engine.Advance(candidateBoard, candidate);
                GameState resultingState = Engine.EvaluateGameState(candidateBoard, opponent);

                if (resultingState == GameState.Checkmate)
                    otherMatingMoves.Add($"{candidate.StartPosition}->{candidate.EndPosition}");
            }

            Assert.That(otherMatingMoves, Is.Empty,
                $"{position.Name}: expects the unique move {position.ExpectedMoveDescription}, but these other legal moves ALSO deliver " +
                $"immediate mate: {string.Join(", ", otherMatingMoves)} — the position doesn't have a unique answer as authored.");
        }

        private static IEnumerable<TestCaseData> ForcedMaterialGainPositions()
        {
            foreach (YardstickPosition position in YardstickSuite.All)
                if (position.ProofClass == YardstickProofClass.ForcedMaterialGain || position.ProofClass == YardstickProofClass.BetrayalTrap)
                    yield return new TestCaseData(position).SetName($"ForcedMaterialGain_{position.Name}");
        }

        [TestCaseSource(nameof(ForcedMaterialGainPositions))]
        public void ForcedMaterialGainPosition_ExpectedMove_IsACleanCaptureNoAlternativeMatches(YardstickPosition position)
        {
            BoardState board = position.BuildBoard();
            Team mover = board.CurrentTurn;
            Team opponent = mover == Team.White ? Team.Black : Team.White;

            var legalMoves = new List<MoveCommand>(32);
            Engine.GetAllLegalMovesIncludingBetrayal(board, mover, legalMoves);

            MoveCommand? expected = null;
            foreach (MoveCommand candidate in legalMoves)
                if (position.Matches(candidate)) { expected = candidate; break; }

            Assert.That(expected, Is.Not.Null,
                $"{position.Name}: the expected move {position.ExpectedMoveDescription} isn't in the legal move list.");

            Assert.That(expected.Value.HasCapture, Is.True,
                $"{position.Name}: ForcedMaterialGain requires the expected move to capture something.");

            int expectedGain = PieceValue(expected.Value.CapturedType);

            Engine.ApplyMove(board, expected.Value);
            bool expectedDestinationIsSafe = !ChessEngine.IsSquareUnderAttack(board, expected.Value.EndPosition, opponent);
            Engine.UndoMove(board, expected.Value);

            Assert.That(expectedDestinationIsSafe, Is.True,
                $"{position.Name}: the expected move lands on a square the opponent can recapture on — not a clean, unconditional gain.");

            foreach (MoveCommand alternative in legalMoves)
            {
                if (position.Matches(alternative)) continue;

                int alternativeGain = 0;
                if (alternative.HasCapture)
                {
                    Engine.ApplyMove(board, alternative);
                    bool alternativeDestinationIsSafe = !ChessEngine.IsSquareUnderAttack(board, alternative.EndPosition, opponent);
                    Engine.UndoMove(board, alternative);

                    // Only a capture that also can't be immediately recaptured competes on the same
                    // "clean, unconditional" terms the expected move is being held to.
                    if (alternativeDestinationIsSafe)
                        alternativeGain = PieceValue(alternative.CapturedType);
                }

                Assert.That(alternativeGain, Is.LessThan(expectedGain),
                    $"{position.Name}: alternative move {alternative.StartPosition}->{alternative.EndPosition} " +
                    $"cleanly wins {alternativeGain} material, which is not less than the expected move's {expectedGain} — " +
                    "the expected move isn't actually the unique best answer.");
            }
        }

        private static IEnumerable<TestCaseData> QuietPositionalGainPositions()
        {
            bool any = false;
            foreach (YardstickPosition position in YardstickSuite.All)
            {
                if (position.ProofClass != YardstickProofClass.QuietPositionalGain) continue;
                any = true;
                yield return new TestCaseData(position).SetName($"QuietPositionalGain_{position.Name}");
            }

            // NUnit fails a source-driven test outright when its source is empty, rather than
            // reporting nothing to do. A null case keeps the fixture honest while the suite happens
            // to hold no position of this class — the test itself treats it as "nothing to prove".
            if (!any)
                yield return new TestCaseData((YardstickPosition)null).SetName("QuietPositionalGain_NoPositionsOfThisClassYet");
        }

        [TestCaseSource(nameof(QuietPositionalGainPositions))]
        public void QuietPositionalGainPosition_ExpectedMove_WinsMaterialByForceNoAlternativeMatches(YardstickPosition position)
        {
            if (position == null)
            {
                Assert.Pass("The suite currently holds no QuietPositionalGain positions.");
                return;
            }


            // The proof every position in this class rests on: search every legal move with an
            // evaluator that can see material and nothing else, and require the expected move to
            // come out ahead by the position's stated margin. Because the proving evaluator is
            // blind to pawn structure, king safety and king approach, a pass here cannot have been
            // manufactured by the very terms this position exists to measure.
            BoardState board = position.BuildBoard();

            Assert.That(position.ProvingDepth, Is.GreaterThan(0),
                $"{position.Name}: a QuietPositionalGain position must state the depth its proof searches to.");
            Assert.That(position.ProvingMarginCp, Is.GreaterThan(0),
                $"{position.Name}: a QuietPositionalGain position must state the margin its expected move has to beat.");

            List<ProvenMoveScore> scored = QuietMoveProver.ScoreAllMoves(board, position.ProvingDepth);

            Assert.That(scored, Is.Not.Empty, $"{position.Name}: no legal moves in the position as authored.");

            ProvenMoveScore best = scored[0];
            Assert.That(position.Matches(best.Move), Is.True,
                $"{position.Name}: expected {position.ExpectedMoveDescription} to be the best move by forced material at depth " +
                $"{position.ProvingDepth}, but {best} scores higher. Full ranking: {Describe(scored)}");

            // The expected move is best; now confirm it is best by enough of a margin that choosing
            // the runner-up would be a real mistake rather than a defensible difference of opinion.
            ProvenMoveScore runnerUp = default;
            bool foundRunnerUp = false;
            for (int i = 1; i < scored.Count; i++)
            {
                if (position.Matches(scored[i].Move)) continue;
                runnerUp = scored[i];
                foundRunnerUp = true;
                break;
            }

            if (!foundRunnerUp) return; // Only one legal move: nothing to be better than.

            int margin = best.ScoreCp - runnerUp.ScoreCp;
            Assert.That(margin, Is.GreaterThanOrEqualTo(position.ProvingMarginCp),
                $"{position.Name}: {position.ExpectedMoveDescription} beats the next best move {runnerUp} by only {margin}cp, " +
                $"short of the required {position.ProvingMarginCp}cp — the position does not have a sharply best answer. " +
                $"Full ranking: {Describe(scored)}");
        }

        /// <summary>The proof's full ranking, so a failure shows what the search actually preferred
        /// rather than only that it disagreed.</summary>
        private static string Describe(List<ProvenMoveScore> scored)
        {
            var parts = new List<string>(scored.Count);
            for (int i = 0; i < scored.Count && i < 8; i++) parts.Add(scored[i].ToString());
            if (scored.Count > 8) parts.Add($"... ({scored.Count - 8} more)");
            return string.Join(", ", parts);
        }

        private static bool TryFindLegalMove(BoardState board, Team mover, YardstickPosition position, out MoveCommand found)
        {
            var legalMoves = new List<MoveCommand>(32);
            Engine.GetAllLegalMovesIncludingBetrayal(board, mover, legalMoves);

            foreach (MoveCommand candidate in legalMoves)
            {
                if (position.Matches(candidate))
                {
                    found = candidate;
                    return true;
                }
            }

            found = default;
            return false;
        }

        /// <summary>Standard relative values, used only to rank alternatives against each other
        /// within a single proof check — not the production evaluator's material table.</summary>
        private static int PieceValue(ChessPieceType type) => type switch
        {
            ChessPieceType.Pawn => 1,
            ChessPieceType.Knight => 3,
            ChessPieceType.Bishop => 3,
            ChessPieceType.Rook => 5,
            ChessPieceType.Queen => 9,
            _ => 0
        };
    }
}
