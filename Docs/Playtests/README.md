# AI Playtest Log

A lightweight, repeatable manual check on how an AI tier actually *feels* to
play against — the strength-ordering benchmark proves a tier wins more often
than the one below it, but it can't tell you whether losing to it felt fair
or the games all blurred together.

Run this after changing any `AIProfile` dial (or the evaluator/search
weighting behind it), before treating the change as done.

## How to run a session

1. Play 3–5 games against the tier under test, using a real build and the
   actual Practice-vs-AI settings screen — not a unit test, not the editor
   scene directly.
2. For each game, write one line per row of the table below.
3. Save the session as `YYYY-MM-DD-<tier>.md` in this folder, one file per
   session.

## What to log, per game

| Question | What a pass looks like |
|---|---|
| **Mistake plausibility** | When the AI erred, did it read as a *lapse* (hung a piece to a 1–2 move tactic, missed a fork) or as *noise* (aimless shuffling, moves with no point)? Lapse = pass. Noise means the blunder margin is too wide or the depth is too shallow for the bias dials it's carrying. |
| **Betrayal legibility** | Did the AI Act at a moment you could read as a betrayal — a clear material win, or a trap you could reconstruct afterward? An Act you can't explain even after seeing the whole game is a fail, even if it was objectively the strongest move. |
| **Variety** | Were the games distinguishable from each other — different openings, different middlegame shapes? Two near-identical games in one session usually means the tie-break window is too narrow or the opening book weights are too spiky for this tier. |
| **Pace** | Did the time-per-move match what you'd expect for this tier — fast and casual for Easy, visibly "thinking" for Impossible? |

## Session file template

```markdown
# <Tier> — YYYY-MM-DD

## Game 1
- Mistake plausibility: <lapse / noise — one sentence>
- Betrayal legibility: <N Acts, readable? — one sentence>
- Variety vs prior games: <note>
- Pace: <note>

## Game 2
...

## Overall verdict
<pass / needs a dial change, and which dial>
```
