using System.Collections.Generic;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.Tooling
{
    /// <summary>
    /// One game's worth of IPositionSampler buffering: collects this game's quiet positions as
    /// they're offered, then stamps every one of them with the game's final outcome and hands them to
    /// a shared thread-safe writer once the game ends. Each simulated game gets its OWN instance (see
    /// MatchSimulator's own doc comment on why one simulator, and therefore one sampler, is not safe
    /// to share across a parallel run's worker threads) — only the writer itself is shared.
    /// </summary>
    public sealed class TexelCorpusSampler : IPositionSampler
    {
        private readonly TexelCorpusWriter _writer;
        private readonly List<(string BoardEncoding, Team SideToMove, bool BetrayalRightAvailable, bool PostDefectionOccurred)> _buffer = new();

        public TexelCorpusSampler(TexelCorpusWriter writer)
        {
            _writer = writer;
        }

        public void OnQuietPosition(BoardState board, Team sideToMove, int ply, bool postDefectionOccurred)
        {
            _buffer.Add((TexelBoardCodec.Encode(board), sideToMove, board.BetrayalRightAvailable, postDefectionOccurred));
        }

        public void OnGameComplete(MatchOutcome outcome)
        {
            double label = TexelPositionRecord.LabelFor(outcome);
            foreach (var sample in _buffer)
            {
                _writer.WritePosition(new TexelPositionRecord(
                    sample.BoardEncoding, sample.SideToMove, sample.BetrayalRightAvailable,
                    sample.PostDefectionOccurred, label));
            }
            _buffer.Clear();
        }
    }
}
