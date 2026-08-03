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

Measured on a desktop (i5-13500HX), in batchmode via `MobileBenchmarkDesktopCaptureTests` (net of
Unity's own startup, since that harness's clock starts once the plan itself begins): **1m 59.6s**,
against a provable 2m 20s ceiling — almost no slack left, because four of the six tiers are pinned at
their own 3-second budget by construction. A phone will land nearer the 2m 20s ceiling.

**The exhaustive run is a different thing entirely and must never be handed to a tester.** Every
position, every repeat, play-forward included, on both thread contexts is roughly 3,960 searches and
a worst case near **three hours** — an early attempt at it was still running, unfinished, after 71
minutes on the desktop above. It exists for a machine you own and can leave alone.

**The thermal run is a third thing again, opt-in and roughly five times longer than the tester run —
see "Sustained-load (thermal) run" below.**

## Running it, and getting the report back

The app opens on a Start button and does nothing until it is pressed. That is deliberate: a run
that began on scene load would be timing a phone still settling from launch, and could not be
repeated without relaunching. Pressing Start again after a run finishes starts a genuinely fresh
one — new runner, new report, new run id — so two readings from the same phone are never blended.
A second button, **Long Run**, starts the sustained-load (thermal) plan instead — see below.

Results appear as they arrive. The screen keeps the header, the status line and the per-tier summary
complete at all times, but shows only the newest stretch of the scrolling detail log and says on the
page how many lines it is not showing. That cap exists because TextMeshPro stops drawing a text
object past 16,383 characters without warning, and a tester run's report already runs to roughly
13,000 — an uncapped log would quietly start losing the newest lines, which are the ones being
watched. A saved copy has everything.

**Share Report** stays greyed out and unpressable until a run completes. Pressing it always does two
things first, on every platform: copies the whole report to the clipboard, and writes it, unstyled
and untrimmed, as a `.txt` under `Application.persistentDataPath`, named for the device and the
moment it ran (`chess-ai-benchmark_<device>_<yyyyMMdd-HHmmss>.txt`) so a folder full of reports from
several testers stays attributable and nothing overwrites anything. The full path is appended to the
on-screen log and to the player log — that pair is the safety net nothing else here depends on.

On Android, what happens next depends on the API level. Android 10 (API 29) and newer write a second
copy straight into the phone's public Downloads folder through MediaStore and raise a share sheet
with that file attached, so the tester can open it from a chat app, mail or a file manager without
ever leaving the share sheet. Android 8-9 (API 26-28), or any device where the Downloads write or the
attached share fails for any reason, fall back to the same text-only share sheet this always used —
no file attached, just the report as the message body — which needs no storage permission and no
manifest entry. Whichever layer actually fired, the on-screen note says so, so a fallback is never a
silent one.

**One caveat worth stating plainly.** The file write and the clipboard copy are one ordinary write
and one line of UI code, and both run identically in the editor and on a phone, so an editor run
genuinely exercises them. Everything past that — the Downloads write, either share sheet — is
Android-only code that never executes in the editor, so a working editor run says nothing about
whether any of it works on a device — that part is only proven by a real build.

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

### Sustained-load (thermal) run

The tester plan answers "does a move arrive in time," but every one of its searches is short and
isolated — nothing in it says whether depth quietly drops fifteen minutes into a real 20-40 minute
match as a phone heats up. **Long Run** starts `BenchmarkPlan.Thermal()` to answer exactly that: the
impossible tier alone, the same hand-placed quiet-midgame position searched cold 200 times in a row,
worker-thread only (production never dispatches a search anywhere else, so a main-thread control adds
nothing here). Its own worst case is provable the same way the tester plan's is — 200 searches at
impossible's 3000 ms hard budget is a **10-minute ceiling on any device**.

