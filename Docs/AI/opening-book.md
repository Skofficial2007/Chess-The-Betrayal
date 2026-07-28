# Opening book

What the AI knows about openings, how it uses it, and what has and has not been verified about it.

## How it works

The book is a list of positions with a recommended reply for each, keyed by the exact Zobrist hash
the search itself computes. A lookup binary-searches for the hash, widens to the run of entries
sharing it, re-checks each recommended move against the position's real legal moves, and picks
among the survivors at random weighted by how often that move appears across the source lines.

That is the same shape most engines use. Two things follow from it that are worth knowing:

- **Move order does not matter.** Any sequence that arrives at a position the book knows gets an
  answer, so a line reached by transposition is free. In practice this book barely benefits: its
  252 lines produce 2,647 distinct move sequences and only 8 entries are shared by transposition,
  so the mechanism is right but currently earns very little.
- **There is no partial matching.** One move either side plays that is not in the book and the book
  is silent from then on, unless play happens to transpose back into something it knows.

Out of book, the lookup returns nothing and the normal search runs. The AI does not track "which
opening it is playing" and never has to: each turn is an independent question about the position in
front of it, so an opponent deviating is not something it needs to recover from — there is no
longer a line to be following. `AsyncAIAgent` raises `OnLeftOpeningBook` once, on the first move it
has to work out for itself, purely so a log shows where memory stopped and play began.

Betrayal is excluded by construction. The compiler rejects any Betrayal move, and the lookup
declines outright while a Betrayal sequence is in progress, so no book entry can ever recommend one.

## How much theory each tier plays

Every tier consults the same book, but they are allowed different amounts of it. Left unlimited,
the easiest tier played a median of sixteen plies of grandmaster theory and then started hanging
pieces at a thirty percent blunder rate — difficulty that only begins once the book runs out is not
difficulty.

| Tier | Plies of theory | Openings it shortens (of 40 sampled) |
|---|---:|---:|
| easy | 4 | 40 |
| normal | 8 | 40 |
| aggressive | 10 | 39 |
| hard | 14 | 26 |
| extreme | whole book | — |
| impossible | whole book | — |

The allowance counts plies played in the game, read off the board rather than tallied by the agent.
That makes it mean the same thing whichever colour the AI has, and it means taking a move back moves
the boundary back with it.

The top two tiers keeping the whole book is a deliberate advantage rather than an omission: correct
theory played instantly, with the entire per-move time budget saved for the middlegame.

Very little variety is lost to these limits. The choice of opening is made in the first few moves
and the book narrows quickly after that — see the shape below.

## What the book contains

252 variations across six families, compiled to 2,887 positions.

| Family | Openings covered |
|---|---|
| Open Games (1.e4 e5) | Ruy Lopez, Italian, Scotch, Petroff, Four Knights, Vienna, King's Gambit (accepted and declined), Philidor, Ponziani, Bishop's Opening, Center Game |
| Sicilian Defence | Najdorf, Dragon (plus accelerated and hyperaccelerated), Scheveningen, Sveshnikov, Kalashnikov, Taimanov, Kan, Classical, Alapin, Closed, Grand Prix, Smith-Morra (accepted and declined), Rossolimo, Moscow |
| Semi-Open Games | French, Caro-Kann, Pirc, Modern, Scandinavian, Alekhine |
| Queen's Gambit | Declined, Accepted, Slav, Semi-Slav, Tarrasch, Chigorin, Albin Counter-Gambit, Baltic |
| Flank and system openings | English, Réti, King's Indian Attack, London, Trompowsky, Colle and Colle-Zukertort, Stonewall, Bird's, Dutch, Sokolsky, Nimzo-Larsen |
| Indian Defences | King's Indian, Nimzo-Indian, Queen's Indian, Grünfeld, Benoni, Benko Gambit |

### Its shape

Lines run from 8 to 38 plies, median 16. First moves come out at roughly e4 54%, d4 37%, c4 5%, with
Nf3, f4, b3 and b4 making up the rest — close to how the openings are distributed in real play, and
reached by accumulating the source weights rather than by hand-tuning them.

