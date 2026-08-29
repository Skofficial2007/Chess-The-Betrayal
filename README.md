# Chess: The Betrayal

[![Checks](https://github.com/Skofficial2007/Chess-The-Betrayal/actions/workflows/checks.yml/badge.svg)](https://github.com/Skofficial2007/Chess-The-Betrayal/actions/workflows/checks.yml)

Chess, with one extra rule: you are allowed to take your own pieces.

Everything else is ordinary chess. Castling, en passant, promotion, the fifty-move rule and threefold
repetition all work the way you expect. The difference is that on your turn you may capture one of
your own men instead of one of theirs, and what happens next is the whole game.

## What the Betrayal does

Taking your own piece is an Act. It does not end your turn.

Your opponent then owes a Retribution: they have to capture the traitor. If their king is in check
they deal with that first, which is the one way out of paying. And if they cannot reach the traitor,
or the only piece that could is pinned, or they simply decline — the traitor Defects, and changes
sides. It is now theirs.

So an Act is a bribe. You give up a piece for tempo and position, and either they spend a move
killing it or they inherit a piece you chose for them.

One asymmetry is deliberate and worth knowing before you report it as a bug. A pawn that reaches the
last rank by an ordinary capture promotes. The same pawn reaching the same square by an Act does
not, and stays a pawn. Promotion is the reward for fighting your way across the board, not for
turning on your own side.

## Running it

You need Unity `6000.3.10f1`. Clone, open, and import three free Asset Store packages that this
repository is not allowed to redistribute — [SETUP.md](SETUP.md) has the links and takes about five
minutes. The editor tells you which are missing and links to each store page if you skip it.

The code, scenes and prefabs are all here. Only the art is not, which is why the repository is about
15 MB rather than 750.

## The opponent

Most of this project is the AI, and [Docs/AI/search.md](Docs/AI/search.md) is where to start reading.

It is an alpha-beta search with a transposition table, iterative deepening, and the usual pruning
and move-ordering machinery, taught to understand that an Act and its Retribution are two halves of
one turn rather than two ordinary moves. Six difficulty tiers share that one engine and differ by
dials: how deep it looks, how long it thinks, and how willing it is to take a slightly worse move
that suits its personality.

There is an opening book, kept for variety rather than strength. It was measured over 640 games and
made no difference to the result, which is written down in the docs rather than quietly forgotten.

## Tests

About fourteen hundred, and they run without the art or a scene, because almost nothing here needs
an engine to be tested. Window → General → Test Runner → EditMode. Thirteen hundred of them finish in
under a minute; the hundred or so that play real chess take about ten more, and
[CONTRIBUTING.md](CONTRIBUTING.md) explains when to run which.

Pull requests also get a short set of checks that need no Unity licence, so they run on a fork's
contribution the same as anyone's. They do not run the suite — that is deliberate, and CONTRIBUTING
says why.

## Contributing

[CONTRIBUTING.md](CONTRIBUTING.md) covers how to get a change in and what the code is expected to
look like. [Docs/](Docs/) describes how the larger pieces work today, including what has been
verified and what has not, which is usually the more useful half.

Issues and pull requests are welcome. If you are planning something large, open an issue first.

## Licence

MIT, see [LICENSE](LICENSE). That covers the code in this repository. The Asset Store packages you
import during setup are covered by their own licences and are not redistributed here.
