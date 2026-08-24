using System.Runtime.CompilerServices;

// The book compilers keep their working parts internal: turning a line of text into book entries
// is a sequence of steps that only make sense together, and the rest of the project has no business
// calling into the middle of it. One assembly does need to.
//
// The tests, because a compiled book is only as trustworthy as the replay that produced it. An entry
// is right if the moves it encodes are the moves the engine actually generates from that position,
// and the only way to check that is to run the replay step directly and look at what comes out.
// Testing the compiler through its finished output would tell us a book was built, not that it was
// built correctly.
[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]