The book is **deep but narrow**. Counting how many replies it knows per position:

| Ply | Replies known, on average |
|---:|---:|
| 0 | 7.00 |
| 2 | 1.86 |
| 4 | 1.46 |
| 6 | 1.25 |
| 8 | 1.09 |
| 11 | 1.02 |
| 18 and beyond | 1.00 |

So it is closer to 252 long corridors than to a tree. Past roughly ply 11 it holds a single reply
per position, and past ply 18 that is strictly true. All of its variety lives in the first eight
plies, and its deep tail only ever fires when an opponent plays the one scripted continuation.

**The clearest way to improve it is more breadth between plies 2 and 10, not longer lines.** A
thirtieth ply added to an existing line is reachable only by an opponent following that exact line
that far; a second known answer to a common fourth move is reached constantly.

## Editing and rebuilding it

The source is `Assets/_Scripts/AI/OpeningBook/Data/openings.book.txt`: one line per variation, in
coordinate notation, with an optional `| w=N` weight. The compiled asset the game loads is
`Assets/AI/Opening Book/OpeningBook.asset`.

**Compiling is a manual step and nothing triggers it for you.** Edit the source, forget to rebuild,
and the game keeps playing the old lines while every test that compiles its own book still passes.
Rebuild with the `Chess: The Betrayal/AI/Rebuild Opening Book` menu command, or headlessly:

```
Unity.exe -batchmode -quit -projectPath <project> \
  -executeMethod ChessTheBetrayal.EditorTools.OpeningBook.OpeningBookBuilder.CompileDefaultBookHeadless
```

Commit the source and the regenerated asset together. `ShippedOpeningBookTests` exists to catch the
case where you do not.

Every line is replayed through the real engine at compile time, so an illegal move fails the build
rather than shipping. That check is not a formality — when these families were imported, seven lines
were perfectly well-formed and still illegal in the position they reached, including one pawn move
that was pinned against its own king. A format check is not verification; only the replay is.

## What is verified, and what is not

Verified automatically, all in seconds:

| Property | Fixture |
|---|---|
| Every line is legal when replayed through the engine | `OpeningBookImporterTests` |
| The shipped asset matches its source text, entry for entry | `ShippedOpeningBookTests` |
| Hashes match the engine's current Zobrist scheme | `ShippedOpeningBookTests` |
| Coverage has not silently shrunk | `ShippedOpeningBookTests` |
| Theory is played without searching, and handed back cleanly when it ends | `OpeningBookTheoryWalkTests` |
| Openings vary across seeds, and repeat for a fixed seed | `OpeningBookTheoryWalkTests` |
| Each tier stops at its own allowance, and the allowances form a ladder | `OpeningBookTheoryWalkTests` |
| No opening leaves the AI in a position it already considers lost | `OpeningBookExitBalanceTests` |
| A tier keeps its allowance through profile resolution | `AIProfileGuardrailTests` |

**Not verified: that the book makes the AI play better.** Nothing measures that, and it is worth
being precise about why rather than leaving it as a gap someone tries to close casually.

The tournament harness builds the search directly and never constructs the agent that owns the
book, so no tournament — quick, full or focused — can observe a book change at all. Teaching it the
book was considered and rejected: it already starts its games from curated opening positions four to
eight plies deep, so both sides would recite matching theory for another ten plies on top of that,
compressing the part of each game that actually decides anything and pushing up a draw rate that is
already high between the top tiers. It would also make every tournament result recorded so far
incomparable with everything measured after it.

Grading book exits against a deeper reference search was also considered and rejected. That
instrument measures agreement with the reference evaluator rather than strength, and its own
reference has not converged on these positions, so it would answer a question about the search
rather than about the book.

So the checks above are all about the book's *content*: that it is correct, current, varied, and
does not walk into a lost game. None of them claims a strength gain, and none should be read as one.
