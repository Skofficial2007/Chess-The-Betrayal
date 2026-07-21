# AI benchmark tooling

This folder holds the output of the AI strength/performance benchmark runs. If you're looking at
this after cloning the repo and want to check how strong the AI actually is, or whether a change
you made helped or hurt it, this is where to start.

## What each entry point gives you

**Fastest, no games needed — the fixed-position yardstick.** Runs in seconds. A handful of
hand-authored chess positions with provably correct answers (forced mates, positions where exactly
one move wins material by force). Run `YardstickStrengthTests` in the Unity Test Runner (or via
`-testFilter YardstickStrengthTests` in batchmode). A failure tells you the position, what move was
expected and why it's provably correct, what the AI chose instead, both moves' evaluation scores,
and how deep the search actually got — enough to tell an evaluation problem apart from a search
depth problem without playing a single game.

**Per-commit sanity check — the strength gate.** `AIProfileStrengthGateTests` plays a handful of
short, clock-compressed games between adjacent difficulty tiers and asserts only that a stronger
tier isn't losing outright to a weaker one. Fast enough to run on every commit; not precise enough
to trust for anything beyond "did something obviously break."

**On-demand check — the Quick tournament.** Menu: `Chess: The Betrayal/AI/Run Strength Benchmark
(Quick)`, or batchmode via `-executeMethod ChessTheBetrayal.EditorTools.Benchmark.BenchmarkMenu.
RunQuickBatch`. Plays real games at each tier's actual per-move time budget — the same clock a
player faces — across a small slice of positions, so it finishes in a few minutes rather than many.
This is the one to run after a change to confirm the difficulty ladder still holds.

**The full statistical suite.** `AIProfileStrengthOrderingTests` ([Explicit] — run it deliberately)
or the Full benchmark from the menu/batchmode. The same measurement as Quick, just over the entire
curated position set, for a tighter confidence interval. Takes many minutes; reach for Quick first
and only fall back to this when Quick's result isn't decisive enough.

## Reading a win rate

Every win rate this tooling reports comes with a `+/-` margin — its 95% confidence interval. An
8-game sample naturally swings by tens of percentage points on nothing but luck, so a result like
`52.0% +/-15.5%` is not a measurement of "52%," it's a measurement of "somewhere between roughly 37%
and 68%." When a result falls short of the strength floor but the floor is still inside that
interval, the tooling reports the finding as **inconclusive**, not failed — the sample is too small
to tell a real regression apart from noise. Only trust a Fail verdict; treat an Inconclusive one as
"run more games."

## If a run gets interrupted

Any run started through `BenchmarkRunner` with persistence enabled (the menu and batchmode entry
points both do this by default) streams every finished game to `Docs/Benchmarks/Runs/<run-id>/
run.jsonl` as it happens — killing the process at any point still leaves every game that finished
by then as real, readable data. A completed run additionally gets `report.json` (machine-readable)
and `summary.md` (a readable table) written once every game is done; their absence is exactly how
you can tell a run in that folder never finished. A run that goes more than a few minutes without a
single new game completing stops itself automatically (the tournament watchdog) rather than hanging
forever, and says so in the log.

## Docs/Benchmarks/baseline.json

The committed reference point every run diffs against, updated deliberately (never as a side effect
of a routine run) via `Chess: The Betrayal/AI/Update Benchmark Baseline...` in the Editor menu.
