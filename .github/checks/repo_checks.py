#!/usr/bin/env python3
"""Repository checks that do not need Unity.

The test suite needs a licensed editor to start, and GitHub will not give a licence to a pull
request opened from a fork. So the tests cannot be what greets a contribution from outside the
project, and this runs instead. It reads what git is tracking and needs nothing but Python.

Most of what it finds is a missing .meta file, which is the usual way a Unity pull request turns
up broken.

    python .github/checks/repo_checks.py
"""

import json
import posixpath
import re
import subprocess
import sys


def tracked_files():
    out = subprocess.run(
        ["git", "ls-files"], capture_output=True, text=True, check=True).stdout
    return [line for line in out.splitlines() if line]


def read(path):
    with open(path, "rb") as handle:
        return handle.read()


# Code that arrived with a package rather than being written here. It still needs its .meta files
# like anything else in Assets, but its encoding and its comments are not ours to rewrite, and
# editing a shader nobody here authored to satisfy a house rule is a good way to break rendering
# for no gain. The same paths are marked as vendored in .gitattributes.
VENDORED = ("Assets/Plugins/", "Assets/Settings&Actions/TextMeshPro/", "Packages/")


def is_vendored(path):
    return path.startswith(VENDORED)


# ---------------------------------------------------------------------------------------------
# Unity keeps an asset's identity in the .meta file beside it. Commit the asset without its .meta
# and every reference to it breaks. Commit a .meta whose asset is not there and Unity deletes the
# .meta on the next import, so it shows up as a change in somebody else's working copy.
# ---------------------------------------------------------------------------------------------
def unity_ignores(path):
    """Unity imports neither hidden files nor backups, so it writes no .meta for them."""
    return any(part.startswith(".") or part.endswith("~") for part in path.split("/"))


def check_meta_files(files):
    tracked = set(files)
    assets = [f for f in files if f.startswith("Assets/") and not unity_ignores(f)]
    problems = []

    for path in assets:
        if path.endswith(".meta"):
            continue
        if path + ".meta" not in tracked:
            problems.append(
                "%s has no .meta -- commit it too, or Unity hands out a new identity and every "
                "reference to this asset breaks" % path)

    for path in assets:
        if not path.endswith(".meta"):
            continue
        described = path[: -len(".meta")]
        if described in tracked:
            continue
        # A folder is only really there if something inside it is tracked. Git does not carry
        # empty directories, so a .meta for one arrives on a fresh clone describing nothing.
        if any(other.startswith(described + "/") for other in tracked):
            continue
        problems.append(
            "%s describes nothing a clone would get -- delete it, or commit something inside %s"
            % (path, described))

    return problems


# ---------------------------------------------------------------------------------------------
# An assembly definition that is not valid JSON stops the whole project compiling, and the editor
# reports it in a way that does not mention which file is at fault.
# ---------------------------------------------------------------------------------------------
def check_assembly_definitions(files):
    problems = []
    for path in files:
        if not path.endswith((".asmdef", ".asmref")):
            continue
        try:
            parsed = json.loads(read(path).decode("utf-8"))
        except (ValueError, UnicodeDecodeError) as error:
            problems.append("%s is not valid JSON: %s" % (path, error))
            continue
        if path.endswith(".asmdef") and not parsed.get("name"):
            problems.append("%s has no name, so nothing can reference it" % path)
    return problems


# ---------------------------------------------------------------------------------------------
# Text that has been through the wrong encoding once cannot be read back, and it spreads: the next
# person to edit the file saves the damage along with their change. The byte-order mark is the
# other half of the same problem -- some tools write one, and it shows up as stray characters at
# the start of the first line.
# ---------------------------------------------------------------------------------------------
TEXT_SUFFIXES = (".cs", ".md", ".json", ".asmdef", ".asmref", ".txt", ".yml", ".yaml", ".py",
                 ".shader", ".hlsl", ".uss", ".uxml")

MOJIBAKE = (b"\xc3\xa2\xe2\x82\xac", b"\xc3\x83\xc2\xa9", b"\xc3\x82\xc2\xbb",
            b"\xc3\x82\xc2\xab", b"\xc3\xaf\xc2\xbb\xc2\xbf")