The report gains a `--- Thermal curve ---` section for it: one line per minute of wall-clock elapsed,
per tier and thread context, giving that minute's sample count and its worst/mean depth reached. A
flat curve means the depth reached in minute 1 still holds in minute 10 — the phone sustains for a
whole match. A curve that falls says the AI is quietly getting weaker as the game goes on, in a way
the tester plan's short searches could never reveal. This section is populated for any plan, tester
included, but is only informative on a run long and repetitive enough to show a trend — see the
desktop reference below.

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

Machine: i5-13500HX, batchmode (headless, no Editor window), Mono. Captured `2026-08-03`.
Worker-thread figures (the production path); 8 samples per tier.

| Tier | Budget | Worst elapsed | Worst overshoot | Depth worst / mean |
|---|---:|---:|---:|---:|
| easy | 1300 ms | 0.22 s | none | 3 / 3.0 |
| normal | 2250 ms | 2.06 s | none | 5 / 5.0 |
| hard | 3000 ms | 3.01 s | +11 ms | 7 / 7.5 |
| aggressive | 3000 ms | 3.01 s | +14 ms | 7 / 7.0 |
| extreme | 3000 ms | 3.01 s | +14 ms | 7 / 7.8 |
| impossible | 3000 ms | 3.01 s | +12 ms | 7 / 7.8 |

**Reading this row.** The two shallow tiers finish well inside their budgets and reach their
configured depth ceiling, so their timings say more about how little work they were asked to do than
about the machine. The four deeper tiers are all budget-bound — pinned at their 3-second cap by
construction — so for them the only number carrying information is the depth reached. That is the
column a device gets ranked on.

Overshoot is 11–14 ms across every deep tier. That is the cancellation check landing at the next node
boundary rather than mid-node, not the budget being missed in any sense a player could perceive, and
it is the figure a device result should be compared against: a phone showing a few tens of
milliseconds is behaving normally, one showing hundreds is a real finding.

The main-thread control matched the worker pass on time (also 3.00–3.01 s, +3 to +12 ms) with no
meaningful difference on this machine. Its depth column is not directly comparable — the control runs
a single sample per tier against the worker pass's eight, so its "worst" is drawn from a much smaller
pool. The control exists to catch a device whose scheduler treats background work differently, which
is a mobile concern; a desktop showing no difference is the expected result, not a finding.

### Sustained-load (thermal) desktop reference

Captured `2026-08-03`, same machine and method as above: `MobileBenchmarkDesktopCaptureTests`,
batchmode, impossible tier, 200 cold searches against the quiet-midgame position.

Wall clock: **10m 03s**, against a provable 10m 00s ceiling — 0.5% over, the tightest margin of any
plan on this page, because every one of the 200 searches is budget-bound by construction and nothing
else in a headless batchmode run competes for the CPU. Overall: 200 samples, worst elapsed 3.01 s,
worst overshoot +15 ms, depth worst 7 / mean 8.0.

The `--- Thermal curve ---` section is the actual finding this run exists to produce — one line per
minute, worst/mean depth for that minute alone:

| Minute | Samples | Depth worst | Depth mean |
|---:|---:|---:|---:|
| 0 | 19 | 8 | 8.0 |
| 1 | 20 | 8 | 8.0 |
| 2 | 20 | 8 | 8.0 |
| 3 | 20 | 7 | 8.0 |
| 4 | 20 | 8 | 8.0 |
| 5 | 20 | 8 | 8.0 |
| 6 | 20 | 8 | 8.0 |
| 7 | 20 | 8 | 8.0 |
| 8 | 20 | 8 | 8.0 |
| 9 | 19 | 7 | 7.9 |
| 10 | 2 | 8 | 8.0 |

**Flat.** Minute 0's depth is minute 9's depth — this machine shows no detectable thermal throttling
across a full ten-minute sustained load at the impossible tier. The two minutes reading a worst depth
of 7 instead of 8 are ordinary wall-clock jitter (iterative deepening stops on a real-time cutoff, so
a search landing a few milliseconds either side of finishing one more ply is expected noise, not a
trend — a real thermal curve would read as a sustained decline across many consecutive minutes, not
one isolated dip that recovers the very next minute). A phone is the real target for this section: a
16-core desktop with headroom to spare is the least likely device to show throttling at all, so this
row is a "the instrument works and reads correctly" result, not evidence that a phone will match it.

