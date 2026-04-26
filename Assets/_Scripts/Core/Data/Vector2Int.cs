namespace ChessTheMasterPiece.Data
{
    /// <summary>
    /// Custom struct for position representation.
    /// Mirrors Unity's Vector2Int for compatibility without Unity dependencies.
    /// </summary>
    public struct Vector2Int
    {
        public int x;
        public int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);

        public override bool Equals(object obj)
        {
            if (obj is Vector2Int other)
                return this == other;
            return false;
        }

        public override int GetHashCode() => (x, y).GetHashCode();

        public override string ToString() => $"({x}, {y})";

        // Convenience properties
        public static Vector2Int Zero => new Vector2Int(0, 0);
        public static Vector2Int One => new Vector2Int(1, 1);
        public static Vector2Int Invalid => new Vector2Int(-1, -1);
    }
}