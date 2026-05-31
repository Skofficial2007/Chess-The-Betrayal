namespace ChessTheBetrayal.Core.Diagnostics
{
    /// <summary>
    /// A default logger implementation that silently discards all diagnostic events.
    /// Used to ensure domain classes remain safely decoupled and testable when executed 
    /// in environments without a presentation layer, such as unit tests.
    /// </summary>
    public sealed class NullDomainLogger : IDomainLogger
    {
        public static readonly NullDomainLogger Instance = new NullDomainLogger();
        
        private NullDomainLogger() { }

        public bool IsVerbose => false;
        
        public void LogInfo(DomainLogEvent evt) { }
        public void LogWarning(DomainLogEvent evt) { }
        public void LogError(DomainLogEvent evt) { }
    }
}
