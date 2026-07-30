# Writes dist/latest.json - the manifest the in-app updater reads.
#
#   python tools/publish-manifest.py            # version from <Version> in Lumenotepad.csproj
#   python tools/publish-manifest.py 1.2.1      # explicit override
#
# Run AFTER the platform packagers, since it hashes whatever zips they produced:
#   tools/publish-macos.sh
#   tools/publish-windows.sh
#   python tools/publish-manifest.py
#
# One manifest covers every platform. UpdateService.PlatformKey picks its own entry ("macos-arm64",
# "macos-x64", "win-x64"), so a single file uploaded to a single release serves both operating systems and
# nobody can be handed a build for the wrong one.
#
# Missing builds are SKIPPED WITH A WARNING rather than failing: publishing a macOS-only release is a
# legitimate thing to do. An absent key simply means the updater finds nothing for that platform, which is
# the same as being up to date.
import hashlib
import json
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
CSPROJ = os.path.join(ROOT, "src", "Lumenotepad", "Lumenotepad.csproj")
DIST = os.path.join(ROOT, "dist")


def csproj_version() -> str:
    m = re.search(r"<Version>([^<]+)</Version>", open(CSPROJ, encoding="utf-8").read())
    if not m:
        sys.exit("no <Version> in Lumenotepad.csproj")
    return m.group(1).strip()


VERSION = sys.argv[1] if len(sys.argv) > 1 else csproj_version()
RELEASE_BASE = os.environ.get(
    "LUMENOTEPAD_RELEASE_BASE",
    f"https://github.com/unl0cks/Lumenotepad/releases/download/v{VERSION}")

# manifest key -> zip filename
BUILDS = {
    "macos-arm64": f"Lumenotepad-macOS-{VERSION}-arm64.zip",
    "macos-x64": f"Lumenotepad-macOS-{VERSION}-x64.zip",
    "win-x64": f"Lumenotepad-{VERSION}-win-x64-portable.zip",
}


def sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> None:
    notes_path = os.path.join(ROOT, "docs", f"release-notes-{VERSION}.md")
    # The manifest's "notes" is the ONE line the updater window shows before downloading. Read it from an
    # explicit marker rather than guessing at the prose: a "first real sentence" heuristic happily picked
    # the tail of a wrapped line and put half a sentence in the dialog.
    notes = ""
    if os.path.isfile(notes_path):
        m = re.search(r"<!--\s*summary:(.*?)-->", open(notes_path, encoding="utf-8").read(), re.S)
        if m:
            notes = " ".join(m.group(1).split())
        else:
            print(f"  WARNING: no '<!-- summary: ... -->' in {os.path.basename(notes_path)} "
                  f"- the updater will show no description")

    builds, missing = {}, []
    for key, name in BUILDS.items():
        path = os.path.join(DIST, name)
        if not os.path.isfile(path):
            missing.append(name)
            continue
        builds[key] = {
            "url": f"{RELEASE_BASE}/{name}",
            "sha256": sha256(path),
            "size": os.path.getsize(path),
        }
        print(f"  {key:12} {os.path.getsize(path) / 1024 / 1024:6.1f} MB  {name}")

    if not builds:
        sys.exit("no build zips found in dist/ - run the publish scripts first")
    for name in missing:
        print(f"  WARNING: no {name} - that platform will have no update available")

    out = os.path.join(DIST, "latest.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"version": VERSION, "notes": notes, "builds": builds}, f, indent=2)
    print(f"wrote {out}")
    print(f"Attach latest.json AND every zip above to release v{VERSION}, or the updater will 404.")


if __name__ == "__main__":
    main()
