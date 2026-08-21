using System.Runtime.CompilerServices;

// The animation layer keeps some of its state internal — which animation currently holds a piece's
// position, for one — because nothing outside this assembly has any business steering it. Tests do
// need to read it: "only one thing moves a piece at a time" is a rule about this class's own
// bookkeeping, and checking it through the transform instead would mean driving real animations
// frame by frame to catch a decision that was already made.
[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]
