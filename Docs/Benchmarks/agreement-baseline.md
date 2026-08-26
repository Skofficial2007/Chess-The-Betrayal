# Reference-move agreement baseline

How often each of the top three tiers plays the move a much deeper search would, measured over
the curated position set. Numbers here are measured, not projected.

Recorded: `2026-07-27`. Reproduce with `ReferenceAgreementBaselineProbe` (explicit; the run below
took 74 minutes).

## How to read this

Agreement asks a different question from the win-rate ladder in `baseline.md`. Instead of playing
two tiers against each other, it asks each tier one question per position and compares its answer
to a depth-10 reference search. There are no draws to dilute the signal and one clear answer per
position, which is why it was built: between two near-identical engines most games are drawn and a
draw says nothing about which side was better.

`raw` is the search's own best move — the clean evaluation signal. `as-played` is the move the
profile would really play once its personality dials have acted. `cut short` counts positions where
the search hit its clock before reaching its configured depth ceiling, and it is the column that
decides how to read everything else: a disagreement at full depth points at the evaluator, while
one that ran out of clock points at speed.

## Result

| Tier | Ceiling | Raw agreement | As-played | Cut short | Depth reached |
|---|---:|---:|---:|---:|---|
| hard | 8 | 8/20 (40.0%) | 8/20 (40.0%) | 19/20 | 6-7 |
| extreme | 9 | 8/20 (40.0%) | 8/20 (40.0%) | 20/20 | 6-7 |
| impossible | 9 | 8/20 (40.0%) | 8/20 (40.0%) | 20/20 | 6-7 |

Reference: `impossible` evaluator, depth 10, over all 20 curated positions.

## What it says

**All three tiers score identically, and none of it is an evaluation measurement.** Every single
disagreement — 12 per tier — is annotated as cut short of the depth ceiling. The tiers reach depth
6-7 against ceilings of 8 and 9, so on these positions they are not three different strengths
being compared; they are the same search stopping in the same place.

That is the same conclusion the win-rate ladder reached from the opposite direction: `extreme` vs
`hard` at 52.2% and `impossible` vs `extreme` at 51.4% are coin flips because the tiers reach the
same effective depth. Two instruments with nothing in common now agree on the cause.

The practical consequence is that **this instrument cannot grade an evaluation change until the
search reaches its ceiling.** Comparing agreement before and after an evaluation tweak would
mostly compare which positions happened to time out.

`raw` and `as-played` being equal for all three tiers is expected rather than surprising. Only
`hard` carries any blunder rate at all (2%), and over 20 positions that will usually fire on none
of them; the tie-break windows are narrow enough (15cp and 10cp) that the selection policy rarely
finds a second move close enough to the best one to swap in.

## Position stability

A separate screen asked whether each position's reference answer holds still, by searching the two
plies below the reference depth and requiring all three to agree.

**9 of 20 stable** (1, 4, 7, 8, 10, 11, 13, 17, 18); **11 of 20 unstable** (0, 2, 3, 5, 6, 9, 12,
14, 15, 16, 19). Re-measured `2026-08-25` at `eaca09c`, three times, identical every time.

The list this replaced named the same counts and four different positions, which is worth knowing
about before trusting any screen like this: read only the summary line and it confirms itself. It
was taken before the table below, so it never saw the late-move-reduction change, and the search has
since also learned to notice a line returning to a position it came from.

On an unstable position the reference's answer is a fact about which depth was searched rather than
about the position, so agreement measured there is ply parity dressed up as strength. **A majority
of the curated set is affected.** Any future agreement figure should either restrict itself to the
stable subset or report the two groups separately; the headline above deliberately does neither,
because its own conclusion is that the depth ceiling dominates everything else here.

## Re-measured after the search reached deeper

Recorded `2026-07-27`, on the same 20 positions with the same depth-10 reference, after late move
reduction was changed to scale with depth and list position. The point of repeating it was that the
reading above is explicitly not an evaluation measurement: it says the search stops before its
ceiling, so agreement mostly records which positions ran out of clock.

| Tier | Ceiling | Raw agreement | As-played | Cut short | Depth reached |
|---|---:|---:|---:|---:|---|
| hard | 8 | 12/20 (60.0%) | 12/20 (60.0%) | 10/20 | 6-8 |
| extreme | 9 | 6/20 (30.0%) | 6/20 (30.0%) | 19/20 | 7-9 |
| impossible | 9 | 13/20 (65.0%) | 13/20 (65.0%) | 18/20 | 6-8 |

