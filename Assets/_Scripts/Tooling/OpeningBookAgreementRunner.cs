using System.Collections.Generic;
using System.Threading;
using ChessTheBetrayal.AI.Search;
using ChessTheBetrayal.AI.Profiles;
using ChessTheBetrayal.AI.Evaluation;
using ChessTheBetrayal.AI.OpeningBook;
using ChessTheBetrayal.Core.Data;
using ChessTheBetrayal.Core.Engine;
using ChessTheBetrayal.Core.Randomness;
using ChessTheBetrayal.Gameplay.Manager;

namespace ChessTheBetrayal.Tooling
{
    /// <summary>How one tier's own judgement compares with the book's, on positions the book knows.</summary>
    public sealed class BookAgreementResult
    {
        public string ProfileId;
        public int BookDepthPlies;
        public int Positions;

        /// <summary>Positions where the book's move is the one a much deeper search names.</summary>
        public int BookMatchedReference;

        /// <summary>Positions where this tier, searching for itself, names the deeper search's move.</summary>
        public int TierMatchedReference;

        /// <summary>Positions where the tier would have played the book's move anyway — the book
        /// changed nothing there, whether or not either was right.</summary>
        public int BookSameAsTier;

        /// <summary>Positions where the tier ran out of clock before reaching its own depth ceiling.
        /// A disagreement here says the tier was short of time, not that it judged differently.</summary>
        public int TierCutShort;

        public float BookAgreement => Positions == 0 ? 0f : (float)BookMatchedReference / Positions;
        public float TierAgreement => Positions == 0 ? 0f : (float)TierMatchedReference / Positions;

        /// <summary>
        /// The difference between the two figures above. It looks like it measures what the book
        /// buys a tier. It does not — the reference shares the tiers' evaluator and not the book's,
        /// so this is negative by construction no matter how well the book plays. See this type's
        /// runner for the full explanation, and use OpeningBookImpactRunner for the real answer.
        /// </summary>
        public float AgreementGain => BookAgreement - TierAgreement;

        /// <summary>Share of positions where the book changed the move at all. Caps how much
        /// difference the book could possibly have made to this tier either way.</summary>
        public float MoveChangedRate => Positions == 0 ? 0f : 1f - (float)BookSameAsTier / Positions;
    }

    /// <summary>
    /// Describes how often the book and each tier pick the move a much deeper search picks, on
    /// positions the book covers.
    ///
    /// READ THIS BEFORE QUOTING ANY NUMBER THIS PRODUCES. It is a descriptive statistic, and it is
    /// NOT evidence about whether the book plays good moves. The comparison it appears to offer —
    /// book against tier, on a shared yardstick — does not actually work, for a reason that only
    /// became obvious once it had been run:
    ///
    /// The deep reference search uses the same evaluator the tiers use. So "this tier agrees with
    /// the reference" is largely measuring whether a tier agrees with a deeper copy of ITSELF,
    /// which is close to guaranteed. The book shares no evaluator with the reference; it is an
    /// outside opinion. It will therefore agree less than any tier does, whichever of them is
    /// actually playing better chess. Measured, the book landed at 43.8% while the tiers ran from
    /// 50% to 81.3% — a spread that the shared evaluator alone predicts, so nothing about the book
    /// can be read out of it.
    ///
    /// Grading the book properly would need a yardstick that shares nothing with the engine — a
    /// different engine, or a database of real master games. Neither exists in this project. The
    /// measurement that DOES work is playing the book against no book and counting the results;
    /// see OpeningBookImpactRunner, which answers the question this cannot.
    ///
    /// Kept because the sampling and per-tier plumbing are reusable, because the cut-short column
    /// is a genuine observation about how much clock each tier has in the opening, and because a
    /// recorded dead end is cheaper than the next person rebuilding it.
    ///
    /// A second limit worth knowing: positions are sampled across a range of depths regardless of
    /// any tier's book allowance, so for a tier whose repertoire stops early, some sampled
    /// positions are ones it would never have consulted the book for at all.
    /// </summary>
    public static class OpeningBookAgreementRunner
    {
        /// <summary>
        /// Collects distinct positions the book has an answer for, by walking it from the standard
        /// start and stopping at varying depths. Positions are deduplicated by hash, so a common
        /// early position cannot dominate the sample simply by being on many lines.
        /// </summary>
        public static List<BoardState> SampleBookPositions(
            OpeningBookAsset book, IChessEngine engine, int wanted, int seed, int maxWalkPlies)
        {
            var sampled = new List<BoardState>(wanted);
            var seenHashes = new HashSet<ulong>();
            var resolver = new TurnResolver();

            // Bounded rather than looping until `wanted` is met: a book shallower than the walk
            // length would otherwise spin forever looking for positions that do not exist.
            for (int attempt = 0; attempt < wanted * 40 && sampled.Count < wanted; attempt++)
            {
                IRandomSource rng = new SystemRandomSource(seed + attempt);
                int targetPlies = 1 + (attempt % maxWalkPlies);

                BoardState board = StandardStartingPosition();
                bool reachedTarget = true;

                for (int ply = 0; ply < targetPlies; ply++)
                {
                    MoveCommand? step = OpeningBookLookup.TryGetBookMove(book, board, engine, rng);
                    if (step == null) { reachedTarget = false; break; }
                    resolver.Advance(board, step.Value);
                }

                if (!reachedTarget) continue;

                // The position must still be one the book answers, or there is no book move to
                // compare against and it belongs in a different measurement entirely.
                if (OpeningBookLookup.TryGetBookMove(book, board, engine, rng) == null) continue;
                if (!seenHashes.Add(board.ZobristHash)) continue;

                sampled.Add(board);
            }

            return sampled;
        }

