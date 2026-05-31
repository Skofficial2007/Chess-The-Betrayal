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

    /// <summary>
    /// Thrown when an operation violates a hard mechanic invariant.
    /// Invariants include: King cannot be a Betrayer, King cannot be a Victim,
    /// and special mechanic rights cannot be consumed more than once per match.
    /// </summary>
    public sealed class BetrayalRuleViolationException : DomainException
    {
        public BetrayalRuleViolationException(DomainEventCode code, string message)
            : base(code, message) { }
    }
}
