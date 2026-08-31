# Mobile / on-device search benchmark

Everything in `Docs/Benchmarks/baseline.md` was measured on one 16-core desktop. The game ships to
Android across a wide device range, and the 3-second per-move budget is what protects a player on
the slowest phone that still runs the game — not the desktop mean. This page is the record of what
the on-device instrument (`Assets/_Scripts/AI/DeviceBenchmark/`) actually measures, what it
deliberately does not, and the numbers gathered so far.

## How long it takes, and why that number is trustworthy

**The tester run's searching is bounded at 2m 20s on any device**, and the app shows that bound on
screen before it starts.

That bound is exact rather than optimistic, because each search is stopped by a wall-clock timer
rather than by finishing its work. A slower phone does not spend longer on a search — it reaches a
shallower depth inside the same milliseconds. Adding up every search's own hard budget therefore
gives a ceiling that holds on the fastest desktop and the slowest phone alike. A weak device trends
toward that ceiling, because fewer of its searches finish early enough to stop on their own, but its
searching cannot go past it.

**What that ceiling does not cover is the gaps between searches**, and it is worth being exact about
this because both plans have now been seen to finish late. A run yields a frame between cells so
results appear as they arrive, and a frame costs whatever that device's Update loop costs at the
time — so real wall clock is the ceiling plus a per-cell overhead that says nothing about the search
and everything about what else is on screen. A phone drawing the live report measured 75–100 ms a
cell across two devices: a few seconds onto the 54-cell tester run, fifteen to twenty onto the
200-cell thermal one. In the
Editor it is far larger and depends on window focus — a hand-driven Play-mode tester run took closer
to three minutes — which is worth knowing before reading one as a slow device. Quote the ceiling for
what it bounds, which is the searching, because that is the part that measures the phone.

Measured on a desktop (i5-13500HX), in batchmode via `MobileBenchmarkDesktopCaptureTests` (net of
Unity's own startup, since that harness's clock starts once the plan itself begins): **2m 00.6s**,
against a provable 2m 20s ceiling — almost no slack left, because four of the six tiers are pinned at
their own 3-second budget by construction. A phone will land nearer the 2m 20s ceiling.

**The exhaustive run is a different thing entirely and must never be handed to a tester.** Every
position, every repeat, play-forward included, on both thread contexts is roughly 3,960 searches and
a worst case near **three hours** — an early attempt at it was still running, unfinished, after 71
minutes on the desktop above. It exists for a machine you own and can leave alone.

**The thermal run is a third thing again, opt-in and roughly five times longer than the tester run —
see "Sustained-load (thermal) run" below.**

## Running it, and getting the report back

The app opens on a **Quick Run** button and does nothing until it is pressed. That is deliberate: a
run that began on scene load would be timing a phone still settling from launch, and could not be
repeated without relaunching. Pressing it again after a run finishes starts a genuinely fresh one —
new runner, new report, new run id — so two readings from the same phone are never blended. A second
button, **Long Run**, starts the sustained-load (thermal) plan instead — see below.

The other half of that guarantee is that starting a run discards the previous one's report outright.
There is only ever one report and no history behind it, so **share a finished run before starting
another**. The first device to run both plans back to back lost its tester numbers exactly that way,
and only found out when the file arrived with a single tier in it. Starting a run while a finished
report has not been shared now costs two presses: the first replaces the report on screen with a
warning naming what is about to be lost, and the second goes ahead. Sharing takes the warning back
down — unless the write itself failed, in which case the next start is warned about all over again,
because nothing actually reached disk.

Results appear as they arrive. The screen keeps the header, the status line and the per-tier summary
complete at all times, but shows only the newest stretch of the scrolling detail log and says on the
page how many lines it is not showing. That cap exists because TextMeshPro stops drawing a text
object past 16,383 characters without warning, and real reports off a device have measured 16,915
characters for a tester run and 41,174 for the sustained one — both past the point where an
uncapped log would quietly start losing the newest lines, which are the ones being
watched. A saved copy has everything.

**Share Report** stays greyed out and unpressable until a run completes. Pressing it always does two
things first, on every platform: copies the whole report to the clipboard, and writes it, unstyled
and untrimmed, as a `.txt` under `Application.persistentDataPath`, named for the device and the
moment it ran (`chess-ai-benchmark_<device>_<yyyyMMdd-HHmmss>.txt`) so a folder full of reports from
several testers stays attributable and nothing overwrites anything. The full path is appended to the
on-screen log and to the player log — that pair is the safety net nothing else here depends on.

