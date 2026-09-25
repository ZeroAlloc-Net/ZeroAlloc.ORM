#!/usr/bin/env python3
"""Mark everything in the Unshipped release-tracking files as shipped in a release.

Usage: python3 scripts/ship-release-tracking.py <version>
       python3 scripts/ship-release-tracking.py --check

Two sets of files track what a release contains, and both are only useful when
the Unshipped half is moved into the Shipped half as each version goes out:

* src/ZeroAlloc.ORM.Generator/AnalyzerReleases.{Shipped,Unshipped}.md
  The Roslyn release-tracking analyzers read these. Once a rule is in a
  "## Release x.y.z" section, changing its severity or category, or removing
  it, has to be declared, so an accidental change to a shipped rule fails the build.
* src/*/PublicAPI.{Shipped,Unshipped}.txt
  The public-API analyzers read these. Entries in Shipped are the API a
  release has promised; *REMOVED* entries in Unshipped delete their Shipped line.

The release-please workflow runs this on the release PR branch with the version
that PR releases, so the release commit carries the move. The script is
idempotent: with nothing unshipped it changes no file.

--check changes nothing. It lists every Unshipped entry and exits 1 if there is
any. CI runs it on release PRs, so a release cannot merge with entries unmoved.
"""

from __future__ import annotations

import glob
import os
import re
import sys

ANALYZER_DIR = os.path.join("src", "ZeroAlloc.ORM.Generator")
ANALYZER_HEADER = (
    "; {kind} analyzer release{s}.\n"
    "; https://github.com/dotnet/roslyn-analyzers/blob/main/src/"
    "Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md\n"
)
RULE_ROW = re.compile(r"^[A-Z]+\d+\s*\|")
REMOVED_PREFIX = "*REMOVED*"


def read(path: str) -> str:
    with open(path, encoding="utf-8") as f:
        return f.read().replace("\r\n", "\n")


def write(path: str, text: str) -> None:
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def ship_analyzer_rules(version: str) -> bool:
    shipped_path = os.path.join(ANALYZER_DIR, "AnalyzerReleases.Shipped.md")
    unshipped_path = os.path.join(ANALYZER_DIR, "AnalyzerReleases.Unshipped.md")
    unshipped = read(unshipped_path)
    if not any(RULE_ROW.match(line) for line in unshipped.splitlines()):
        return False

    shipped = read(shipped_path)
    if re.search(rf"^## Release {re.escape(version)}\s*$", shipped, re.MULTILINE):
        sys.exit(f"{shipped_path} already has a Release {version} section while "
                 f"{unshipped_path} still lists rules; resolve by hand.")

    # Keep the section headings and tables; drop the leading ';' comment lines.
    body = "\n".join(
        line for line in unshipped.splitlines() if not line.startswith(";")
    ).strip("\n")
    write(shipped_path, shipped.rstrip("\n") + f"\n\n## Release {version}\n\n{body}\n")
    write(unshipped_path, ANALYZER_HEADER.format(kind="Unshipped", s=""))
    return True


def api_entries(text: str) -> list[str]:
    return [line for line in text.splitlines() if line.strip() and not line.startswith("#")]


def ship_public_api(unshipped_path: str) -> bool:
    shipped_path = unshipped_path.replace("PublicAPI.Unshipped.txt", "PublicAPI.Shipped.txt")
    unshipped_text = read(unshipped_path)
    pending = api_entries(unshipped_text)
    if not pending:
        return False

    shipped_text = read(shipped_path)
    directives = [line for line in shipped_text.splitlines() if line.startswith("#")]
    entries = set(api_entries(shipped_text))
    for entry in pending:
        if entry.startswith(REMOVED_PREFIX):
            removed = entry[len(REMOVED_PREFIX):]
            if removed not in entries:
                sys.exit(f"{unshipped_path}: {entry!r} is not in {shipped_path}.")
            entries.discard(removed)
        else:
            entries.add(entry)

    # Same order the files already use and the analyzer's code fix produces.
    ordered = sorted(entries, key=lambda s: (s.lower(), s))
    write(shipped_path, "\n".join(directives + ordered) + "\n")
    unshipped_directives = [line for line in unshipped_text.splitlines() if line.startswith("#")]
    write(unshipped_path, "\n".join(unshipped_directives) + "\n")
    return True


def ship_all_public_apis() -> list[str]:
    return [p for p in sorted(glob.glob(os.path.join("src", "*", "PublicAPI.Unshipped.txt")))
            if ship_public_api(p)]


def unshipped_entries() -> list[str]:
    found = []
    path = os.path.join(ANALYZER_DIR, "AnalyzerReleases.Unshipped.md")
    found += [f"{path}: {line}" for line in read(path).splitlines() if RULE_ROW.match(line)]
    for path in sorted(glob.glob(os.path.join("src", "*", "PublicAPI.Unshipped.txt"))):
        found += [f"{path}: {line}" for line in api_entries(read(path))]
    return found


def main() -> None:
    if sys.argv[1:] == ["--check"]:
        found = unshipped_entries()
        if found:
            print("Unshipped entries remain on a release PR; run "
                  "scripts/ship-release-tracking.py <version> on the branch:")
            print("\n".join(found))
            sys.exit(1)
        print("nothing unshipped")
        return
    if len(sys.argv) != 2 or not re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?", sys.argv[1]):
        sys.exit("usage: ship-release-tracking.py <version>, for example 2.0.1")
    version = sys.argv[1]
    changed = []
    if ship_analyzer_rules(version):
        changed.append(f"analyzer rules -> Release {version}")
    changed += ship_all_public_apis()
    print("\n".join(changed) if changed else "nothing unshipped")


if __name__ == "__main__":
    main()
