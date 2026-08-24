namespace ChessTheBetrayal.Core.Diagnostics
{
    /// <summary>
    /// The outcome of a domain operation that can fail while everyone involved is playing properly —
    /// as opposed to <see cref="DomainException"/>, which reports a caller doing something the rules
    /// of the program forbid. A rejected move is the first kind; a board of the wrong size is the
    /// second.
    ///
    /// Confirming a Betrayal Act is the first thing that needed it. That question stays on screen
    /// while the clock runs, so by the time the player answers, the move they named may have stopped
    /// being legal — which is nobody's mistake, just a position that moved on. A server needs the
    /// same answer for the same reason: a client sends what it would like to happen, the board that
    /// counts decides, and the refusal has to cross an RPC with a reason attached. An exception
    /// cannot do that, and a player trying something illegal is ordinary traffic, not an error.
    ///
    /// A struct, so returning one from a rejection check costs nothing. That constrains T to a value
    /// type, which suits what the domain hands back — a MoveCommand, a MatchResult, a turn outcome.
    /// </summary>
    public readonly struct DomainResult<T> where T : struct
    {
        public readonly bool IsSuccess;
        public readonly T Value;
        public readonly DomainEventCode ErrorCode;
        public readonly string ErrorDetail;

        private DomainResult(bool ok, T value, DomainEventCode code, string detail)
        {
            IsSuccess = ok;
            Value = value;
            ErrorCode = code;
            ErrorDetail = detail;
        }

        public static DomainResult<T> Success(T value) =>
            new DomainResult<T>(true, value, default, null);

        public static DomainResult<T> Failure(DomainEventCode code, string detail = null) =>
            new DomainResult<T>(false, default, code, detail);
    }
}
