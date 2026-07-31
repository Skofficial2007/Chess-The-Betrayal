# Mobile / on-device search benchmark

Everything in `Docs/Benchmarks/baseline.md` was measured on one 16-core desktop. The game ships to
Android across a wide device range, and the 3-second per-move budget is what protects a player on
the slowest phone that still runs the game — not the desktop mean. This page is the record of what
the on-device instrument (`Assets/_Scripts/AI/DeviceBenchmark/`) actually measures, what it
deliberately does not, and the numbers gathered so far.

## How long it takes, and why that number is trustworthy

**The tester run is bounded at 2m 20s on any device**, and the app shows that bound on screen before
it starts.

That bound is exact rather than optimistic, because each search is stopped by a wall-clock timer
rather than by finishing its work. A slower phone does not spend longer on a search — it reaches a
shallower depth inside the same milliseconds. Adding up every search's own hard budget therefore
gives a ceiling that holds on the fastest desktop and the slowest phone alike. A weak device trends
toward that ceiling, because fewer of its searches finish early enough to stop on their own, but it
cannot go past it.

Measured on a desktop (i5-13500HX): **2m 13s wall clock**, of which roughly 1m 35s was search and the
rest Unity starting up. A phone will land nearer the 2m 20s ceiling.

**The exhaustive run is a different thing entirely and must never be handed to a tester.** Every
position, every repeat, play-forward included, on both thread contexts is roughly 3,960 searches and
a worst case near **three hours** — an early attempt at it was still running, unfinished, after 71
minutes on the desktop above. It exists for a machine you own and can leave alone.

## Running it, and getting the report back

The app opens on a Start button and does nothing until it is pressed. That is deliberate: a run
that began on scene load would be timing a phone still settling from launch, and could not be
repeated without relaunching. Pressing Start again after a run finishes starts a genuinely fresh
one — new runner, new report, new run id — so two readings from the same phone are never blended.

Results appear as they arrive. The screen keeps the header, the status line and the per-tier summary
complete at all times, but shows only the newest stretch of the scrolling detail log and says on the
page how many lines it is not showing. That cap exists because TextMeshPro stops drawing a text
object past 16,383 characters without warning, and a tester run's report already runs to roughly
13,000 — an uncapped log would quietly start losing the newest lines, which are the ones being
watched. A saved copy has everything.

**Download** stays greyed out and unpressable until a run completes. Pressing it writes the whole
report, unstyled and untrimmed, as a `.txt` under `Application.persistentDataPath`, named for the
device and the moment it ran (`chess-ai-benchmark_<device>_<yyyyMMdd-HHmmss>.txt`) so a folder full
of reports from several testers stays attributable and nothing overwrites anything. The full path is
appended to the on-screen log and to the player log.

On Android that folder is somewhere a phone's file manager will not go, so the save is followed by a
share sheet carrying the report as text — the tester picks whatever they already use to send things.
It carries text rather than an attached file specifically so it needs no storage permission and no
extra manifest entries. It is best effort: if a device refuses the intent, the file is already
written and its path is already on screen.

**One caveat worth stating plainly.** The write is one ordinary file write and runs identically in
the editor and on a phone, so an editor run genuinely exercises it. The share sheet is Android-only
code that never executes in the editor, so a working editor run says nothing about whether it works
on a device — that part is only proven by a real build.

## What it measures

Every search, in either plan, is built the same way a real match builds one — the production
transposition table size, the profile's real evaluator weighting, the profile's real rescore
margin — so a number here means the same thing a real move would have cost on that device.

**The tester plan** (what a build runs by default, 54 cells): for all six tiers, a **cold single
search** on four positions — both hand-placed depth probes plus two curated openings from opposite
ends of the set — twice each, dispatched on a thread-pool worker exactly as `AsyncAIAgent` does. Plus
a small main-thread control on one position, to show whether a device's scheduler treats background
work differently. The two thread contexts are reported separately and never averaged together, since
averaging would hide precisely the difference the control exists to find.

