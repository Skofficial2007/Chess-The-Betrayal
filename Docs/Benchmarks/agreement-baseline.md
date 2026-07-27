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

`raw` and `as-played` being equal for all three tiers is expected rather than surprising: these
profiles carry no blunder rate and no tie-break window, so there is nothing for the selection
policy to change.

## Position stability

A separate screen asked whether each position's reference answer holds still, by searching the two
plies below the reference depth and requiring all three to agree.

**9 of 20 stable** (0, 3, 6, 7, 9, 10, 13, 17, 18); **11 of 20 unstable** (1, 2, 4, 5, 8, 11, 12,
14, 15, 16, 19).

On an unstable position the reference's answer is a fact about which depth was searched rather than
about the position, so agreement measured there is ply parity dressed up as strength. **A majority
of the curated set is affected.** Any future agreement figure should either restrict itself to the
stable subset or report the two groups separately; the headline above deliberately does neither,
because its own conclusion is that the depth ceiling dominates everything else here.
