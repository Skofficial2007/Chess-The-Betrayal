# Docs

Reference material for people changing this project. Each document describes how something works
today, in present tense, so it can be checked against the code rather than taken on trust.

This is not a changelog. Why a thing changed lives in the pull request that changed it; what it does
now lives here.

## AI

How the chess engine and its opponent work — the search and the techniques that make it fast, the
opening book, and the difficulty model. Start with `AI/search.md` if you are looking at the engine
for the first time; everything else assumes the vocabulary it defines.

Each document ends its sections with the file to read next, and carries a section on what has been
verified and what has not. That second part is the useful one. A technique with tests proving it is
correct is not the same as a technique with tests proving the engine still uses it, and the documents
say which is which.

## Playtests

Logs from manual play sessions against a difficulty tier, one file per session. The automated
benchmarks can prove a tier wins more often than the one below it; they cannot tell you whether
losing to it felt fair. That is what these are for.

## Adding to this

One directory per area, one document per subsystem. If you are documenting something new, copy the
shape of an existing document rather than inventing one — the headings are deliberately boring so a
reader can find the same thing in the same place every time.

Two rules worth stating outright. Write what is true on `main` now, never "we added X", because a
document written as history rots the moment the code moves. And end every claim you cannot prove
with the fact that you cannot prove it; a document that admits its own gaps is the one people
believe.
