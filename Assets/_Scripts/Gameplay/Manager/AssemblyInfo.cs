using System.Runtime.CompilerServices;

// The wording this assembly writes into the log is worth checking, and the log is the one report
// surface nothing was checking. Every other place the project writes text for somebody to read back
// - the benchmark report, the plan description, the match report - has a test holding it to plain
// ASCII, because a file of ours came back off a device with its em dashes mangled and the fix was to
// stop using characters that can mangle. The log lines sat outside that net purely because they are
// formatted here rather than in a report class, and they are the ones a tester actually pastes.
[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]
