using System.Collections.Generic;
using UnityEngine;
using ChessTheBetrayal.Core.Data;

namespace ChessTheBetrayal.View
{
    /// <summary>Where a captured piece stands once it reaches the death pile, and which way it faces.</summary>
    public readonly struct GraveyardSlot
    {
        public readonly Vector3 Position;
        public readonly Vector3 LookDirection;

        public GraveyardSlot(Vector3 position, Vector3 lookDirection)
        {
            Position = position;
            LookDirection = lookDirection;
        }
    }

    /// <summary>
    /// The board measurements a death pile is positioned against. Recomputed whenever the board is
    /// rebuilt, since the origin moves with it.
    /// </summary>
    public readonly struct GraveyardLayout
    {
        private readonly Vector3 _boardOrigin;
        private readonly float _tileSize;
        private readonly int _tileCountX;
        private readonly int _tileCountY;
        private readonly float _tilesYOffset;
        private readonly float _pieceYOffset;
        private readonly float _deathSpacing;

        public GraveyardLayout(Vector3 boardOrigin, float tileSize, int tileCountX, int tileCountY,
            float tilesYOffset, float pieceYOffset, float deathSpacing)
        {
            _boardOrigin = boardOrigin;
            _tileSize = tileSize;
            _tileCountX = tileCountX;
            _tileCountY = tileCountY;
            _tilesYOffset = tilesYOffset;
            _pieceYOffset = pieceYOffset;
            _deathSpacing = deathSpacing;
        }

        /// <summary>
        /// Where the piece at <paramref name="stackIndex"/> in <paramref name="team"/>'s pile stands.
        /// Each side's pile sits off its own edge of the board and grows away from that side's major
        /// rank, so the two never collide and it stays obvious at a glance who lost what.
        /// </summary>
        public GraveyardSlot SlotFor(Team team, int stackIndex)
        {
            bool isWhite = team == Team.White;

            int majorRowIndex = isWhite ? 0 : _tileCountY - 1;
            float rowCenterZ = _boardOrigin.z + majorRowIndex * _tileSize + _tileSize * 0.5f;

            float stackSpacing = Mathf.Max(0.01f, _deathSpacing);

            float xPos = isWhite
                ? _boardOrigin.x + _tileCountX * _tileSize + (_tileSize * 0.5f)
                : _boardOrigin.x - (_tileSize * 0.5f);

            float zPos = isWhite
                ? (rowCenterZ - (stackIndex * 0.5f * stackSpacing)) + (stackIndex * stackSpacing)
                : (rowCenterZ + (stackIndex * 0.5f * stackSpacing)) - (stackIndex * stackSpacing);

            Vector3 position = new Vector3(xPos, _boardOrigin.y + _tilesYOffset + _pieceYOffset, zPos);

            Vector3 boardCenterPos = _boardOrigin + new Vector3(
                _tileCountX * _tileSize * 0.5f,
                0f,
                _tileCountY * _tileSize * 0.5f);

            Vector3 lookDir = (boardCenterPos - position).normalized;
            lookDir.y = 0;

            return new GraveyardSlot(position, lookDir);
        }
    }

    /// <summary>
    /// Each side's pile of captured pieces, in the order they were taken.
    ///
    /// Order is the whole point. A slot is handed out by position in the pile, so the piece taken
    /// most recently is always the one on the end — which means a takeback can hand that exact
    /// piece back and every remaining slot still describes where its occupant is standing. Popping
    /// anywhere other than the end would leave the pile's geometry describing an arrangement that
    /// is no longer on screen.
    ///
    /// Generic over the occupant so the ordering and the geometry can be exercised on their own,
    /// without a scene or a spawned piece to stand in the pile.
    /// </summary>
    public sealed class GraveyardStack<T> where T : class
    {
        private readonly List<T> _white = new List<T>(16);
        private readonly List<T> _black = new List<T>(16);

        private GraveyardLayout _layout;

        /// <summary>Every occupant of both piles, oldest first — for teardown, which does not care whose it was.</summary>
        public IEnumerable<T> All
        {
            get
            {
                for (int i = 0; i < _white.Count; i++) yield return _white[i];
                for (int i = 0; i < _black.Count; i++) yield return _black[i];
            }
        }

        /// <summary>Point the piles at the board's current measurements. Call whenever it is rebuilt.</summary>
        public void SetLayout(GraveyardLayout layout) => _layout = layout;

        public IReadOnlyList<T> Occupants(Team team) => Pile(team);

        public int Count(Team team) => Pile(team).Count;

        /// <summary>
        /// Adds <paramref name="occupant"/> to the end of its side's pile and returns the slot it
        /// now owns. Claiming the slot and joining the pile are the same step on purpose: a piece
        /// heading for the graveyard needs to know where it is going before it has arrived, and
        /// nothing else may be handed that slot in the meantime.
        /// </summary>
        public GraveyardSlot Push(Team team, T occupant)
        {
            List<T> pile = Pile(team);
            pile.Add(occupant);
            return _layout.SlotFor(team, pile.Count - 1);
        }

        /// <summary>
        /// Removes and returns the most recently added occupant — the one a takeback puts back on
        /// the board. False when that side's pile is empty.
        /// </summary>
        public bool TryPopLast(Team team, out T occupant)
        {
            List<T> pile = Pile(team);
            if (pile.Count == 0)
            {
                occupant = null;
                return false;
            }

            occupant = pile[pile.Count - 1];
            pile.RemoveAt(pile.Count - 1);
            return true;
        }

        public void Clear()
        {
            _white.Clear();
            _black.Clear();
        }

        private List<T> Pile(Team team) => team == Team.White ? _white : _black;
    }
}
