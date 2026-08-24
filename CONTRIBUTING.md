# Contributing

Contributions are welcome. This page covers how to get a change in and what the code is expected to
look like when it arrives.

Start with [SETUP.md](SETUP.md) — the repository does not carry its Asset Store art, so a fresh
clone needs a few minutes of setup before it will run. [Docs/](Docs/) explains how the larger pieces
work; if you are about to change the AI, read `Docs/AI/search.md` first.

## Getting a change in

1. **Fork the repository** and create a branch off `main`. Name it after what it does, not after
   yourself: `fix/undo-during-search`, `feat/endgame-tablebase`.
2. **Make the change, and add a test for it.** Nearly everything here is testable without opening a
   scene, and that is deliberate — see below.
3. **Run the EditMode suite** (Window → General → Test Runner) and make sure it is green.
4. **Update the docs if you changed what they describe.** `Docs/` says how things work now, in
   present tense. If your change makes one of those documents wrong, fix it in the same branch. A
   document that lags the code is worse than no document, because people believe it.
5. **Open a pull request against `main`.** The template will ask you what changed, why, and how you
   tested it. Filling it in properly is most of what makes a change easy to accept.
6. A maintainer reviews it. Nobody can push to `main` directly, so every change — including the
   maintainer's own — arrives through a pull request.

Small, focused pull requests get reviewed quickly. A branch that fixes one bug is easy to say yes
to; a branch that fixes one bug and reformats four files is not.

If you are planning something large, open an issue first and describe the approach. It is much
cheaper to disagree about a design in an issue than in a finished branch.

## Commit messages

Conventional style, imperative mood, lower case:

    feat(ai): add null move pruning
    fix(view): stop a rattle before recording where a piece stands
    test(core): cover the ordinary move the executor answers for

The subject says what changed. The body says **why it was worth changing** — the bug found, the
constraint discovered, the thing that would otherwise go wrong. If a change claims to be faster,
put the measured numbers in the body.

## What the code should look like

- **Keep concerns where they belong.** Chess rules live in `Core`, AI in `AI`, presentation in `UI`
  and `View`, platform work in `Infrastructure`. `Core` cannot reference Unity at all — the compiler
  enforces it — which is what lets the rules and the search be tested without an engine.
- **One reason to change per class.** A class that drives a process, reads a platform API and draws
  a screen is three classes.
- **Nothing in the search may allocate.** `AlphaBetaSearch`, `BetrayalAwareEvaluator` and the
  transposition table run millions of times per move. Everywhere else — tools, reports, UI — you can
  allocate freely.
- **Depend on the seam, not the implementation.** Optional collaborators are constructor parameters
  that default to null, not singletons reached from inside.
- **Testability is a design constraint.** If something cannot be tested without opening a scene,
  that is usually worth fixing rather than worth skipping the test for.

## Comments

Comments explain **why**, in plain English, for someone who was not there. They do not restate what
the next line obviously does, and they do not advertise.

Write "captures worth less than a pawn rarely help once you are this far behind", not "zero
allocation, high performance". If a comment only makes sense to someone who read a planning document
they cannot open, rewrite it so it stands on its own.

## Tests

A green test proves nothing until you have seen it fail. After writing one, break the line it is
meant to protect and confirm that test — and ideally only that test — goes red, then put the line
back.

Be especially careful with assertions about timing. A threshold that was tight when it was written
can quietly become impossible to fail as the code gets faster, and a test that cannot fail is not a
test. Ask of any timing assertion: can this still go red?
