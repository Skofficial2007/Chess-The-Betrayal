namespace ChessTheBetrayal.Core.Diagnostics
{
    /// <summary>
    /// The outcome of a domain operation that can fail while everyone involved is playing properly —
    /// as opposed to <see cref="DomainException"/>, which reports a caller doing something the rules
    /// of the program forbid. A rejected move is the first kind; a board of the wrong size is the
    /// second.
    ///
    /// Nothing calls this yet. It is here for the side of the game that has not been built: once a
    /// server is deciding whether a move a client sent is legal, it has to answer with a reason the
    /// client can act on, and an exception is the wrong carrier for that. It cannot cross an RPC
    /// intact, and a player trying something illegal is ordinary traffic, not an error.
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