A cold search is deliberately the only thing measured here: an empty transposition table and no move
ordering carried in from a previous ply is the least favourable case, and the per-move promise is
about exactly that case. Playing several moves forward reuses a warm table and so runs easier.

**The exhaustive plan** (opt-in, for a machine you own): every position, every repeat, play-forward
included, on both thread contexts.

Because every tier's hard budget is already at or under three seconds, the number worth reading is
never the raw mean time — it's **overshoot past that tier's own budget**, and **depth reached**.
A tier that stays inside its budget while reaching a shallower depth than the desktop is a real,
player-visible weakness even though it never technically misses a deadline; a tier that goes even a
little past its own budget on some device is the exact failure this instrument exists to catch.

## What it does not measure

- **Win rate or strength.** This instrument answers "how fast and how deep," never "who wins." For
  strength, see `Docs/Benchmarks/baseline.md` — a completely different instrument (`MatchSimulator`,
  real self-played games) measuring a completely different question. Never compare a number from one
  page against the other; they aren't measuring the same thing even where their wiring matches.
- **The opening book.** The runner builds `AlphaBetaSearch` directly and never consults
  `OpeningBookPolicy` — no figure here can move when the book changes, and a change to the book has
  nothing to say about anything measured on this page.
- **How often a player actually reaches a slow position.** The position set is deliberately the
  positions already known to be expensive (the curated openings a real game's own budget was
  measured running out on, plus two hand-placed positions built to grow expensive at depth) — not a
  random sample of ordinary play. It answers "how bad does the worst case get," not "how often does
  the worst case happen."
- **The multi-move line's search tree under normal play.** The play-forward loop always runs under
  `BetrayalUsage.DefendOnly`, because it has no model for a Betrayal Retribution sequence and would
  misread the game as over the moment one appeared at the root. Only the single cold search measures
  `BetrayalUsage.Full` — the setting a player actually gets unless they turn Betrayal off for the AI.
- **Anything beyond raw search wall-clock.** No animation pacing, no UI thread cost, no rendering —
  purely how long `AlphaBetaSearch.FindBestMove` takes to return.
- **A definitive "this run was interrupted" flag.** Repeats past the first are not independent
  measurements of a different outcome — the search is deterministic given identical inputs, so a
  repeat exists only to catch OS scheduling and thermal noise, not a different move being found. And
  since nothing can guarantee cleanup code runs after a crash or a forced quit, a partial run is
  recognizable only by its own last line: a `STATUS: RUNNING n/N` with no later `STATUS: COMPLETE`
  already means it didn't finish, honestly, without needing an explicit flag that a hard kill
  couldn't set anyway.

## The gate

A move must never cross its own tier's hard budget — every tier is 3000 ms or less
(`AIProfileTable.cs`: easy 1300 ms, normal 2250 ms, hard/aggressive/extreme/impossible all 3000 ms).
Any recorded overshoot on any device is a real finding, not noise, and gets a profile-row fix with
the failing and corrected numbers both recorded — never a loosened gate.

## Desktop reference

Captured with `MobileBenchmarkDesktopCaptureTests` (`[Explicit]` — run it deliberately, read the
summary from the log), the same plan a phone runs, so this row is the only valid comparison point for
a device number. Never compare a device row against `baseline.md`.

Machine: i5-13500HX, Editor, Mono. Captured `2026-07-30`. Worker-thread figures (the production
path); 8 samples per tier.

| Tier | Budget | Worst elapsed | Worst overshoot | Depth worst / mean |
|---|---:|---:|---:|---:|
| easy | 1300 ms | 0.22 s | none | 3 / 3.0 |
| normal | 2250 ms | 2.12 s | none | 5 / 5.0 |
| hard | 3000 ms | 3.01 s | +14 ms | 7 / 7.8 |
| aggressive | 3000 ms | 3.01 s | +10 ms | 7 / 7.0 |
| extreme | 3000 ms | 3.01 s | +15 ms | 7 / 7.8 |
| impossible | 3000 ms | 3.01 s | +14 ms | 7 / 7.8 |

