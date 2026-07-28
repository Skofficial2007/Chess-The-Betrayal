# Making early search exit conditional on move-ordering health

Measured with `CappedDepthCeilingReverifyTests`, which searches under the exact contract a real
match gives the engine — the same per-move time budget and the same settle-early logic — and
records the depth each search actually reached. Depth reached is the number judged here. Node
counts and elapsed time are supporting evidence only: a cheaper tree does not automatically mean
a deeper search, and this project has previously measured a large node-count win that bought no
depth at all.

## The problem

Iterative deepening stopped early once the best root move had held steady across a few depths and
the soft time budget had passed. That treats move persistence as convergence. The two only agree
when the search was ordering its moves well enough that a refutation would have surfaced if one
existed — and when ordering is poor, the root move also stops changing, because the search cannot
see deep enough to dislodge it. From move persistence alone the two are identical.

The consequence showed up in the stop reasons. Of thirty-two measured searches, eleven stopped
early, and they were concentrated on the positions least able to afford it:

| position | first-move cutoff rate | depth reached | why it stopped |
|---|---:|---|---|
| Dutch Defence line | 0.32 | 7 / 7 / 7 / 7 | settled early, all four tiers |
| Italian Game line | 0.37 | 7 / 7 / 7 / 7 | settled early, all four tiers |
| Queen's Gambit Declined line | 0.39 | 8 / 7 / 8 / 8 | mixed |
| semi-open middlegame | 0.44 | 8 / 7 / 8 / 8 | settled early |
| tactical middlegame | 0.58 | 8 / 7 / 9 / 9 | reached its ceiling |

The three weakest-ordered positions stopped two plies short of their ceilings having spent roughly
a third of their available time. The budget was being spent backwards: the searches that needed
depth most were the ones giving it up first.

## The change

A settle is now trusted only when the first-move cutoff rate shows ordering is working. Below that
share, apparent stability is treated as unproven and the search continues on time it already had.
Nothing extends past the hard budget, so the per-move ceiling is unchanged.

The threshold sits in the gap between the two groups above — beneath where healthy positions sit,
above where starved ones do. That is what makes it separate the two cases rather than simply
switching early exit off. Measured alternatives:

| threshold | total depth over 32 cells |
|---|---:|
| no gate (previous behaviour) | 247 |
| 0.42 | **251** |
| 0.45 | 250 |
| 0.50 | 250 |

Raising it further starts overriding searches that had genuinely converged, which costs time
without buying depth.

## Held out from the tuning

The threshold was chosen by reading eight positions, so it was then checked against the seventeen
curated opening lines those eight do not include — sixty-eight further searches the choice never
saw:

| | total depth over 68 held-out cells | mean |
|---|---:|---:|
| previous behaviour | 495 | 7.279 |
| with the gate | **509** | **7.485** |

The gain on unseen positions is proportionally larger than on the ones used to pick the threshold,
which is the evidence that it reflects a real effect rather than a fit to those eight.

## Gates

Per-move time budget held: profile benchmarks 12/12, search benchmarks 3/3. Won endgames still
convert: conversion proofs 13/13, defection-aware endgames 8/8 — both were run because reduction
and stopping changes have previously broken mating technique while every other test stayed green.
Search allocation unchanged at a zero-byte delta; search correctness 10/10.
