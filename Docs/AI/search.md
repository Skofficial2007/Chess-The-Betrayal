# Search

What the AI does while it is thinking, why it manages that inside three seconds, and what about it
has and has not been measured.

Written for someone who knows chess but has never worked on an engine. Every section ends with the
file to read next.

## How it works

Play every legal move in your head, then every reply, then every reply to that, as deep as time
allows. Score the positions at the bottom, and assume both sides pick the best line available to
them on the way back up. The tree is the whole game from here: a **node** is one position reached
partway down it, and a **ply** is one move by one side, so depth 7 means seven half-moves deep.

That tree is far too big to walk completely, and **alpha-beta pruning** is what makes it tractable.
The moment one reply proves a branch is worse than something already found, the rest of that branch
goes unexamined, because nothing in it can change the decision. That early stop is a **cutoff**, and
causing more of them sooner is what almost every technique below is for.
`Assets/_Scripts/AI/AlphaBetaSearch.cs`.

### What a position is worth

The number the search is trying to maximise. Count material with the usual values, add a small bonus
or penalty per piece for the square it stands on so a knight in the centre beats one on the rim, and
add a little for holding an unspent Betrayal right. Positive means good for the side being scored.

It is deliberately simple. The strength here comes from searching deeply, not from a clever
evaluation. `Assets/_Scripts/AI/BetrayalAwareEvaluator.cs`.

### The Betrayal moves, and why the search treats them specially

This is not ordinary chess, and the difference shows up in nearly every technique below.

An **Act** is a piece turning on its own side, targeting a friendly piece. It does not pass the turn.
The same player must immediately follow it with a **Retribution**, capturing their own betrayer; if
no capture is available the piece **defects** to the opponent, and if that leaves their king in check
they owe a forced king-saving move called a **Defensive Override**.

So a Betrayal is one player making two or three moves in a row. Every pruning technique below assumes
turns alternate, which is why they are all switched off while a Betrayal is pending.
`Assets/_Scripts/Core/Engine/TurnResolver.cs`.

### Zobrist hash

A fingerprint of a position, kept as a single number. Each piece on each square has a fixed random
value, and a position's hash is all of them combined; moving a piece updates the hash rather than
recomputing it.

It is what makes the transposition table possible, and it doubles as a cheap correctness check,
since a search that fails to undo a move leaves the hash wrong. Here it also covers the Betrayal
sub-state, which is what makes caching safe mid-sequence. `Assets/_Scripts/Core/Data/BoardState.cs`.

### Iterative deepening

Search to depth 1, then depth 2, then 3, keeping the best move from the last depth that finished
completely.

It sounds wasteful, since the shallow part gets re-searched every time. It isn't. The shallow
searches are cheap, and what they produce is a good guess at the best move, which makes the deep
search dramatically faster. It also means the search can stop at any moment and still have a usable
answer, which is the only reason a time limit is possible at all.

### Attack map

Ask a piece which squares it attacks, once, and read the answer, rather than simulating moves to
find out. Sliding pieces walk each ray to the first blocker; pawns report both diagonals whether or
not anything is standing there. Used by both Betrayal target generation and check detection.
`Assets/_Scripts/Core/Engine/Movement/IPieceMovement.cs`.

## How deep each tier searches

| Tier | Depth ceiling | Soft budget | Hard budget |
|---|---:|---:|---:|
| easy | 3 | 400 ms | 1300 ms |
| normal | 5 | 700 ms | 2250 ms |
| hard | 8 | 900 ms | 3000 ms |
| aggressive | 7 | 900 ms | 3000 ms |
| extreme | 9 | 1000 ms | 3000 ms |
| impossible | 9 | 1200 ms | 3000 ms |

**The depth is a ceiling, not a promise.** easy and normal are shallow by design — their difficulty
comes from that plus a blunder rate — and they reach their full depth in a fraction of their budget.
The four deeper tiers are budget-bound: they reach whatever depth the clock allows on the hardware in
front of them, deeper on a faster machine, and iterative deepening always keeps the last completed
depth's move, so being stopped is never a wasted search.

