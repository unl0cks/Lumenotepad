# Packs the Windows portable build from `dotnet publish` output.
#
# Input:  src/Lumenotepad/bin/Release/net10.0/win-x64/publish   (created by publish-windows.sh)
# Output: dist/Lumenotepad-<version>-win-x64-portable.zip
#
# PORTABLE, deliberately - not an installer. On Windows the app keeps all user data in a `userdata`
# folder beside its own assemblies (see AppSettings.DefaultDir), so installing into Program Files would
# put notebooks somewhere a non-admin process cannot write and saving would fail. An MSI/Inno installer
# would first require moving the Windows data directory to %APPDATA%, which is a migration for anyone
# already running a portable copy - a decision, not a packaging detail.
import os
import re
import sys
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
CSPROJ = os.path.join(ROOT, "src", "Lumenotepad", "Lumenotepad.csproj")
DIST = os.path.join(ROOT, "dist")


def csproj_version() -> str:
    m = re.search(r"<Version>([^<]+)</Version>", open(CSPROJ, encoding="utf-8").read())
    if not m:
        sys.exit("no <Version> in Lumenotepad.csproj")
    return m.group(1).strip()


VERSION = sys.argv[1] if len(sys.argv) > 1 else csproj_version()
FOLDER = f"Lumenotepad-{VERSION}-win-x64"

README = f"""Lumenotepad {VERSION} for Windows - PORTABLE BETA
================================================================

HOW TO RUN
  1. Extract this whole folder somewhere you keep programs - your Documents,
     a tools folder, a USB stick, anywhere you like. Do NOT run it from
     inside the zip.
  2. Run Lumenotepad.exe.

  Windows SmartScreen may say "Windows protected your PC" the first time,
  because this build is not code-signed. Click "More info" then
  "Run anyway". That is SmartScreen being cautious about an app it has not
  seen before, not a sign anything is wrong.

WHERE YOUR NOTES GO
  In the "userdata" folder next to Lumenotepad.exe. This build is portable:
  everything it saves stays in its own folder, nothing is written to the
  registry, and nothing is installed system-wide.

  Because of that, keep the folder somewhere WRITABLE. Do not put it in
  C:\\Program Files - Windows blocks normal programs from writing there and
  your notes would fail to save.

HOW TO UPDATE
  Preferences > About > "Check for updates". Lumenotepad downloads the new
  version, replaces its own program files, and restarts - your "userdata"
  folder is left exactly where it is.

  By hand, if you prefer: extract the new version and copy your old
  "userdata" folder into it (or extract over the top of the old folder and
  leave the existing userdata folder in place).

HOW TO UNINSTALL
  Delete the folder. That is all - there is nothing else on your system.

BETA
  This is a beta. It is the same codebase as the macOS build and gets the
  same fixes, but the Windows packaging has had less real-world mileage.
  Back up anything you would be upset to lose:
  Preferences > General > Saving has a backup folder setting.
"""


def main() -> None:
    pub = os.path.join(ROOT, "src", "Lumenotepad", "bin", "Release", "net10.0", "win-x64", "publish")
    if not os.path.isdir(pub):
        sys.exit(f"missing publish output: {pub} - run dotnet publish -r win-x64 first")
    os.makedirs(DIST, exist_ok=True)
    out = os.path.join(DIST, f"Lumenotepad-{VERSION}-win-x64-portable.zip")

    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for base, _, files in os.walk(pub):
            for name in files:
                full = os.path.join(base, name)
                rel = os.path.relpath(full, pub).replace("\\", "/")
                # Everything lives under one folder so extracting never scatters 200 files loose.
                zf.write(full, f"{FOLDER}/{rel}")
                count += 1
        zf.writestr(f"{FOLDER}/README.txt", README)
    size = os.path.getsize(out)
    print(f"  win-x64: {count} files, {size / 1024 / 1024:.1f} MB -> {os.path.basename(out)}")
    print("Now run:  python tools/publish-manifest.py   (writes the shared update manifest)")


if __name__ == "__main__":
    main()
