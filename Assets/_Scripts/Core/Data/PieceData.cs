namespace ChessTheMasterPiece.Data
{
    /// <summary>
    /// Pure C# representation of a chess piece - no Unity dependencies.
    /// This is the source of truth for piece state in the game logic layer.
    /// Serializable for network sync, game saves, or move simulation.
    /// </summary>
    public class PieceData
    {
        public Team Team { get; set; }
        public ChessPieceType Type { get; set; }
        public int CurrentX { get; set; }
        public int CurrentY { get; set; }
        public int InitialY { get; set; }
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
            InitialY = y;
            MoveDirection = direction;
            HasMoved = false;
        }

        /// <summary>
        /// Private constructor for cloning (preserves all fields including HasMoved).
        /// </summary>
        private PieceData(Team team, ChessPieceType type, int x, int y, int direction, int initialY, bool hasMoved)
        {
            Team = team;
            Type = type;
            CurrentX = x;
            CurrentY = y;
            InitialY = initialY;
            MoveDirection = direction;
            HasMoved = hasMoved;
        }

        /// <summary>
        /// Creates a deep copy of this piece's data.
        /// Critical for simulating moves during check/checkmate detection 
        /// without modifying the actual game state.
        /// </summary>
        public PieceData Clone()
        {
            return new PieceData(Team, Type, CurrentX, CurrentY, MoveDirection, InitialY, HasMoved);
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
        /// Promotes a pawn to a new piece type. This method is called when a pawn reaches the opposite end of the board.
        /// </summary>
        /// <param name="newType">The new type to which the pawn will be promoted.</param>
        public void PromoteTo(ChessPieceType newType)
        {
            Type = newType;
            // We preserve InitialY and MoveDirection as they are historical markers
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