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
  the lines produce almost as many distinct move sequences, and only a handful of entries are shared by transposition,
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
| normal | 8 | 39 |
| aggressive | 10 | 36 |
| hard | 14 | 26 |
| extreme | whole book | — |
| impossible | whole book | — |

The right-hand column shifts whenever lines are added to the book, so `OpeningBookTheoryWalkTests`
reports it on every run rather than leaving it recorded only here.

The allowance counts plies played in the game, read off the board rather than tallied by the agent.
That makes it mean the same thing whichever colour the AI has, and it means taking a move back moves
the boundary back with it.

The top two tiers keeping the whole book is a deliberate advantage rather than an omission: correct
theory played instantly, with the entire per-move time budget saved for the middlegame.

Very little variety is lost to these limits. The choice of opening is made in the first few moves
and the book narrows quickly after that — see the shape below.

## What the book contains

366 variations across six families, compiled to 3,792 positions.

| Family | Openings covered |
|---|---|
| Open Games (1.e4 e5) | Ruy Lopez, Italian, Scotch, Petroff, Four Knights, Vienna, King's Gambit (accepted and declined), Philidor, Ponziani, Bishop's Opening, Center Game |
| Sicilian Defence | Najdorf, Dragon (plus accelerated and hyperaccelerated), Scheveningen, Sveshnikov, Kalashnikov, Taimanov, Kan, Classical, Alapin, Closed, Grand Prix, Smith-Morra (accepted and declined), Rossolimo, Moscow |
| Semi-Open Games | French, Caro-Kann, Pirc, Modern, Scandinavian, Alekhine |
| Queen's Gambit | Declined, Accepted, Slav, Semi-Slav, Tarrasch, Chigorin, Albin Counter-Gambit, Baltic |
| Flank and system openings | English, Réti, King's Indian Attack, London, Trompowsky, Colle and Colle-Zukertort, Stonewall, Bird's, Dutch, Sokolsky, Nimzo-Larsen |
| Indian Defences | King's Indian, Nimzo-Indian, Queen's Indian, Bogo-Indian, Grünfeld, Benoni, Benko Gambit, Budapest, Catalan |

It also answers **all twenty legal first moves**. Thirteen of them — 1.g3, 1.Nc3, 1.e3, 1.d3 and
the rest — carry a handful of lines each at weight 1, purely so the AI is not improvising from its
very first reply as Black. A club player opens 1.g3 far more often than they reach move twenty of a
Najdorf. They are weight 1 deliberately: a line teaches the engine the position it starts from as
much as the answers that follow, so at any higher weight the AI would start opening 1.g4 itself.

A final section answers positions the [trap book](trap-book.md) records, playing the sound reply
rather than the mistake, so the book has an answer ready where a natural move loses.

### Its shape

Lines run from 8 to 38 plies, median 16. The first-move mix is roughly half 1.e4 and a little under
half 1.d4, with 1.c4 a distant third and everything else in the low single digits — close to how the
openings are distributed in real play, and reached by accumulating the source weights rather than by
hand-tuning them. It shifts slightly whenever lines are added, so treat the exact split as something
to measure rather than a figure to quote.

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

So it is closer to a set of long corridors than to a tree. Past roughly ply 11 it holds a single reply
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
| No line plays a move the trap book records as losing | `OpeningBookTrapSafetyTests` |
| A tier keeps its allowance through profile resolution | `AIProfileGuardrailTests` |

The checks above are all about the book's *content*: that it is correct, current, varied, and does
not walk into a lost game. None of them claims a strength gain. What the book is worth in games is a
separate question, and it has now been measured — see below.

## What the book is worth: measured

**Over 640 games it is worth nothing measurable: 49.8%, −1 Elo, 95% interval [46.0%, 53.7%].**

Measured by playing each tier against a copy of itself where only one side may open the book, from
the standard starting position, colours alternating. The normal tournament path cannot see a book at
all — it builds the search directly and never constructs the agent that owns one — so this runs
through its own harness (`OpeningBookImpactRunner`), with the tournament path left untouched.

| Tier | Games | Score | 95% CI | Elo |
|---|---:|---:|---:|---:|
| easy | 40 | 53.8% | ±15.5% | +26 |
| normal | 240 | 52.7% | ±6.3% | +19 |
| hard | 40 | 47.5% | ±15.5% | −17 |
| aggressive | 240 | 47.1% | ±6.3% | −20 |
| extreme | 40 | 50.0% | ±15.5% | ±0 |
| impossible | 40 | 47.5% | ±15.5% | −17 |
| **all pooled** | **640** | **49.8%** | **±3.9%** | **−1** |

No tier shows an effect that clears its own interval. The pooled interval is now tight enough that a
book worth more than about 26 Elo, or costing more than about 28, would have shown up.

**Three reasons this is the expected answer, not a disappointing one:**

- **The opening is a small slice of a game** — eight to sixteen plies out of sixty to a hundred and
  twenty. A small edge there is diluted by everything after it.
- **This engine has no chess clock, by design.** Every move is bounded on its own, so the time a
  side saves by answering instantly from memory cannot be spent later. The largest practical benefit
  a book gives a real engine is structurally unavailable here.
- **Against a human it rarely fires for long.** The book answers only positions it knows exactly, and
  a club player leaves known theory within a few moves.

**What the book is actually for, then:** instant opening moves instead of a three-second think,
recognisable named theory instead of improvisation, and a different game every time instead of the
same opening on repeat. Those are real and player-facing. They are simply not strength, and should
not be reported as strength.

**A caution about small samples, learned here.** A first pass at forty games a tier suggested the
book was worth +61 Elo to `normal` and costing `aggressive` 70 Elo — the latter looking like a real
bug, with a plausible story attached (`aggressive` is the one tier with a deliberately reshaped
evaluator, so neutral theory steering it into calm positions sounded convincing). Both effects
vanished at two hundred games a tier: `normal` fell to +10, `aggressive` rose to −10. They were
noise wearing an explanation. Acting on the first pass would have removed a tier's opening book for
no reason.
