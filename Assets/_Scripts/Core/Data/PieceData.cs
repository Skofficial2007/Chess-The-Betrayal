namespace ChessTheMasterPiece.Data
{
    /// <summary>
    /// All the data that describes one chess piece — its type, team, position, and whether it has moved yet. No Unity code, so it's safe to clone and pass around freely.
    /// </summary>
    public class PieceData
    {
        public Team Team { get; set; }
        public ChessPieceType Type { get; set; }
        public int CurrentX { get; set; }
        public int CurrentY { get; set; }
        public int StartRow { get; set; }
        public int MoveDirection { get; set; }
        public bool HasMoved { get; set; }

        /// <summary>
        /// Primary constructor for creating a new piece.
        /// </summary>
        public PieceData(Team team, ChessPieceType type, int x, int y, int direction)
        {
            Team = team;
            Type = type;
            CurrentX = x;
            CurrentY = y;
            StartRow = y;
            MoveDirection = direction;
            HasMoved = false;
        }

        private PieceData(Team team, ChessPieceType type, int x, int y, int direction, int startRow, bool hasMoved)
        {
            Team = team;
            Type = type;
            CurrentX = x;
            CurrentY = y;
            StartRow = startRow;
            MoveDirection = direction;
            HasMoved = hasMoved;
        }

        /// <summary>
        /// Returns an exact copy of this piece. The AI uses this to simulate moves without touching the real board.
        /// </summary>
        public PieceData Clone()
        {
            return new PieceData(Team, Type, CurrentX, CurrentY, MoveDirection, StartRow, HasMoved);
        }

        /// <summary>
        /// Updates the piece's position and marks it as moved.
        /// </summary>
        public void MoveTo(int x, int y)
        {
            CurrentX = x;
            CurrentY = y;
            HasMoved = true;
        }

        /// <summary>
        /// Changes a pawn's type when it reaches the far end of the board.
        /// </summary>
        /// <param name="newType">The new piece type.</param>
        public void PromoteTo(ChessPieceType newType)
        {
            Type = newType;
        }

        /// <summary>
        /// Convenience property: returns true if this is a white piece.
        /// </summary>
        public bool IsWhite => Team == Team.White;

        /// <summary>
        /// Convenience property: returns true if this is a black piece.
        /// </summary>
        public bool IsBlack => Team == Team.Black;

        public override string ToString()
        {
            return $"{Team} {Type} at ({CurrentX},{CurrentY})";
        }
    }
}