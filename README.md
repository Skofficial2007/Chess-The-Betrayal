# Chess: The Betrayal

[![Checks](https://github.com/Skofficial2007/Chess-The-Betrayal/actions/workflows/checks.yml/badge.svg)](https://github.com/Skofficial2007/Chess-The-Betrayal/actions/workflows/checks.yml)

Chess, with one extra rule: once a game, somebody gets to take their own piece.

Everything else is ordinary chess. Castling, en passant, promotion, the fifty-move rule and
threefold repetition all work the way you expect.

## There is one Betrayal in the game, and you are both sharing it

Not one each. One, for the whole match, between the two of you. Whoever spends it first has taken
it, and the other player never gets one.

So it hangs over everything. Use it and you have nothing left to threaten with. Sit on it and they
may go first.

## What it looks like

You are White. Your queen on d1 has mate on h5 — except your own pawn is sitting on h5.

So take the pawn. Your knight on g3 captures it.

That is the Act, and your knight now standing on h5 is the Betrayer. Your turn does not end. It
comes straight back to you, because a piece that turned on your own side is your problem to answer
for, not your opponent's.

What you owe now is the Retribution: kill your own Betrayer using another of your pieces. Which is
the entire trick, because the piece you kill it with is the queen. Qd1xh5. The knight is gone, the
queen is standing on the square you wanted from the start, and Black is mated.

The Act clears your own square. The Retribution puts the piece you actually wanted on it. You spent
two of your own men, the pawn and the knight, to move one queen one square — and if it is mate, that
is a bargain.

Clearing a square is one idea. Opening a line, breaking your own pawn chain on purpose, pulling a
defender out of the way: it is a tool, and what you build with it is yours to work out.

## Or you don't pay

You can refuse the Retribution even when you are perfectly able to make it. Sometimes you cannot
make it at all, because the only piece that could reach the Betrayer is pinned to your king — and
then you are not asked, it simply happens.

Either way the Betrayer defects. It changes colour where it stands and joins the other army. Your
knight on h5 is now their knight on h5, and it is their move: run it, take something with it, or
leave it sitting there.

That is the risk that makes the Act worth thinking about for more than a second. Get the Retribution
wrong and you have handed your opponent a live piece, on a square you picked for them, right next to
whatever you were trying to protect.

If the piece checks your own king the moment it changes sides, you get one forced move to deal with
that before the turn passes.

## The rest of the rules

Your king can never be the Betrayer and can never be the victim. Any other piece can be either. Your
king is allowed to carry out the Retribution, provided that capture is legal for a king. Castling
cannot be used to do it.

An Act cannot expose your own king, the same as any other move. And if the Act uncovers a check
against your opponent, that check waits: nothing is evaluated until the whole sequence has resolved,
so you cannot win a game in the middle of your own Betrayal.

A pawn Act onto the last rank does not promote, and stays a pawn. A pawn that captures the Betrayer
on the last rank does promote, with all four choices. Promotion is the reward for fighting your way
across the board, not for turning on your own side — and hunting down a traitor still counts as
fighting.

Because the whole thing is one-shot and cannot be taken back, the game asks you to confirm an Act
before it commits.

## Clocks

Six time controls, from Bullet 1|0 up to Rapid 15|10, and an untimed mode.

In a timed game a successful Retribution pays a bonus onto your clock, scaled to the control you are
playing — three seconds at Bullet 1|0 up to thirty at Rapid 15|10. A defection pays nothing. The
faster the game, the more the mechanic doubles as a lifeline.

Games against the AI are untimed, so there is no bounty in those.

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
one turn instead of two ordinary moves. Six difficulty tiers share that one engine and differ by
dials: depth ceilings run from three plies up to nine, alongside how long it may think, how often it
throws a move away on purpose, and how much it likes the idea of betraying its own pieces. Only two
of the six have any appetite for that at all.

There is an opening book, kept for variety and not for strength. It was measured over 640 games and
made no difference to the result, which is written down in the docs instead of quietly forgotten.

## Tests

About fourteen hundred, and they run without the art or a scene, because almost nothing here needs
an engine to be tested. Window → General → Test Runner → EditMode. Thirteen hundred of them finish in
under a minute; the hundred or so that play real chess take about ten more, and
[CONTRIBUTING.md](CONTRIBUTING.md) explains when to run which.

Pull requests also get a short set of checks that need no Unity licence, so they run on a fork's
contribution the same as anyone's. They do not run the suite, and CONTRIBUTING says why.

## Contributing

[CONTRIBUTING.md](CONTRIBUTING.md) covers how to get a change in and what the code is expected to
look like. [Docs/](Docs/) describes how the larger pieces work today, including what has been
verified and what has not, which is usually the more useful half.

Issues and pull requests are welcome. If you are planning something large, open an issue first.

## Licence

MIT, see [LICENSE](LICENSE). That covers the code in this repository. The Asset Store packages you
import during setup are covered by their own licences and are not redistributed here.