def check_text_encoding(files):
    problems = []
    for path in files:
        if not path.endswith(TEXT_SUFFIXES) or is_vendored(path):
            continue
        body = read(path)
        if body.startswith(b"\xef\xbb\xbf"):
            problems.append(
                "%s starts with a byte-order mark -- save it as UTF-8 without one" % path)
        for pattern in MOJIBAKE:
            if pattern in body:
                problems.append(
                    "%s holds text that has been through the wrong encoding -- retype the damaged "
                    "characters rather than saving over them" % path)
                break
    return problems


# ---------------------------------------------------------------------------------------------
# CONTRIBUTING asks for comments that explain why, in plain English, and that do not advertise.
# Only comments are read: Is.EqualTo(1) is ordinary code and has nothing to do with big-O.
# ---------------------------------------------------------------------------------------------
BANNED_IN_COMMENTS = (
    (re.compile(r"zero[- ]?(gc|allocation)", re.I), "say what the constraint actually is"),
    (re.compile(r"high[- ]performance", re.I), "say what is fast and why it has to be"),
    (re.compile(r"gc[- ]optimi[sz]ed", re.I), "say what the constraint actually is"),
    (re.compile(r"\bO\((?:1|n|log ?n|n log n|n\^?2)\)", re.I),
     "drop the notation and keep the reason"),
    (re.compile(r"pre-allocated", re.I), "say why it is allocated up front"),
    (re.compile(r"\bAI-\d+\b"), "an outside reader cannot open that"),
    (re.compile(r"this ticket|per the plan", re.I), "an outside reader cannot open that"),
    (re.compile(r"\bADR[-_ ]?\d+", re.I), "an outside reader cannot open that"),
)


def check_comment_style(files):
    problems = []
    for path in files:
        if not path.endswith(".cs") or is_vendored(path):
            continue
        in_block = False
        for number, line in enumerate(read(path).decode("utf-8", "replace").splitlines(), 1):
            stripped = line.strip()
            is_comment = (in_block or stripped.startswith("//")
                          or stripped.startswith("/*") or stripped.startswith("*"))
            if "/*" in stripped and "*/" not in stripped:
                in_block = True
            if "*/" in stripped:
                in_block = False
            if not is_comment:
                continue
            for pattern, advice in BANNED_IN_COMMENTS:
                if pattern.search(line):
                    problems.append("%s:%d %s -- %s" % (path, number, stripped[:70], advice))
    return problems


# ---------------------------------------------------------------------------------------------
# A document pointing at a file that has moved is worse than one that says nothing, because people
# believe it.
# ---------------------------------------------------------------------------------------------
MARKDOWN_LINK = re.compile(r"\[[^\]]*\]\(([^)]+)\)")


def check_document_links(files):
    tracked = set(files)
    problems = []
    for path in files:
        if not path.endswith(".md"):
            continue
        folder = posixpath.dirname(path)
        for number, line in enumerate(read(path).decode("utf-8", "replace").splitlines(), 1):
            for target in MARKDOWN_LINK.findall(line):
                if target.startswith(("http://", "https://", "#", "mailto:")):
                    continue
                target = target.split("#")[0].strip()
                if not target:
                    continue
                resolved = posixpath.normpath(posixpath.join(folder, target))
                if resolved in tracked:
                    continue
                if any(other.startswith(resolved + "/") for other in tracked):
                    continue
                problems.append("%s:%d points at %s, which is not here" % (path, number, target))
    return problems


CHECKS = (
    ("asset identity files", check_meta_files),
    ("assembly definitions", check_assembly_definitions),
    ("text encoding", check_text_encoding),
    ("comment style", check_comment_style),
    ("document links", check_document_links),
)


def main():
    files = tracked_files()
    print("Checking %d tracked files.\n" % len(files))

    failed = 0
    for name, check in CHECKS:
        problems = check(files)
        if problems:
            failed += 1
            print("FAIL  %s -- %d to fix" % (name, len(problems)))
            for problem in problems[:40]:
                print("        %s" % problem)
            if len(problems) > 40:
                print("        ... and %d more" % (len(problems) - 40))
        else:
            print("ok    %s" % name)

    if failed:
        print("\n%d of %d checks need attention." % (failed, len(CHECKS)))
        return 1

    print("\nAll %d checks passed." % len(CHECKS))
    return 0


if __name__ == "__main__":
    sys.exit(main())
