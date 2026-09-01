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
3. **Run the quick half of the suite while you work, and all of it before you open the pull
   request.** Window → General → Test Runner, and see "Running the tests" below — thirteen hundred
   tests finish in well under a minute, and the hundred or so that play real chess take about ten
   more.
4. **Update the docs if you changed what they describe.** `Docs/` says how things work now, in
   present tense. If your change makes one of those documents wrong, fix it in the same branch. A
   document that lags the code is worse than no document, because people believe it.
5. **Open a pull request against `main`.** The template will ask you what changed, why, and how you
   tested it. Filling it in properly is most of what makes a change easy to accept. A short set of
   checks runs on it automatically — see "What runs automatically" below.
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

## Running the tests

Open Window → General → Test Runner and switch to EditMode. The category dropdown in its toolbar is
what decides how long you wait:

- **Uncategorized** — everything that decides something in memory. About thirteen hundred tests, and
  they finish in well under a minute. This is the one to run while you are working.
- **Slow** — the hundred or so that play real chess or run a real search against a real clock. Around
  ten minutes. Run these before opening a pull request, because they are the only things that catch a
  difficulty tier playing worse than the one below it, or two search threads corrupting each other.
- **OnDemand** — recording harnesses and long measurement runs. These never start on their own: they
  are all marked `[Explicit]`, so Run All skips them and only naming them starts one. Some are
  allowed to run for hours. Do not pick this one unless you meant to.

Leaving the dropdown alone and pressing Run All runs the first two and skips the third, which is the
right thing before a pull request.

From the command line it is the same three names:

    Unity.exe -runTests -batchmode -projectPath <project> -testPlatform EditMode               -testCategory "Uncategorized" -testResults results.xml -logFile run.log -nographics

A new test needs no category. Add `[Category(TestCategories.Slow)]` only if it plays games or waits
on a real clock — and never to something already `[Explicit]`, which would start it.

## Editor tests do not prove a player build compiles

`UNITY_EDITOR` is always defined in the editor, so code behind `#if UNITY_EDITOR ||
DEVELOPMENT_BUILD` compiles and passes its tests even where a release build rejects it. `dotnet
build` on the generated csproj has the same blind spot, because Unity writes those with the
editor's own symbols. This has broken an Android build before.

If you change anything near one of those guards, compile the assembly without them. Copy its
generated `.csproj`, delete `UNITY_EDITOR` and `DEVELOPMENT_BUILD` from `<DefineConstants>`, and
build the copy:

    dotnet build rel.csproj -v quiet --nologo

Around twenty seconds an assembly, and `*.csproj` is generated and gitignored so the copy is
disposable. Put the offending line back once and check the build goes red before you trust a clean
run.

## What runs automatically

Opening a pull request starts one job, and it is not the test suite. It checks that:

- every asset has the `.meta` file Unity identifies it by, and no `.meta` is left describing a
  folder that a fresh clone would not get;
- every assembly definition is still valid JSON;
- nothing was saved with a byte-order mark or through the wrong text encoding;
- comments follow the rule above — they explain rather than advertise, and they do not point at
  documents an outside reader cannot open;
- links between the documents still lead somewhere.

Run exactly the same thing before you push, and you will never be surprised by it:

    python .github/checks/repo_checks.py

**The suite itself is not part of that job, and running it is still your job.** Unity needs a
licence to start, GitHub will not hand a licence to a pull request opened from a fork, and a check
that silently skips itself for outside contributors is worse than one that was never there. So the
tests stay where you can see them fail: on your machine, before you push.

## Tests

A green test proves nothing until you have seen it fail. After writing one, break the line it is
meant to protect and confirm that test — and ideally only that test — goes red, then put the line
back.

Be especially careful with assertions about timing. A threshold that was tight when it was written
can quietly become impossible to fail as the code gets faster, and a test that cannot fail is not a
test. Ask of any timing assertion: can this still go red?
