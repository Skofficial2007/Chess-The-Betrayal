# Trap book

A list of positions where one natural-looking move loses material or gets mated, paired with a sound
reply. It exists to stop the opening book recommending a move that loses, and it is the only thing
in the project that can.

See [opening-book.md](opening-book.md) for the book itself.

## Why it is separate from the opening book

The opening book stores a line as one entry per ply, keyed by the position before each move. It has
no record of which side a line was written for, so **every move of every line is stored as a move to
play**. That is fine for main-line theory, where both sides play well. It breaks completely for a
trap, because a trap is defined by one side blundering: importing one teaches the mistake exactly as
confidently as it teaches the refutation.

This is not a gap to work around in the data. It is the wrong shape for the fact, so traps live in
their own file with their own compiler, and the opening book is checked against them.

The failure is worse than an ordinary bad line. A book move is played instantly with no search, so
the position where the AI most needs to think is the one position it is guaranteed not to.

## The record format

The source is `Assets/_Scripts/AI/OpeningBook/Data/traps.book.txt`. One record per line:

```
e2e4 e7e5 g1f3 d7d6 f1c4 b8c6 b1c3 c8g4 h2h3 g4h5 f3e5 | avoid=h5d1 best=c6e5 | Légal Trap
```

Three sections separated by `|`:

| Section | Meaning |
|---|---|
| moves | from the starting position up to and including the move that sets the trap |
| `avoid=` / `best=` | the losing move, and a sound reply to play instead |
| name | what the trap is called |

`avoid` is the fact a record exists to state, and must be right. `best` is **one** sound reply out of
several a position usually has — two sources naming different ones are both correct, so the compiler
keeps the first and only fails when two records disagree about which move *loses*.

The name is a field rather than a comment because it is data: anything that tells a player which trap
they just avoided reads it, and a record that lost its name to a stray edit would otherwise still
compile.

## What it contains

48 traps across six families, in the openings the book already covers.

| Family | Traps |
|---|---|
| Open Games | Légal, Fried Liver, Blackburne Shilling, Noah's Ark, Fishing Pole, Traxler, Lolli, Mortimer, Tarrasch, Rigel, Würzburger |
| Queen's Pawn and Indian Defences | Elephant, Lasker, Budapest Kieninger, Englund, Monticelli, plus King's Indian, Nimzo-Indian, Queen's Indian, Bogo-Indian, Grünfeld, Benoni, Benko, Budapest and Catalan traps |
| Sicilian | Najdorf Poisoned Pawn, Siberian, Alapin, Magnus Smith |
| Flank and system openings | Bird's (From's and Lisitsyn Gambits), Dutch (Hopton, Krejcik), London, Réti, Trompowsky, Sokolsky, Nimzo-Larsen, Colle, English |

Traps nest. Some are only reachable by first playing a move another record says loses — the deeper
Lasker, Budapest Kieninger and Lolli traps are all of this kind. They stay in the trap book, because
the AI should know what not to play if a game arrives there, but the opening book gets no line
steering into them.

## Editing and rebuilding it

Rebuild after editing, or the shipped asset goes stale:

- **In the editor:** `Chess: The Betrayal/AI/Rebuild Trap Book`
- **Headless:** `-executeMethod ChessTheBetrayal.EditorTools.OpeningBook.TrapBookBuilder.CompileDefaultTrapBookHeadless`

The compiled asset is `Assets/AI/Opening Book/TrapBook.asset`. Compiling is a manual step and nothing
reruns it when the source changes, so `ShippedTrapBookTests` compares the two and fails if they have
drifted.

Every record is replayed through the real engine at compile time, and both moves are checked for
legality in the position actually reached. That matters more here than for the opening book: a record
whose position is one ply out reads perfectly well in a text file, and would quietly protect the AI
from a mistake it was never in a position to make.

## What is verified

| Property | Fixture |
|---|---|
| Every record replays legally, and both its moves are legal where it claims | `TrapBookCompilerTests` |
| Two records never disagree about which move loses | `TrapBookCompilerTests` |
| The shipped asset matches its source text | `ShippedTrapBookTests` |
| Hashes match the engine's current Zobrist scheme | `ShippedTrapBookTests` |
| Coverage has not silently shrunk | `ShippedTrapBookTests` |
| **The opening book never plays a move recorded as losing** | `OpeningBookTrapSafetyTests` |
| That check can actually fail | `OpeningBookTrapSafetyTests` |

The last two are the point of the whole file. The check is proved able to fail by compiling an inline
book containing the Légal Trap played through to its losing move and asserting it is caught — a check
that silently compares nothing looks exactly like a check that passes.

It also reports how many trap positions the opening book reaches, currently 45 of 48, with the
recommended reply played in all 45. Without that number a green result would be indistinguishable
from a book that simply never visits any of these positions.

## What it caught

Three lines shipped in the opening book were trap lines imported as main lines. Each played the
losing move and then continued with the opponent's punishment, so the book was walking one side into
losing material and then narrating it:

| Line | Losing move it played |
|---|---|
| Albin Counter-Gambit, "Lasker Trap Line" | `d2b4` — 6.Bxb4??, mated by the underpromotion |
| London System, early Qb6 sideline | `c2f5` |
| Réti Gambit accepted | `b2a3` |

All three were low weight, which is presumably why nothing noticed. A fourth was caught by hand
before it shipped, in the Smith-Morra research: a Siberian Trap line that would have given the AI as
White a coin flip between a sound move and being mated in two.

Two records of the project's own were also wrong and were removed or corrected: the Elephant Trap
recorded 5.cxd5 as losing when that is the ordinary Exchange Variation, and a Cambridge Springs
record condemned 7.Nd2, which is a main line. Both came from reading a source's refutation as though
it branched at the mistake when it branched earlier. **A trap record that condemns sound theory is
worse than no record, because acting on it means deleting good lines.**

## Limits

- **It only sees traps it records.** A green run means the book is clean of the 48 traps in this file,
  not that it is clean. Three of the four suspect lines checked so far turned out to be real.
- **`best` is advisory.** It is one sound reply, not the only one, and nothing verifies it is the
  strongest move — only that it is legal and is not the losing move.
- **It makes no strength claim.** Like the opening book, this is content work: it prevents a specific
  visible embarrassment, and that is all it is being asserted to do.