The report stays inside plain ASCII, and every written copy also carries a UTF-8 byte-order mark.
Both come from the same bug arriving twice. The first report back from a device had every em-dash
rendered as mojibake, because a plain UTF-8 file with nothing identifying it is read as the local
ANSI codepage by enough Windows text viewers to matter. The mark was added for that and did not
fix it — a later report, shared from a build that carried the mark, came back corrupted the same
way, so something between the phone and a reader was decoding without looking for one.

A mark only helps a reader that checks for it; ASCII needs no reader to do anything. So the report
gave up its em-dashes, and that is the rule to keep if you add report text or another export
route. Three tests assert it, one for each thing that produces report text, and they compare code
points rather than chars — the first version of that check was built on NUnit's `LessThan` over
chars and passed with an em-dash still in the string. The mark stays because it costs nothing and
helps where it is honoured, so keep encoding through `ReportExporter.ReportEncoding` rather than
`Encoding.UTF8`; it is simply not what makes the file safe to forward.

The report names the plan that produced it, on the status line, and describes its shape as a header
line — how many positions, which tiers, how many repeats. A cell count cannot separate breadth from
repetition, and "200/200 cells" reads like a matrix when it was one position searched 200 times.

On Android, what happens next depends on the API level. Android 10 (API 29) and newer write a second
copy straight into the phone's public Downloads folder through MediaStore and raise a share sheet
with that file attached, so the tester can open it from a chat app, mail or a file manager without
ever leaving the share sheet. Android 8-9 (API 26-28), or any device where the Downloads write or the
attached share fails for any reason, fall back to the same text-only share sheet this always used —
no file attached, just the report as the message body — which needs no storage permission and no
manifest entry.

Which layer fired is written to the on-screen log and the player log, but not into the report itself,
and it cannot be: the text has to be finished before it can be written, and which layer succeeded is
only known afterwards. Whoever receives it can tell anyway, without being told — a `.txt` arriving as
an attachment is the Downloads layer, and the report pasted into a message body is the text
fallback. What the on-screen note adds is telling the *tester* which one they just used.

### What a report deliberately does not say

Hardware, OS and build facts only — plus which build wrote it, which is the one thing that makes a
report worth keeping: the app version, and the id Unity regenerates for every build, since two
packages a week apart usually share a version. Neither is read off the device or its owner. The same
sentence is the first line in the log at startup, so a logcat with no header is attributable too.

No serial number, no install or advertising id, no owner-chosen
device name. These files are written to be sent onward, often through a chat app to someone the
tester has never met, and anything identifying in one travels with every copy of it from then on. A
timing number needs to know which chip it ran on and never whose phone that was. Two reports from the
same model stay tellable apart by their run id and the timestamp in their filename. `DeviceDescription`
holds that list, and a test pins it, so adding a field is a deliberate act rather than an easy one.

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

**How long the depth took, not just which depth.** Every deep tier is pinned at its budget by
construction, so elapsed time carries nothing, and depth is whole plies — a device can get a third
slower and read identically right up until it drops one. So a cell and its tier summary also report
how long the deepening loop took to reach the depth it reports. Two runs that both reach depth 7 and
took 1.2 s and 2.4 s to get there are a phone with room to spare and a phone about to lose a ply, and
this is the only column that separates them. Whatever is left of the elapsed time went on the
tie-break pass, which runs after the loop and returns when the budget's timer fires.

**Where the overshoot verdict lives, and where it does not.** A per-cell line states its margin as a
quantity — "+2ms past budget (3000ms)" over, "1080ms inside budget (1300ms)" under, and "on budget to
the millisecond" when the rounding leaves nothing to report — and passes no judgement; the judging happens in the
per-tier summary, where the worst overshoot for a whole tier is what the gate below is read against.
That split is deliberate. The timer that cancels a search has a resolution of its own, a few
milliseconds on a phone and around fifteen on Windows, so a line-by-line pass/fail word puts a
property of the clock in front of a reader as a failure of the search. It also has to be true: an
earlier version compared the overshoot exactly and then printed it rounded, so a search 0.3 ms late
announced itself as "OVER BUDGET by 0ms" — 194 of 200 lines in a real device run said exactly that,
which is enough for a reader to stop believing the six that meant something. Whatever decides is now
the same number that gets printed.

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

