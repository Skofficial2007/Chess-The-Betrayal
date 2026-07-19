namespace ChessTheBetrayal.Tests.Utilities
{
    /// <summary>
    /// Dials for MatchAdjudicator's early-stopping rules. These are harness-only arbiter rules —
    /// the same kind of thing cutechess/OpenBench apply around an engine match — not a domain
    /// rulebook change; the live game itself still has no threefold or fifty-move detection, and
    /// adding that is a separate, deliberate future decision, not a side effect of speeding up
    /// this tooling.
    ///
    /// A tournament playing many games against deterministic (zero-blunder) high tiers otherwise
    /// wastes real search time replaying a position both sides already agree is drawn or decided,
    /// all the way out to the ply cap — these rules let a game end the moment that's true instead.
    /// </summary>
    public readonly struct AdjudicationRules
    {
        /// <summary>A position repeated this many times (by Zobrist hash, counting the position
        /// this many times total across the game) is an immediate draw. 3 is the standard chess
        /// threefold-repetition threshold.</summary>
        public readonly int ThreefoldRepetitionCount;

        /// <summary>This many plies with no capture and no pawn move is an immediate draw — the
        /// classic fifty-MOVE rule expressed in plies (50 full moves = 100 plies).</summary>
        public readonly int FiftyMoveRulePlies;

        /// <summary>Only positions from this ply onward are eligible for score-margin
        /// adjudication — too early in the game a large evaluation swing is common and not yet a
        /// reliable signal that the outcome is settled.</summary>
        public readonly int MinPlyForScoreAdjudication;

        /// <summary>A |score| at or above this many centipawns, sustained for
        /// WinAdjudicationConsecutivePlies in a row, adjudicates a win for whichever side the
        /// score favors.</summary>
        public readonly int WinAdjudicationMarginCp;

        public readonly int WinAdjudicationConsecutivePlies;

        /// <summary>A |score| at or below this many centipawns, sustained for
        /// DrawAdjudicationConsecutivePlies in a row, adjudicates a draw.</summary>
        public readonly int DrawAdjudicationMarginCp;

        public readonly int DrawAdjudicationConsecutivePlies;

        public AdjudicationRules(
            int threefoldRepetitionCount, int fiftyMoveRulePlies, int minPlyForScoreAdjudication,
            int winAdjudicationMarginCp, int winAdjudicationConsecutivePlies,
            int drawAdjudicationMarginCp, int drawAdjudicationConsecutivePlies)
        {
            ThreefoldRepetitionCount = threefoldRepetitionCount;
            FiftyMoveRulePlies = fiftyMoveRulePlies;
            MinPlyForScoreAdjudication = minPlyForScoreAdjudication;
            WinAdjudicationMarginCp = winAdjudicationMarginCp;
            WinAdjudicationConsecutivePlies = winAdjudicationConsecutivePlies;
            DrawAdjudicationMarginCp = drawAdjudicationMarginCp;
            DrawAdjudicationConsecutivePlies = drawAdjudicationConsecutivePlies;
        }

        /// <summary>The rules a real tournament/benchmark run applies by default. Margins are
        /// deliberately conservative (a wide win margin held for many plies in a row, not a single
        /// noisy reading) so adjudication only fires on positions genuinely no longer in doubt —
        /// see MatchSimulatorTests for cases pinning that a merely-better-but-still-contested
        /// position is NOT adjudicated away.</summary>
        public static readonly AdjudicationRules Standard = new AdjudicationRules(
            threefoldRepetitionCount: 3,
            fiftyMoveRulePlies: 100,
            minPlyForScoreAdjudication: 20,
            winAdjudicationMarginCp: 500,
            winAdjudicationConsecutivePlies: 8,
            drawAdjudicationMarginCp: 20,
            drawAdjudicationConsecutivePlies: 12);

        /// <summary>Every early-stopping rule disabled — every game plays to a real
        /// checkmate/stalemate or the ply cap, exactly like the tooling behaved before this type
        /// existed. Exists for tests/debugging that want the ply cap as the only exit.</summary>
        public static readonly AdjudicationRules Disabled = new AdjudicationRules(
            threefoldRepetitionCount: int.MaxValue,
            fiftyMoveRulePlies: int.MaxValue,
            minPlyForScoreAdjudication: int.MaxValue,
            winAdjudicationMarginCp: int.MaxValue,
            winAdjudicationConsecutivePlies: int.MaxValue,
            drawAdjudicationMarginCp: -1,
            drawAdjudicationConsecutivePlies: int.MaxValue);
    }
}