**The tiers separated.** The identical 8/20 across all three is gone, and `hard` halved the number
of positions that ran out of clock, from 19 to 10. That is the change the first table said it was
waiting for.

**`extreme` moving the other way is not a regression, and the reason matters for anyone using this
instrument again.** The reference is a depth-10 search using the `impossible` profile's evaluator,
which is neutral: no Betrayal aggression, no attack/defense bias. `hard` and `impossible` are also
neutral on both dials, so they are being asked to agree with an oracle that values positions the
same way they do. `extreme` is the only tier that is not — it carries 0.3 Betrayal aggression and a
1.2 attack/defense bias — so it is being graded against an evaluator it deliberately disagrees
with, and searching deeper lets it pursue its own preferences further rather than converge on the
reference's.

The supporting number: on the positions where they disagree, `extreme` searches *deeper* than the
other two (mean 7.7 plies against 7.0 and 7.1) while agreeing less. A tier that is simply short of
depth cannot produce that combination.

## Re-measured with the stability screen wired in

Recorded `2026-08-25` at `eaca09c`, same 20 positions, same depth-10 reference. The run took 24
minutes. Earlier tables record a date and not a revision, which is what let the stability list above
go a month out of date without anything saying so.

| Tier | Ceiling | Raw agreement | As-played | Cut short | Raw, stable positions only |
|---|---:|---:|---:|---:|---:|
| hard | 8 | 13/20 (65.0%) | 8/20 (40.0%) | 5/20 | 8/9 (88.9%) |
| extreme | 9 | 6/20 (30.0%) | 5/20 (25.0%) | 19/20 | 3/9 (33.3%) |
| impossible | 9 | 12/20 (60.0%) | 12/20 (60.0%) | 17/20 | 8/9 (88.9%) |

**Raw agreement is reproducible.** Against the table above it moves by at most one position on every
tier, across a month and a great deal of merged work. That is the strongest evidence so far that this
instrument measures something real rather than whatever the machine was doing that afternoon.

**The last column is new, and it is the one to read.** The headline divides by all twenty positions,
eleven of which the reference cannot hold still on — there, agreeing or disagreeing with it is ply
parity. Restricted to the nine it can speak to, `hard` and `impossible` both reach 8 of 9, and the
gap between them closes entirely. Twenty-four points of the headline spread was noise.

**`hard` lost as-played while gaining raw.** Its own best move now matches the reference more often
(12 to 13) and the move it would actually play matches far less (12 to 8), so its personality dials
are discarding five more correct moves than before. That follows a deliberate change to when a tier
is allowed to apply those dials, and it is the number to watch if that decision is revisited.

**`hard` halved its cut-short count again**, 10 to 5, so most of its disagreements are now genuine
evaluation disagreements rather than positions that ran out of clock. The first table's central
conclusion — that this instrument could not grade an evaluation change because the depth ceiling
dominated everything — no longer holds for `hard`. It still holds for the other two.

So this instrument measures agreement-with-the-reference-evaluator, not strength, and it is only
directly comparable across tiers that share the reference's dials. **Read `extreme`'s number
against its own history, never against `hard` or `impossible`.** Grading it properly would need a
reference built from its own weights.

## Reference cost, re-measured

The reference depth is a cost/quality trade, so the curve behind it was re-measured on the same tip
as the second table above, over three positions.

| Depth | Mean per position | Ratio to previous |
|---|---:|---:|
| 8 | 2,093 ms | — |
| 9 | 3,316 ms | 1.58x |
| 10 | 10,732 ms | 3.24x |
| 11 | 20,271 ms | 1.89x |
| 12 | 27,231 ms | 1.34x |

**The curve flattens above depth 10.** Each extra ply costs proportionally less than the one before
it, so a depth-12 reference is roughly 2.5x a depth-10 one rather than the far steeper multiple a
constant per-ply cost would predict. Raising the reference depth is cheaper than it looks.

There is a reason to consider doing so. **The depth-10 reference has not converged on these
positions.** Position 0 answers differently at 10, 11 and 12; position 2 changes at 12 and returns
to its depth-10 answer. An oracle that still changes its mind is reporting which depth it searched
as much as what the position holds, which is the same instability the position-stability screen
above found in a majority of the curated set - here it is in the reference itself rather than in
the positions.

That does not invalidate the agreement numbers, which are all measured against the same reference
and so remain comparable to each other. It does mean the reference's answer is not a ground truth,
and a future pass wanting a firmer one can buy depth 12 for less than the old curve suggested.