Read that against the resolution of the clock enforcing it, not as an absolute. The search checks for
cancellation at a node boundary and the timer behind it ticks on its own schedule, so a reading in
the tens of milliseconds is a property of the measuring apparatus and not of the search — the same
point the per-cell section above makes about where a verdict belongs. Hundreds of milliseconds is a
real finding, and it gets a profile-row fix with the failing and the corrected numbers both
recorded — never a loosened gate.

## Desktop reference

Captured with `MobileBenchmarkDesktopCaptureTests` (`[Explicit]` — run it deliberately, read the
summary from the log), the same plan a phone runs, so this row is the only valid comparison point for
a device number. Never compare a device row against `baseline.md`.

Machine: i5-13500HX, batchmode (headless, no Editor window), Mono. Captured `2026-08-03`, re-run on
`2026-08-04` with every row unchanged within noise. Worker-thread figures (the production path);
8 samples per tier.

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

Overshoot is 11–14 ms across every deep tier — not the budget being missed in any sense a player
could perceive. It is worth knowing what that figure is actually made of, because it is a property
of the measuring machine as much as of the thing measured. The search is not slow to notice it has
been cancelled: `AlphaBetaSearch` tests the token on entry to every node, so the delay one slow node
can add is microseconds. What is coarse is the timer behind `CancelAfter` — on Windows it fires on a
15.6 ms tick, which is where a tight cluster of 11–14 ms readings comes from. A platform with a
finer timer lands much closer to its budget on far slower hardware: the first real Android device
measured a worst overshoot of **+1 ms** across 200 searches. So read this column for order of
magnitude only. Tens of milliseconds is normal on either platform; hundreds is a real finding.

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
Quick Run, and send back the shared report. One row per device and binary once a full run completes
on it — the same phone can hold two rows, because the 32-bit binary searches fewer positions in the
same milliseconds and its row would otherwise read as slower hardware. Take that column from the
report's build line, never from what was built: the package carries both and the phone is what chose.

| Device | Chipset (GPU proxy) | Binary | Worst-case overshoot | Tier that overshot | Deepest tier's depth reached (worst-case) | Verdict | Notes |
|---|---|---|---:|---|---:|---|---|
| TrebleDroid GSI, Android 14 / API 34 (model not reported) | Mali-G68 MC4 [ARM], 8 cores @ 2400 MHz, 7.6 GB | 64-bit | +1 ms | impossible | 7 (impossible) | Pass | Thermal run only — the tester run was lost, see below |
| realme RMX3998, Android 16 / API 36 | Mali-G57 MC2, 8 cores @ 2200 MHz, 5.5 GB | 64-bit | +10 ms | impossible | 7 (impossible, sustained run) | Pass | First device to complete both plans — all six tiers, see below |

### The first complete six-tier device run — realme RMX3998

Release IL2CPP, ARM64. This is the first phone to run the tester plan and the sustained-load plan and
share both, so it is the first row where all six tiers have numbers rather than one. It was captured
before both of the later changes to the build config below — the package was ARM64-only and listed
Vulkan ahead of OpenGL ES 3. Neither disturbs the worker-thread figures, which is nearly all of what
follows: this phone runs the same 64-bit code out of either package, and search never touches the
GPU. The one figure worth re-checking on a re-run is the main-thread control, which shares a frame
with rendering.

**The tester plan finished 54 cells in 2m 11s** against its promised 2m 20s ceiling. The bound holds
on real hardware, which is the claim the whole "how long it takes" section above rests on.

Worker-thread pass, 8 samples per tier:

| Tier | Budget | Worst elapsed | Worst overshoot | Depth worst / mean |
|---|---:|---:|---:|---:|
| easy | 1300 ms | 0.62 s | none | 3 / 3.0 |
| normal | 2250 ms | 2.25 s | +1 ms | 5 / 5.0 |
| hard | 3000 ms | 3.00 s | +5 ms | 5 / 6.3 |
| aggressive | 3000 ms | 3.00 s | none | 6 / 6.5 |
| extreme | 3000 ms | 3.00 s | +5 ms | 6 / 7.0 |
| impossible | 3000 ms | 3.00 s | +1 ms | 5 / 6.3 |

