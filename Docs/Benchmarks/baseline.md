# AI strength baseline

The committed reference point every benchmark run is compared against. Numbers here are
measured, not projected. Each row records which run produced it.

Recorded: `2026-07-29T21:01:19Z`

## How to read this

A score is `(wins + half the draws) / games` from the first tier's point of view, so 50%
means the two tiers are indistinguishable and higher means the first tier is stronger.
Every score carries a 95% confidence interval: a result is only a real pass or a real
failure when that whole interval sits on one side of the strength floor. When the floor
falls inside the interval the honest answer is that the sample cannot tell, which is
reported as inconclusive rather than dressed up as either.

The strength floor is **55%** and the per-move budget is
**3000 ms**.

## Ladder

| Stronger | Weaker | Games | Score | 95% CI | W / L / D | Draws | Verdict | Measured in |
|---|---|---:|---:|---:|---|---:|---|---|
| normal | easy | 40 | 81.2% | +/-15.5% | 31 / 6 / 3 | 8% | MeetsFloor | `Full-20260713-20260729-135224` |
| hard | normal | 40 | 93.8% | +/-15.5% | 35 / 0 / 5 | 13% | MeetsFloor | `Full-20260713-20260729-135224` |
| aggressive | normal | 40 | 95.0% | +/-15.5% | 37 / 1 / 2 | 5% | MeetsFloor | `Full-20260713-20260729-135224` |
| aggressive | easy | 40 | 100.0% | +/-15.5% | 40 / 0 / 0 | 0% | MeetsFloor | `Full-20260713-20260729-135224` |
| extreme | hard | 320 | 52.2% | +/-5.5% | 114 / 100 / 106 | 33% | Inconclusive | `Full-20260713-20260727-070405` |
| impossible | extreme | 320 | 51.4% | +/-5.5% | 114 / 105 / 101 | 32% | Inconclusive | `Full-20260713-20260727-070405` |

## Is the deepest tier actually stronger?

The floor above asks whether a pairing has regressed below 55%, which is the right question for
catching a change that made things worse. It is the wrong question for "is this tier stronger at
all," because a score can sit below the floor while still being comfortably ahead of even. That
question is answered by looking only at games that produced a winner, since a draw carries no
information about which side is better.

| Pairing | Decisive games | W / L | Win share | 95% CI | Stronger? |
|---|---:|---|---:|---:|---|
| extreme vs hard | 214 | 114 / 100 | 53.3% | [46.6, 60.0] | not distinguishable from a coin flip |
| impossible vs extreme | 219 | 114 / 105 | 52.1% | [45.4, 58.7] | not distinguishable from a coin flip |

**Neither of the two deepest steps in the ladder can be shown to be a real step up**, at the largest
sample this project has taken, by either test. The reason is visible in the depth column below: the
deepest tiers reach effectively the same depth as each other (extreme 7.60, impossible 7.66 mean
plies) despite being configured for 9 and 9. Tiers that search equally deep with the same evaluator
play equally well. This is a property of the search, not of the tier settings — raising a configured
ceiling that is already never reached would change nothing.

**This is now an accepted design position, not an open defect.** Later work lifted every deep tier
together rather than separating them, which is what a uniform search improvement does. Closing the
gap would need a difference in kind between those tiers — a different evaluator or a genuinely
different depth — not further tuning of the same search.

## Speed

Every tier stays inside the per-move budget on mean time per move, which is the number the drift
check enforces:

| Tier | Mean ms/move | Budget | |
|---|---:|---:|---|
| easy | 172 | 3000 | pass |
| normal | 1158 | 3000 | pass |
| impossible | 2028 | 3000 | pass |
| aggressive | 2363 | 3000 | pass |
| hard | 2703 | 3000 | pass |
| extreme | 2778 | 3000 | pass |

The dedicated timing suite, which additionally checks that the cancellation timer really stops a
search and that each tier still reaches a minimum depth inside its budget, passes on all six tiers.
Individual plies on a deliberately demanding position can finish a few tens of milliseconds past the
budget; that is the cancellation timer landing, not the budget being exceeded in any way a player
would notice.

## Per-tier search behaviour

Mean milliseconds per move is the number the per-move budget is judged against.

| Tier | Moves | ms/move | Nodes/move | Depth mean | min | max | Blunder rate | Acts | Measured in |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| easy | 4708 | 172 | 7661 | 3.00 | 2 | 3 | 24.3% | 6 | `Full-20260713-20260729-135224` |
| normal | 6118 | 1158 | 51967 | 4.99 | 2 | 5 | 6.9% | 15 | `Full-20260713-20260729-135224` |
| aggressive | 6966 | 2363 | 115229 | 6.67 | 1 | 7 | 3.9% | 18 | `Full-20260713-20260729-135224` |
| hard | 6823 | 2703 | 133358 | 7.22 | 1 | 8 | 0.6% | 7 | `Full-20260713-20260729-135224` |
| extreme | 6876 | 2778 | 143959 | 7.60 | 1 | 9 | 0.0% | 15 | `Full-20260713-20260729-135224` |
| impossible | 6594 | 2028 | 98472 | 7.66 | 1 | 9 | 0.0% | 11 | `Full-20260713-20260729-135224` |

Every row now comes from one whole-matrix run, so no tier's numbers are carried from a run
that did not play it. Depth is reported as a mean across real moves rather than the deepest
ply ever reached: one cheap position can push a maximum far above what the tier typically
manages, which is misleading in exactly the direction that flatters the engine.

**The four budget-bound tiers all search deeper than the previous baseline on fewer nodes**
— hard 6.85 to 7.22, aggressive 6.49 to 6.67, extreme 7.07 to 7.60, impossible 7.09 to 7.66,
with nodes per move down between 6% and 17%. Measured over thousands of moves per tier, this
is far better resolved than the 40-game pairings above, and it is the clearest evidence that
the move-ordering and settle-rule work did what it was meant to.

## Sources

- Top of ladder: `Docs/Benchmarks/Runs/Full-20260713-20260727-070405/`
- Remaining pairings and all per-tier search figures: `Docs/Benchmarks/Runs/Full-20260713-20260729-135224/`

The two pairings that decide whether the deepest tiers are genuinely ordered were measured
at a large sample specifically for this baseline. The rest come from the most recent
whole-matrix run, where they are settled by margins that further sampling would not move.

## What this baseline does not cover

The harness that produces these numbers drives the search directly and never opens an opening
book, so **no figure here can move when the book changes**. Book and trap-book work is measured
separately, and has been measured as worth nothing in Elo terms: 640 games with and without the
book came out at 49.8%, −1 Elo, interval [46.0%, 53.7%]. Treat that work as content and
correctness, never as strength, and do not expect this page to reflect it.