One consequence worth knowing before tuning anything: on a middlegame position at three seconds, the
four deep tiers can all bottom out at the same depth, in which case what separates them is their
personality dials rather than their search. `Assets/_Scripts/AI/AIProfileTable.cs`.

## What makes it fast

These compose in roughly this order. Iterative deepening drives the whole thing, the table and the
ordering decide what gets looked at first, and the pruning family decides what gets skipped
entirely. Unless noted, all of it lives in `Assets/_Scripts/AI/AlphaBetaSearch.cs`.

### Transposition table

A cache of positions already scored, keyed by Zobrist hash. The same position is reachable by many
different move orders, so without a cache you pay for it on every arrival.

Two things make it safe here. A stored entry's best move is never played directly — it is only
matched against the freshly generated legal move list to decide what to try first, so a stale or
colliding entry can waste time but can never produce an illegal move. And because the hash covers the
Betrayal sub-state, a position mid-sequence never collides with the same board outside one.

Costs memory: sixteen megabytes in a real match. It lives for the whole match on purpose, since that
persistence is what stops each move re-deriving the previous move's work.
`Assets/_Scripts/AI/TranspositionTable.cs`.

### Move ordering

Alpha-beta can only skip a branch once it has found something good enough to prove the branch
irrelevant, so the order moves are tried in decides how much gets skipped. Nothing else in a chess
search pays back as well for as little; it costs a sort.

Moves fall into explicit bands: the table's suggested move first, then captures that win material,
then promotions and even trades, then a Betrayal Act, then everything else. The Act is demoted
deliberately — it opens a forced sequence, so it is worth checking whether an ordinary winning move
exists before going down that road.

Ordering only changes what gets looked at first. It can never change which move is chosen.
See `OrderScore`.

### History heuristic and killer moves

Two ways of remembering that a quiet move worked. **Killers** are the last couple of moves that
caused a cutoff at a given distance from the root. The **history table** is longer-lived and keyed by
piece type and destination square. Both feed a bonus into the quiet-move band above.

A move that has proven useful before gets tried earlier the next time it is legal. Neither can push a
quiet move above a capture, a promotion, or an Act.

One detail specific to this game: killers are keyed by distance from the root rather than by whose
turn it is, because a Betrayal sequence does not alternate movers every ply.

### Static exchange evaluation

Given a capture, work out who comes out ahead once every piece bearing on that square has taken a
turn recapturing, cheapest piece first on both sides, including sliders revealed when the piece in
front of them leaves.

It refines ordering within the winning and equal capture bands, and it lets quiescence skip a capture
that looks like a material gain by piece count but loses material once the recaptures are played out.

The classic algorithm assumes the two sides simply alternate on the contested square, and that is
exactly what a pending Betrayer breaks — a Retribution capture is played by an ally of the piece that
Acted, not by the opponent, so "whose turn is next" and "who benefits" come apart. Every call site
checks for a pending Betrayer first and falls back to plain material-difference ordering when one
exists. `Assets/_Scripts/AI/StaticExchangeEvaluation.cs`.

### Null move pruning

Give the opponent a free extra move. If the position is still good enough to cause a cutoff even
after handing them one, it is almost certainly good enough that this branch does not need searching
properly.

It is an assumption, not a proof, and it is wrong in positions where being obliged to move is itself
bad. So it is disabled when little material is left, when the side to move is in check, and never
twice in a row. One guard is specific to this game: never during a pending Betrayal. A null move
flips whose turn it is, and mid-sequence the same player is required to move again, so a null move
there would corrupt move generation, the hash, and the search window at once.

### Late move reductions

If the ordering is good, moves near the end of the list are unlikely to be best, so search them at
reduced depth first. If a reduced search unexpectedly comes back strong, search it again properly.

The risk is obvious: reduce the wrong move and you miss it. Captures, promotions and the table's
suggested move are exempt, and so is every child of a position with a pending Betrayer — that node
guard is the important one.

An Act itself is **not** exempt. It is reduced like a quiet move, because an Act hands one of your
own pieces to the opponent unless your side then executes it, which makes it rarely the best move at
a node — the exact profile reductions exist for.

### Principal variation search

