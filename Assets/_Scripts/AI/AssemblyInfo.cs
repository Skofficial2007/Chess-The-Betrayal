using System.Runtime.CompilerServices;

// Most of the AI is internal on purpose. The evaluation terms, the search's scratch state and the
// move packing all exist to serve one caller each, and making them public would invite the rest of
// the game to depend on decisions we want to keep free to change. Two assemblies still have to see
// past that, for reasons worth writing down.
//
// The tests, because the parts most worth testing hardest are the internal ones: an evaluation term
// only means anything measured against the others, and a search that offered nothing but its final
// move could never be checked for how it got there.
//
// The editor tooling, because the opening-book compilers store each position's chosen move in the
// search's own packed form, so that the book and the search agree on exactly what a move is.
[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]
[assembly: InternalsVisibleTo("ChessTheBetrayal.EditorTools")]