**Reading this row.** The two shallow tiers finish well inside their budgets and reach their
configured depth ceiling, so their timings say more about how little work they were asked to do than
about the machine. The four deeper tiers are all budget-bound — pinned at their 3-second cap by
construction — so for them the only number carrying information is the depth reached. That is the
column a device gets ranked on.

Overshoot is 10–15 ms across every deep tier. That is the cancellation check landing at the next node
boundary rather than mid-node, not the budget being missed in any sense a player could perceive, and
it is the figure a device result should be compared against: a phone showing a few tens of
milliseconds is behaving normally, one showing hundreds is a real finding.

The main-thread control matched the worker pass on time (also 3.01 s, +2 to +12 ms) with no
meaningful difference on this machine. Its depth column is not directly comparable — the control runs
a single sample per tier against the worker pass's eight, so its "worst" is drawn from a much smaller
pool. The control exists to catch a device whose scheduler treats background work differently, which
is a mobile concern; a desktop showing no difference is the expected result, not a finding.

## Per-device results

The user builds; testers/devices run the app with `DeviceBenchmark.unity` as the boot scene, press
Start, and send back the saved report. One row per device once a full run completes on it.

| Device | Chipset (GPU proxy) | Worst-case overshoot | Tier that overshot | Deepest tier's depth reached (worst-case) | Verdict | Notes |
|---|---|---:|---|---:|---|---|
| _(none yet)_ | | | | | | |

## Build config this was measured under

Pinned in `ProjectSettings/ProjectSettings.asset`, not left on template defaults: IL2CPP, ARM64 only,
IL2CPP configuration Release, code generation "faster runtime," managed stripping Low, target API 36,
min API 26. Never compare device numbers captured under different settings than these — a timing
difference would measure the configuration change, not the phone.

## Using this on your own project

If you've forked this project and changed the AI or the rules, this instrument is built to be
re-run rather than rebuilt.

`DeviceBenchmark.unity` ships in the project but is disabled in Build Settings
(`enabled: 0` in `ProjectSettings/EditorBuildSettings.asset`), so a normal player build never runs
it. To get a device number of your own, either open the scene and press Play in the Editor, or
enable it in Build Settings and move it to index 0 for a dedicated benchmark build — never enable it
in a build you intend to ship to players.

Capture your own desktop reference first, with `MobileBenchmarkDesktopCaptureTests`
(`[Explicit]` — run it deliberately). A device row only means something next to a desktop row taken
with this same harness; the numbers on this page are this project's own and are not a baseline for
a fork that has changed the AI or the rules.

If you changed evaluation weights, search parameters, or added/removed a difficulty tier: those all
live in `AIProfileTable`, and the benchmark resolves every tier through the same
`AIProfileTableProvider.Resolve` a real match uses — a profile-row change is picked up with no
benchmark code edit at all.

If you changed the rules themselves — a new piece, a new legal move, a different board size — the
positions the benchmark searches will not update on their own. They come from hard-coded piece
placements in `CuratedOpeningLines` and `DepthWallPositions`, both written for this game's rules;
you'll want to replace them with positions of your own before trusting a number from a changed
ruleset.

The tester plan (`BenchmarkPlan.Tester()`) is the only one safe to hand to someone else — its 2m20s
worst case is provable from `AIProfileTable`'s own budgets before a single search runs. The
exhaustive plan (`BenchmarkPlan.Exhaustive()`) can run for hours and is for a machine you own;
switching which one a build runs is the one line in `DeviceSearchBenchmark.BuildPlan()`. Whichever
you use, work out its worst case the same way before handing it to anyone — a run nobody finishes
measures nothing.

Record your fork's own results in a copy of this page, not by editing the table above — that table
is this project's baseline, not a template.
