namespace ChessTheMasterPiece.Data
{
    /// <summary>
    /// Custom struct for position representation.
    /// Implements IEquatable to prevent GC boxing when used as Dictionary/HashSet keys.
    /// </summary>
    public struct Vector2Int : System.IEquatable<Vector2Int>
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

        // Interface method for zero-allocation dictionary comparisons
        public bool Equals(Vector2Int other)
        {
            return this.x == other.x && this.y == other.y;
        }

        // Standard object override
        public override bool Equals(object obj)
        {
            return obj is Vector2Int other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Zero-allocation polynomial hash (prime multiplication + XOR)
            // Very fast for small board coordinates and avoids ValueTuple hashing overhead.
            return x * 1000003 ^ y;
        }

        public override string ToString() => $"({x}, {y})";

        // Convenience properties
        public static Vector2Int Zero => new Vector2Int(0, 0);
        public static Vector2Int One => new Vector2Int(1, 1);
        public static Vector2Int Invalid => new Vector2Int(-1, -1);
    }
}