**Reading it against the desktop.** The two shallow tiers reach their configured ceiling exactly, as
they do everywhere — the only thing their timings measure is how little they were asked to do. The
four deep tiers are budget-bound by construction, so depth is the whole of the signal, and the phone
means 6.3–7.0 against the desktop's 7.0–7.8. Roughly one ply shallower on a chip with a fraction of
the desktop's power is a good result, not a finding.

The main-thread control, one sample per tier, matched the worker pass on time throughout and reached
depth 3/5/6/6/7/6. Nothing here suggests this device's scheduler treats background work differently.

**The worst-depth column is where this run says something the means hide.** On one position — the
Italian, `e2e4 e7e5 g1f3 b8c6 f1c4` — `hard` and `impossible` reach only depth 5 while `extreme`
reaches 7, on both repeats. `extreme` and `impossible` share a `MaxDepth` of 9 and the same 3000 ms
budget and differ only in evaluator weighting, which makes that a controlled comparison: the weighted
evaluator orders this position roughly two plies better than the identity one, and the two
identity-weighted tiers settle on a weakening pawn push as a result. Repeats are not independent
samples here — the search is deterministic given identical inputs — so both agreeing means this is a
property of the position and the profile rather than wall-clock noise.

Recorded as a measurement, not a call to action. The four deep tiers sitting close together, with
`impossible` no deeper than `hard`, is a deliberate and separately recorded property of the profile
table, not something this run discovered.

**Sustained load: no throttling, at all.** 200 cold searches at the impossible tier over 10m 20s.
Depth read **7 in every one of the eleven minute buckets** — not a mean of 7 with variation under
it, but the same figure from minute 0 to minute 10. Worst overshoot across all 200 was **+10 ms on a
3000 ms budget**, and all but a handful came in at +2 ms or less. Battery went 87% to 85%.

That is the second phone in a row to hold depth flat across a full ten minutes at the heaviest tier
the game ships, and it settles the open question about asking Android for a sustained-performance
clock: it would trade peak speed for stability this device does not need. The depth a player sees on
move 5 is the depth they see on move 80.

### What the first real device showed

One phone ran the tester plan to completion and then the thermal plan straight after, without
sharing in between, so only the thermal run survived to be read — 200 samples at the impossible
tier, nothing from the other five. That is the loss described under "Running it" above, and it is
why this row's overshoot and depth columns speak for one tier rather than six. A phone that shares
after each run will fill the rest.

**Depth held perfectly flat.** Every one of the 200 searches reached depth 7, in minute 0 and in
minute 10 alike — not a mean of 7 with variation underneath it, but 7 exactly, 200 times. There is
no throttling here to measure, on a chip with none of the desktop's thermal headroom, across a
sustained ten minutes at the heaviest tier the game ships. That is the answer the whole plan exists
to produce, and it is the good one: the depth a player sees on move 5 is the depth they see on move
80. Battery went 48% to 44% over the same ten minutes.

The device sits exactly one ply below the desktop reference, which reached a mean depth of 8 on this
same position. The gap is real but it is also the whole of the gap — and it is worth noting that the
desktop's 8 was marginal, dipping to 7 in two separate minutes, while the phone's 7 never wavered
once. A tier that is comfortably short of the next ply is steadier than one sitting right on the
boundary, which is the shape a player would rather have.

**The 10-minute ceiling holds for search, not for wall clock.** The run took 10m 15s. All 200
searches together account for exactly 10m 00s of that, so the extra 15 s is entirely the coroutine's
between-cell pacing — about 75 ms a cell, against roughly 15 ms a cell on the desktop's headless
batchmode. That difference is a phone redrawing a live report at a phone's frame rate, not the phone
being slow at chess, and it scales with cell count rather than duration: the same overhead adds
around 4 s to the 54-cell tester plan. The ceiling remains provable and honest about what it bounds,
which is search work; budget for a few seconds past it on a device with a screen to draw.

## Build config this was measured under

Pinned in `ProjectSettings/ProjectSettings.asset`, not left on template defaults: IL2CPP, ARM64 and
ARMv7, IL2CPP configuration Release, code generation "faster runtime," managed stripping Low, target
API 36, min API 26, graphics APIs OpenGL ES 3 ahead of Vulkan. Never compare device numbers captured
under different settings than these — a timing difference would measure the configuration change, not
the phone.

