namespace ChessTheBetrayal.Core.Diagnostics
{
    /// <summary>
    /// Represents the outcome of a domain operation that may fail under valid game conditions.
    /// Designed as a value type to prevent garbage collection overhead on high-frequency evaluation paths.
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
