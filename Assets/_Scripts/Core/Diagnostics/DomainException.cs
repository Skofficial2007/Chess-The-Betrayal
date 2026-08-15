using System;

namespace ChessTheBetrayal.Core.Diagnostics
{
    /// <summary>
    /// Thrown when the domain state reaches a structurally impossible condition,
    /// indicating a call-site programming error rather than a valid in-game rule violation.
    /// </summary>
    public class DomainException : Exception
    {
        public DomainEventCode Code { get; }

        public DomainException(DomainEventCode code, string message)
            : base($"[Domain:{code}] {message}")
        {
            Code = code;
        }
    }
}