Once there is a best move, the goal for its siblings is to prove they are worse, not to find out by
how much. So the first move is searched properly and every sibling gets a deliberately narrow window
that can only answer "worse or not worse". That is much cheaper. A sibling that comes back "not
worse" has to be searched again properly.

It only pays off when ordering is already good, since it is a bet that the first move is best. On the
benchmark position it re-searches 381 times out of 86,201 scouts, a 0.4% miss rate — that number is
how you tell the bet is a good one. See `ScoreChild`.

### Forward pruning

Three techniques sharing one guard, all betting that a cheap static judgment is enough to know a
branch will not matter.

- **Reverse futility** returns the static evaluation immediately when it already clears beta by a
  depth-scaled margin, close to the horizon.
- **Move-count pruning** stops searching quiet moves once enough of them have failed to improve
  alpha at a node.
- **Frontier futility** skips a quiet move whose best-case static outlook still cannot reach alpha,
  unless the move gives check — a forcing move's value cannot be judged from a static score.

This family is where most of the search's speed comes from. It is also the riskiest thing in here,
because unlike alpha-beta itself these are heuristics that can genuinely discard a good move. The
guard is the same one null-move pruning uses: never at a node with a pending Betrayer, never in
check.

### Internal iterative reduction

A node reached for the first time at depth four or deeper has no transposition-table entry to order
its moves by, so its first pass searches close to blind. Before committing to the real search, run a
cheap one-ply-shallower search of the same position; its own recursive table writes usually leave
behind a move worth ordering by.

Costs a small extra search at cold deep nodes and buys ordering everywhere below them. The probe
disables itself for its own inner call, so a cold line cannot cascade into nested reductions all the
way down to the depth floor.

## Quiescence search

Stopping at a fixed depth means the search can stop halfway through a trade and score a position that
is about to swing wildly. Quiescence continues past the depth limit, but only through "loud" moves —
captures, and here also a Betrayal Act, which is never treated as quiet.

It asks for captures and Acts directly rather than generating every legal move and discarding most of
them, it skips captures whose best possible outcome still cannot help, and it uses the transposition
table. It also refuses to stand pat while a Betrayal sequence is unresolved, which is what guarantees
a sequence is always played out before anything gets scored.

The quiescence tree is still larger than the main tree — currently around 1.7 times its node count on
the benchmark position — which is normal for an engine and is where a good chunk of the remaining
time goes.

## How long a move takes

### Soft and hard time budgets

A tier's clock is two numbers rather than one. **Hard** is the ceiling the search must never cross,
and it is the promise to the player: three seconds at most, every tier. **Soft** is the target it
aims for, and the gap between them is the room a genuinely tactical position is allowed to spend.

Without two numbers, "stay a little longer to be sure" and "no cap at all" are the same thing.
`Assets/_Scripts/AI/AITimeBudget.cs`.

### Instability time management

Past the soft budget, a root that has settled — the same best move holding with a steady score across
the last completed depths — stops there rather than running the clock out. A root whose answer is
still moving is allowed to spend into the gap toward the hard ceiling, to buy one more depth's worth
of certainty before committing.

This is what stops the engine stalling for three seconds over a forced recapture it decided
instantly. It activates only for callers that opt in, which in practice means real gameplay and
on-device benchmarking; anything else runs to its configured depth.

### Aspiration windows

Search each depth after the first inside a narrow window guessed from the previous depth's score
rather than the full range, re-searching that depth with the full window when the guess fails.

**This ships switched off.** The flag defaults to false and nothing in the game turns it on. See the
next section for why.

## Reading the search from outside

Plain counters: how many nodes were visited, how often the table was probed and hit, how often each
pruning mechanism fired, how often a forced defection had to be resolved. Without them, "the search
got faster" is an anecdote, and there is no way to tell a technique that is working from one that is
silently disabled.

Every increment sits behind an editor or development-build guard, so a release build pays nothing for
the counting — not even a branch. `Assets/_Scripts/AI/SearchStats.cs`.

## What is verified, and what is not

Verified automatically, all in seconds:

