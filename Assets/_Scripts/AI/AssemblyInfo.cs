using System.Runtime.CompilerServices;

// Most of the AI is internal on purpose. The evaluation terms and the search's scratch state exist
// to serve one caller each, and making them public would invite the rest of the game to depend on
// decisions we want to keep free to change. One assembly still has to see past that, for a reason
// worth writing down.
//
// The tests, because the parts most worth testing hardest are the internal ones: an evaluation term
// only means anything measured against the others, and a search that offered nothing but its final
// move could never be checked for how it got there.
[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]