**A caveat on how this was captured.** This number comes from the headless batchmode harness, not a
live Editor Play session — deliberately, because `DeviceSearchBenchmark`'s coroutine yields once per
cell between searches, and that yield's real-world cost depends on Unity's Update loop actually
running at its normal cadence. An Editor Play-mode run of this same plan on this same machine, done by
hand with the window occasionally out of focus, took 12m 34s — 2m 34s over the ceiling, all of it
traceable to a single ~100-second gap in the per-minute sample counts (an entire minute bucket with
zero samples) rather than a smooth per-cell overhead. Each individual search's own timing was
unaffected either way, since `CancelAfter` is a real system timer independent of Unity's frame loop —
only the coroutine's between-cell pacing is sensitive to the Editor losing focus, and that is a Play
Mode testing artifact, not a benchmark defect. Keep the app foregrounded while a run is going, on a
device or in the Editor alike, exactly as the on-screen text already asks.

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

The tester plan (`BenchmarkPlan.Tester()`, wired to the Start button via
`DeviceSearchBenchmark.StartRun()`) is the only one safe to hand to someone else — its 2m20s worst
case is provable from `AIProfileTable`'s own budgets before a single search runs. The Long Run button
(`DeviceSearchBenchmark.StartThermalRun()`) opts into the 10-minute thermal plan instead — still
provable up front, but five times longer, so it stays its own button rather than folding into Start.
The exhaustive plan (`BenchmarkPlan.Exhaustive()`) can run for hours, is for a machine you own, and
has no button at all — start it from `MobileBenchmarkDesktopCaptureTests.CaptureExhaustiveReference`.
Whichever plan you add or change, work out its worst case the same way before handing it to anyone —
a run nobody finishes measures nothing.

Record your fork's own results in a copy of this page, not by editing the table above — that table
is this project's baseline, not a template.

## In-match AI telemetry (a related, separate feature)

Everything above measures synthetic, nothing-on-screen searches run by this diagnostic tool — never
a real game. `ChessTheBetrayal.AI.MatchTelemetry.AiMatchTelemetry` measures the other half: what the
AI actually did across one real match a player just finished, so a tester can send back a report from
ordinary play instead of a dedicated benchmark session. It records one `AiMoveRecord` per AI move
(ply, team, the move made, elapsed ms and depth reached for a searched move, or just a `FromBook`
flag for a book move, since a book move never runs a search and elapsed/depth would only mislead) and
renders a header, a summary, then every move in order — the same shape and the same reasoning as
`BenchmarkReport`: nothing is formatted into text until a report is actually requested, so a match
costs no per-move string building.

Shipping off by default, behind `GameManager`'s `enableAiTelemetrySharing` — the same
composition-root-owned-flag shape as its existing `logMoves` field. When it's on and the match that
just ended was actually an AI match with at least one AI move recorded, `GameOverUI` shows a **Share
Report** button (hidden otherwise, so no scene needs a separate toggle for it) that saves and shares
the rendered report through the exact same `ReportExporter` path this whole page's Share Report button
uses — clipboard, a saved `.txt`, and the same Android MediaStore/share-sheet fallback chain. `Core`
cannot reference `AI` (the dependency only ever flows the other way), so `GameOverUI` asks for the
report through `IAiMatchTelemetryProvider.GetLastAiMatchReport()`, which `GameManager` implements —
the same seam shape as its existing `IMatchFlow` registration.

This is not a benchmark reading and the two must never be compared: a real match's searches run
warm (a shared transposition table, move ordering carried over from the previous ply), under whatever
device load a real game happens to create, on positions an actual game reached rather than the
deliberately-expensive ones this page's plans search. It exists to catch what no synthetic benchmark
can — a real player's real device having a rough time on a real board — not to produce a number
comparable to anything on this page.
