using System.Runtime.CompilerServices;

// This assembly is where the game touches the platform — the file system, Android's storage rules,
// the share sheet. Most of that can only really be exercised on a device, so the parts kept internal
// are the decisions made just before the platform call: which sharing route suits the Android
// version in front of us, and what encoding a report is written in.
//
// The tests, because those decisions are worth checking on a desktop machine with no phone attached.
// A share that picks the wrong route on an older Android, or a report that loses its byte order mark,
// both fail somewhere far away from here and long after the fact.
[assembly: InternalsVisibleTo("ChessTheBetrayal.Tests.EditMode")]
