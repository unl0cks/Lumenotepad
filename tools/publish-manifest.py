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
REPO = os.environ.get("LUMENOTEPAD_REPO", "unl0cks/Lumenotepad")

WIN_TAG = os.environ.get("LUMENOTEPAD_WIN_TAG", f"v{VERSION}-win-beta")
BUILDS = {
    "macos-arm64": (f"Lumenotepad-macOS-{VERSION}-arm64.zip", f"v{VERSION}"),
    "macos-x64": (f"Lumenotepad-macOS-{VERSION}-x64.zip", f"v{VERSION}"),
    "win-x64": (f"Lumenotepad-{VERSION}-win-x64-portable.zip", WIN_TAG),
}

def asset_url(tag: str, name: str) -> str:
    base = os.environ.get("LUMENOTEPAD_RELEASE_BASE")
    return f"{base}/{name}" if base else f"https://github.com/{REPO}/releases/download/{tag}/{name}"

def sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def main() -> None:
    notes_path = os.path.join(ROOT, "docs", f"release-notes-{VERSION}.md")
    notes = ""
    if os.path.isfile(notes_path):
        m = re.search(r"<!--\s*summary:(.*?)-->", open(notes_path, encoding="utf-8").read(), re.S)
        if m:
            notes = " ".join(m.group(1).split())
        else:
            print(f"  WARNING: no '<!-- summary: ... -->' in {os.path.basename(notes_path)} "
                  f"- the updater will show no description")

    builds, missing = {}, []
    for key, (name, tag) in BUILDS.items():
        path = os.path.join(DIST, name)
        if not os.path.isfile(path):
            missing.append(name)
            continue
        builds[key] = {
            "url": asset_url(tag, name),
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
