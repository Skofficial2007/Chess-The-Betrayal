using System.Runtime.CompilerServices;

namespace ChessTheMasterPiece.Data
{
    public readonly struct PieceData
    {
        public readonly Team           Team;
        public readonly ChessPieceType Type;
        public readonly int            MoveDirection;
        public readonly bool           HasMoved;
        public readonly int            StartRow;

        public static readonly PieceData Empty = default;

        public PieceData(Team team, ChessPieceType type, int moveDirection, int startRow, bool hasMoved = false)
        {
            Team          = team;
            Type          = type;
            MoveDirection = moveDirection;
            StartRow      = startRow;
            HasMoved      = hasMoved;
        }

        public bool IsEmpty => Type == ChessPieceType.None;
        public bool IsWhite => Team == Team.White;
        public bool IsBlack => Team == Team.Black;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PieceData WithMoved() =>
            new PieceData(Team, Type, MoveDirection, StartRow, hasMoved: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PieceData WithHasMoved(bool value) =>
            new PieceData(Team, Type, MoveDirection, StartRow, hasMoved: value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PieceData WithType(ChessPieceType newType) =>
            new PieceData(Team, newType, MoveDirection, StartRow, HasMoved);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PieceData WithTeam(Team newTeam) =>
            new PieceData(newTeam, Type, newTeam == Team.White ? 1 : -1, StartRow, HasMoved);

        public override string ToString() =>
            IsEmpty ? "Empty" : $"{Team} {Type} (moved:{HasMoved})";
    }
}