        /// <summary>
        /// Grades every tier in <paramref name="roster"/> against <paramref name="oracle"/> on the
        /// given book positions. The oracle caches per position, so the expensive deep search is
        /// paid once no matter how many tiers are compared against it.
        ///
        /// bookMoveSeed fixes the book's own weighted pick so the run reproduces. The book is drawn
        /// the same way a real game draws it rather than taking its highest-weighted entry, because
        /// the question is what the AI actually plays, not what the book most prefers.
        /// </summary>
        public static List<BookAgreementResult> Run(
            OpeningBookAsset book, IReadOnlyList<AIProfile> roster, ReferenceMoveOracle oracle,
            IReadOnlyList<BoardState> positions, int bookMoveSeed = 20260728)
        {
            var engine = new ChessEngineAdapter();
            var results = new List<BookAgreementResult>(roster.Count);

            // Resolved once and shared, so every tier is compared against the same book move and the
            // same reference for a given position.
            var bookMoves = new MoveCommand?[positions.Count];
            var referenceMoves = new ReferenceMove[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                bookMoves[i] = OpeningBookLookup.TryGetBookMove(
                    book, positions[i], engine, new SystemRandomSource(bookMoveSeed + i));
                referenceMoves[i] = oracle.Answer(positions[i]);
            }

            foreach (AIProfile tier in roster)
            {
                var tally = new BookAgreementResult
                {
                    ProfileId = tier.Id,
                    BookDepthPlies = tier.OpeningBookDepthPlies,
                    Positions = positions.Count
                };

                for (int i = 0; i < positions.Count; i++)
                {
                    if (bookMoves[i] == null) continue;

                    MoveCommand bookMove = bookMoves[i].Value;
                    ReferenceMove reference = referenceMoves[i];

                    (MoveCommand tierMove, bool cutShort) = SearchAsTierWould(engine, tier, positions[i]);

                    if (SameMove(bookMove, reference.Move)) tally.BookMatchedReference++;
                    if (SameMove(tierMove, reference.Move)) tally.TierMatchedReference++;
                    if (SameMove(bookMove, tierMove)) tally.BookSameAsTier++;
                    if (cutShort) tally.TierCutShort++;
                }

                results.Add(tally);
            }

            return results;
        }

        /// <summary>
        /// Searches the position exactly as this tier would in a real game — its own ceiling, its
        /// own clock, its own personality dials — because the comparison is against what the tier
        /// would really have played, not what it could manage given more time.
        /// </summary>
        private static (MoveCommand Move, bool CutShort) SearchAsTierWould(
            IChessEngine engine, AIProfile tier, BoardState board)
        {
            var search = new AlphaBetaSearch(engine,
                new BetrayalAwareEvaluator(EvaluationWeights.FromProfile(tier)));
            AISearchSettings settings = AISearchSettings.FromProfile(BetrayalUsage.Full, tier);
            int rescoreMargin = System.Math.Max(tier.BlunderMarginCp, tier.TieBreakWindowCp);

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(settings.TimeBudget.HardMs);
            MoveCommand raw = search.FindBestMove(board.CloneForSnapshot(), settings, cts.Token,
                rescoreMargin, enableInstabilityTimeManagement: true);

            bool cutShort = search.Stats.LastCompletedDepth < settings.MaxDepth;
            return (raw, cutShort);
        }

        private static bool SameMove(MoveCommand a, MoveCommand b) =>
            a.StartPosition == b.StartPosition && a.EndPosition == b.EndPosition && a.Stage == b.Stage;

        private static BoardState StandardStartingPosition()
        {
            var board = new BoardState(8, 8);
            board.Clear();

            ChessPieceType[] backRank =
            {
                ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop, ChessPieceType.Queen,
                ChessPieceType.King, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Rook
            };

            for (int x = 0; x < 8; x++)
            {
                board.SetPiece(new PieceData(Team.White, backRank[x], 1, 0), x, 0);
                board.SetPiece(new PieceData(Team.White, ChessPieceType.Pawn, 1, 1), x, 1);
                board.SetPiece(new PieceData(Team.Black, ChessPieceType.Pawn, -1, 6), x, 6);
                board.SetPiece(new PieceData(Team.Black, backRank[x], -1, 7), x, 7);
            }

            board.BetrayalRightAvailable = true;
            board.ComputeFullZobristHash();
            return board;
        }
    }
}