| Property | Fixture |
|---|---|
| Move generation matches the engine's own count at depth 2 | `SearchPerftTests` |
| Fixed positions produce the exact expected move | `SearchCorrectnessTests` |
| An unanswerable Act is not scored as a stalemate draw | `SearchCorrectnessTests` |
| Personality never reads root scores that were never made exact | `SearchCorrectnessTests` |
| Turn and hash survive every search path intact | `ForwardPruningSafetyTests`, `NullMovePruningSafetyTests` |
| Null moves never fire mid-Betrayal, in check, or twice running | `NullMovePruningSafetyTests` |
| Reductions never touch a move that must not be reduced | `LateMoveReductionTests` |
| The ordering bands sort in the promised order | `MoveOrderingTierTests` |
| History and killer bonuses stay inside the quiet band | `HistoryHeuristicTests`, `KillerMoveOrderingTests` |
| The exchange calculator returns the right material result | `StaticExchangeEvaluationTests` |
| A narrow-window failure is always caught and re-searched | `AspirationWindowExperimentTests` |
| Entries survive a round trip, and a clear wipes every view of a shared table | `TranspositionTableTests`, `TranspositionTableLifecycleTests` |
| A winning capture is still found with delta pruning active | `QuiescenceDeltaPruningTests` |
| Null move, reduction and narrow-window activity all register | `SearchTelemetryTests` |
| The search allocates nothing | `SearchTelemetryTests` |
| Every tier arrives inside its hard budget and reaches a depth floor | `AIProfileSearchBenchmarkTests` |

One more check exists and is worth treating separately. `AIProfileStrengthGateTests` plays a handful
of short games per pairing and asserts no stronger tier is losing to a weaker one — the failure that
inverted the ladder once already. It runs in about a minute rather than seconds, and its clock is
compressed hard enough that a sample can swing thirty points between runs, so a single red result
from it is a prompt to look rather than proof of a regression.

If you go looking for that fixture, it lives inside `AIProfileStrengthOrderingTests.cs` alongside the
full statistical suite, which is marked explicit and does not run in an ordinary pass. Naming the file
is not the same as naming the class, and a command-line filter selects classes.

Three further things are **not** settled, and are worth knowing before you trust anything above:

**Aspiration windows have never been measured on this engine.** They are implemented, tested for
correctness, and switched off. The literature is genuinely mixed — there is a documented case of a
comparable engine measuring their *removal* as a net improvement — so a flag nobody flips is the
honest amount of commitment until someone runs the numbers here.

**A Betrayal search extension exists and is disabled.** The idea was to spend an extra ply where a
Betrayal sequence resolves. Once forced defections were scored honestly it measured at roughly double
the node count at depth 9, and it was never buying correctness in the first place, because quiescence
already refuses to stand pat mid-sequence. Depth spent uniformly buys more than depth spent only on
Betrayal lines. It is off by a single constant with the machinery still wired, so re-enabling it for
a measured comparison is a one-line change.

**Nothing proves the search consults static exchange evaluation.** The exchange calculator has its
own tests and they pass. But disable the guard both call sites check and no ordering test, no
benchmark, and no telemetry counter notices the search has stopped using it. The fixture covers the
calculator; nothing covers the wiring.

## What it costs: measured

One fixed midgame position searched to depth 7 on a desktop machine costs about three seconds and
323,000 nodes, roughly a third of them in the main tree and the rest in quiescence. Measured
2026-08-23. Treat the absolute numbers as specific to that machine and that position; the shape is
the part that travels.

The most recent speed work moved it like this:

| | Previously | Now |
|---|---:|---:|
| Wall clock | 5.02 s | 3.01 s |
| Main-search nodes | 99,528 | 117,808 |
| Quiescence nodes | 315,036 | 205,246 |
| Total | 414,564 | 323,054 |

The main-search count is **higher**, which looks wrong until you know why. The search now resolves
around 5,600 forced defections per search that it used to score as instant draws — more nodes, and
correct ones. The quiescence tail shrank by enough to pay for that twice over.

`SearchBenchmarkTests` reproduces the current numbers. Read the per-technique counters out of the
Unity log rather than the test results file: the timings are written with `Console.WriteLine`, which
never reaches the NUnit XML.