ARMv7 is in that list because a phone refused to install an ARM64-only package outright; carrying
both lets the phone choose, which is the same reason a row has to record which one it chose. Rows
captured before it was added still stand, since a 64-bit phone runs the identical code either way.
The graphics API order is listed because it is not free of these numbers: search runs on a worker
thread and never touches the GPU, but the main-thread control cell shares a frame with rendering, so
that one figure can move when the order does.

## Using this on your own project

If you've forked this project and changed the AI or the rules, this instrument is built to be
re-run rather than rebuilt.

`DeviceBenchmark.unity` ships enabled in Build Settings alongside Game Scene, reachable from a QA
button on the main menu — hidden by default behind `GameManager`'s `enableQAButton` toggle (off),
so a normal player build never shows a way to reach it even though the scene is present in the
binary. One tester build this way covers both this page's benchmark and the in-match AI telemetry
below, rather than needing a separate dedicated benchmark build. A Back button in the scene abandons
whatever run is in progress and returns to Game Scene the same way. If your fork wants a build with
neither the QA button nor the scene present at all, remove the toggle and disable the scene in Build
Settings — the two are independent, this is simply the shape this project settled on.

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

The tester plan (`BenchmarkPlan.Tester()`, wired to the Quick Run button via
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
ordinary play instead of a dedicated benchmark session. It records one `AiMoveRecord` per ply — the
ply number, the team, the move, and for a searched move its elapsed ms, depth and stop reason — then
renders a header, a summary, and every ply in order, the same shape and the same reasoning as
`BenchmarkReport`: nothing is formatted into text until a report is actually requested, so a match
costs no per-move string building.

**Three things a reader has to know about that log, all learned from the first one that came back.**

*Only the AI's own plies are recorded*, so the ply numbers skip. The report says so on the page,
because a gap otherwise reads as something having been dropped — which is exactly the conclusion the
next point was first mistaken for.

*A Defection is recorded even though nobody plays it.* The rules produce that ply once Retribution is
refused or impossible, so it reaches no move-decided path, and the first real match log had a Black
queen move off a1 with nothing anywhere putting a queen there. It is the one ply in a match that
moves a piece between the two armies, so a log without it cannot account for the board it describes.

*A move that stopped on a forced mate is kept out of the depth spread.* A search that finds a mate
stops there whatever depth it is at, because no deeper look can change that answer. Pooling it with a
search the clock cut short let a mate found at depth 2 be reported as the worst depth of the whole
match, which reads as an AI that struggled all game when it had just done the best thing available.
The count of those is reported separately. Elapsed time still covers every searched move — a move
that arrived in 40 ms genuinely arrived in 40 ms.

The report carries the same device and build header the benchmark's does, from the same reader, and
is stamped per match so a Replay's own report is attributable too.

It opens with how the match ended, which nothing used to write down — a page of timings about a game
whose outcome the reader had to be told separately. A report shared mid-game says so rather than
leaving the line out, and a takeback that unmakes the final position drops it again, since undoing a
checkmate is allowed and the result would otherwise outlive the mate it came from.

**Its elapsed is not the benchmark's elapsed, and the two must not be read against each other.**
This clock starts when a move is asked for and stops when the search hands the move over, so it
carries the wait for the next frame as well as the search. The benchmark's wraps the search call and
nothing else.

It also stops short of the board. A move is paced against whatever animation is still playing, and
the quickest replies are the likeliest to be held back — an instant book move or a mate found in
40 ms arrives while the previous capture has half a second left to run. That wait is reported
separately, per ply where there was one and as a summary line either way, so both halves of what the
player sat through are stated and neither is mistaken for the whole. The first real match report showed a worst of 3044 ms against a 3000 ms budget while the same
phone's 200-search sustained run never went past +10 ms — that gap is the frame, not a missed
deadline. The report says as much on the page, because the two figures otherwise sit on this one and
invite the comparison. What this clock measures that no benchmark can is the delay a player actually
sat through, which is worth having precisely because it is the larger number.

**The realme's first shared match** (aggressive tier, seven recorded plies) also exercised the parts
of the log added for exactly this: two book plies, a mate found at depth 2 in 36 ms correctly kept
out of the depth spread and counted separately, and no Defection — so the Defection line is still the
one part of this feature never yet seen on a device.

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
