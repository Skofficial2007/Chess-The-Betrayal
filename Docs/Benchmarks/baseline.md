# AI strength baseline

The committed reference point every benchmark run is compared against. Numbers here are
measured, not projected. Each row records which run produced it.

Recorded: `2026-07-27T09:56:34Z`

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
| normal | easy | 40 | 91.3% | +/-15.5% | 35 / 2 / 3 | 8% | MeetsFloor | `Full-20260713-20260723-180948` |
| hard | normal | 40 | 92.5% | +/-15.5% | 34 / 0 / 6 | 15% | MeetsFloor | `Full-20260713-20260723-180948` |
| aggressive | normal | 40 | 90.0% | +/-15.5% | 33 / 1 / 6 | 15% | MeetsFloor | `Full-20260713-20260723-180948` |
| aggressive | easy | 40 | 100.0% | +/-15.5% | 40 / 0 / 0 | 0% | MeetsFloor | `Full-20260713-20260723-180948` |
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
three deepest tiers all reach the same effective depth (6.85, 7.07, 7.09 mean plies) despite being
configured for 8, 9 and 9. Tiers that search equally deep with the same evaluator play equally well.
This is a property of the search, not of the tier settings — raising a configured ceiling that is
already never reached would change nothing.

## Speed

Every tier stays inside the per-move budget on mean time per move, which is the number the drift
check enforces:

| Tier | Mean ms/move | Budget | |
|---|---:|---:|---|
| easy | 160 | 3000 | pass |
| normal | 1178 | 3000 | pass |
| aggressive | 2499 | 3000 | pass |
| hard | 2714 | 3000 | pass |
| impossible | 2263 | 3000 | pass |
| extreme | 2838 | 3000 | pass |

The dedicated timing suite, which additionally checks that the cancellation timer really stops a
search and that each tier still reaches a minimum depth inside its budget, passes on all six tiers.
Individual plies on a deliberately demanding position can finish a few tens of milliseconds past the
budget; that is the cancellation timer landing, not the budget being exceeded in any way a player
would notice.

## Per-tier search behaviour

Mean milliseconds per move is the number the per-move budget is judged against.

| Tier | Moves | ms/move | Nodes/move | Depth mean | min | max | Blunder rate | Acts | Measured in |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| easy | 4979 | 160 | 7144 | 3.00 | 3 | 3 | 23.5% | 11 | `Full-20260713-20260723-180948` |
| normal | 6270 | 1178 | 53489 | 4.99 | 2 | 5 | 7.4% | 14 | `Full-20260713-20260723-180948` |
| aggressive | 7262 | 2499 | 124774 | 6.49 | 1 | 7 | 3.0% | 12 | `Full-20260713-20260723-180948` |
| hard | 13739 | 2714 | 125559 | 6.85 | 1 | 8 | 1.4% | 26 | `Full-20260713-20260727-070405` |
| extreme | 26477 | 2838 | 130475 | 7.07 | 1 | 9 | 0.0% | 36 | `Full-20260713-20260727-070405` |
| impossible | 12772 | 2263 | 98496 | 7.09 | 1 | 9 | 0.0% | 22 | `Full-20260713-20260727-070405` |

A run covering only part of the ladder exercises only the tiers in its own pairings, so
rows it did not play are carried from the most recent whole-matrix run and labelled as
such. Depth is reported as a mean across real moves rather than the deepest ply ever
reached: one cheap position can push a maximum far above what the tier typically manages,
which is misleading in exactly the direction that flatters the engine.

## Sources

- Top of ladder: `Docs/Benchmarks/Runs/Full-20260713-20260727-070405/`
- Remaining pairings: `Docs/Benchmarks/Runs/Full-20260713-20260723-180948/`

The two pairings that decide whether the deepest tiers are genuinely ordered were measured
at a large sample specifically for this baseline. The rest come from the most recent
whole-matrix run, where they are settled by margins that further sampling would not move.
