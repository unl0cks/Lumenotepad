import os
import plistlib
import re
import shutil
import struct
import subprocess
import sys
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
CSPROJ = os.path.join(ROOT, "src", "Lumenotepad", "Lumenotepad.csproj")

def csproj_version() -> str:
    m = re.search(r"<Version>([^<]+)</Version>", open(CSPROJ, encoding="utf-8").read())
    if not m:
        sys.exit("no <Version> in Lumenotepad.csproj")
    return m.group(1).strip()

VERSION = sys.argv[1] if len(sys.argv) > 1 else csproj_version()
RIDS = {"arm64": "osx-arm64", "x64": "osx-x64"}
ICONSET = os.path.join(ROOT, "assets", "macos-iconset")
DIST = os.path.join(ROOT, "dist")

def icns_rle(data: bytes) -> bytes:
    out = bytearray()
    i, n = 0, len(data)
    while i < n:
        run = 1
        while i + run < n and run < 130 and data[i + run] == data[i]:
            run += 1
        if run >= 3:
            out.append(0x80 + run - 3)
            out.append(data[i])
            i += run
        else:
            j = i
            while j < n and j - i < 128:
                if j + 2 < n and data[j] == data[j + 1] == data[j + 2]:
                    break
                j += 1
            out.append(j - i - 1)
            out.extend(data[i:j])
            i = j
    return bytes(out)

def argb_chunk(size: int) -> bytes:
    with open(os.path.join(ICONSET, f"icon_{size}.rgba"), "rb") as f:
        rgba = f.read()
    if len(rgba) != size * size * 4:
        sys.exit(f"icon_{size}.rgba has {len(rgba)} bytes, expected {size * size * 4} - re-run tools/icongen")
    planes = [rgba[c::4] for c in (3, 0, 1, 2)]
    return b"ARGB" + b"".join(icns_rle(p) for p in planes)

def build_icns() -> bytes:
    chunks = []
    for typ, size in [(b"ic04", 16), (b"ic05", 32)]:
        data = argb_chunk(size)
        chunks.append(typ + struct.pack(">I", len(data) + 8) + data)
    for typ, size in [(b"ic07", 128), (b"ic08", 256), (b"ic09", 512), (b"ic10", 1024),
                      (b"ic11", 32), (b"ic12", 64), (b"ic13", 256), (b"ic14", 512)]:
        with open(os.path.join(ICONSET, f"icon_{size}.png"), "rb") as f:
            data = f.read()
        chunks.append(typ + struct.pack(">I", len(data) + 8) + data)
    body = b"".join(chunks)
    return b"icns" + struct.pack(">I", len(body) + 8) + body

def info_plist() -> bytes:
    return plistlib.dumps({
        "CFBundleName": "Lumenotepad",
        "CFBundleDisplayName": "Lumenotepad",
        "CFBundleIdentifier": "com.lumen.lumenotepad",
        "CFBundleExecutable": "Lumenotepad",
        "CFBundleIconFile": "lumenotepad",
        "CFBundlePackageType": "APPL",
        "CFBundleShortVersionString": VERSION,
        "CFBundleVersion": VERSION,
        "CFBundleInfoDictionaryVersion": "6.0",
        "LSMinimumSystemVersion": "13.0",
        "NSHighResolutionCapable": True,
        "NSPrincipalClass": "NSApplication",
        "LSApplicationCategoryType": "public.app-category.productivity",
    })

INSTALL_CMD = r"""#!/bin/bash
set -e
cd "$(dirname "$0")"

DEST="/Applications"
[ -w "$DEST" ] || { DEST="$HOME/Applications"; mkdir -p "$DEST"; }

if [ -d "Lumenotepad.app" ]; then
  echo "Installing Lumenotepad into $DEST ..."
  pkill -x Lumenotepad 2>/dev/null || true
  for i in 1 2 3 4 5; do pgrep -x Lumenotepad >/dev/null || break; sleep 1; done
  pgrep -x Lumenotepad >/dev/null && pkill -9 -x Lumenotepad 2>/dev/null || true
  rm -rf "$DEST/Lumenotepad.app"
  cp -R "Lumenotepad.app" "$DEST/Lumenotepad.app"
elif [ -d "$DEST/Lumenotepad.app" ]; then
  echo "Repairing the copy already in $DEST ..."
else
  echo "Could not find Lumenotepad.app next to this file or in $DEST." >&2
  exit 1
fi

APP="$DEST/Lumenotepad.app"
chmod +x "$APP/Contents/MacOS/Lumenotepad" 2>/dev/null || true
xattr -cr "$APP" 2>/dev/null || true

echo "Signing (this takes a few seconds) ..."
find "$APP/Contents/MacOS" -name "*.dylib" -exec codesign --force -s - {} \; >/dev/null 2>&1 || true
codesign --force -s - "$APP/Contents/MacOS/Lumenotepad" >/dev/null 2>&1 || true
codesign --force -s - "$APP" >/dev/null 2>&1 || true

echo "Done. Opening Lumenotepad."
open "$APP"
"""

README = f"""Lumenotepad for macOS  (v{VERSION})
=====================================

DOUBLE-CLICK "Install Lumenotepad.command".

That is the whole install. It copies the app into your Applications folder and
opens it.

macOS will get in the way the first time, because this app is not registered
with Apple:

  1. You get a warning that it cannot be verified. Click "Done".
  2. Open System Settings > Privacy & Security and scroll down. There is a line
     about "Install Lumenotepad.command" being blocked. Click "Open Anyway"
     and confirm.
  3. Double-click the file again. It installs and opens.

You only do this once. Updates come through the app itself afterwards:
Preferences > About > "Check for updates".

DO NOT just drag the app across on its own. macOS refuses to open apps from
outside the App Store that arrive that way, and tells you the app is damaged
even though it is fine. The installer removes that block for you.

IF YOU ALREADY DRAGGED IT AND SEE "damaged":
  Put Lumenotepad back in Applications if you deleted it, then double-click
  "Install Lumenotepad.command". It repairs the copy that is already there.

YOUR NOTES:
  Stored in ~/Library/Application Support/Lumenotepad, and never touched by
  installing or updating.
"""


def stage_bundle(arch: str, rid: str, icns: bytes, plist: bytes) -> str:
    pub = os.path.join(ROOT, "src", "Lumenotepad", "bin", "Release", "net10.0", rid, "publish")
    if not os.path.isdir(pub):
        sys.exit(f"missing publish output: {pub} - run dotnet publish -r {rid} first")
    app = os.path.join(DIST, "stage", arch, "Lumenotepad.app")
    shutil.rmtree(os.path.dirname(app), ignore_errors=True)
    contents = os.path.join(app, "Contents")
    os.makedirs(os.path.join(contents, "Resources"), exist_ok=True)
    shutil.copytree(pub, os.path.join(contents, "MacOS"))
    with open(os.path.join(contents, "Info.plist"), "wb") as f:
        f.write(plist)
    with open(os.path.join(contents, "Resources", "lumenotepad.icns"), "wb") as f:
        f.write(icns)
    return app

def sign_bundle(app: str) -> None:
    exe = shutil.which("rcodesign")
    if not exe:
        sys.exit("rcodesign not found - install it with:  cargo install apple-codesign")
    r = subprocess.run([exe, "sign", app], capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("rcodesign failed:\n" + r.stdout + "\n" + r.stderr)
    if not os.path.isdir(os.path.join(app, "Contents", "_CodeSignature")):
        sys.exit("rcodesign reported success but wrote no _CodeSignature - refusing to ship it")

def zip_bundle(app: str, out: str) -> int:
    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        root = os.path.dirname(app)
        for base, _, files in os.walk(app):
            for name in files:
                full = os.path.join(base, name)
                arc = os.path.relpath(full, root).replace("\\", "/")
                mode = 0o755 if (name == "Lumenotepad" or name.endswith(".dylib")) else 0o644
                write_file(zf, arc, open(full, "rb").read(), mode)
                count += 1
        write_file(zf, "Install Lumenotepad.command", INSTALL_CMD.encode(), 0o755)
        write_file(zf, "README.txt", README.encode(), 0o644)
    return count

def write_file(zf: zipfile.ZipFile, arcname: str, data: bytes, mode: int) -> None:
    zi = zipfile.ZipInfo(arcname)
    zi.create_system = 3
    zi.external_attr = ((0o100000 | mode) & 0xFFFF) << 16
    zi.compress_type = zipfile.ZIP_DEFLATED
    zf.writestr(zi, data)

def main() -> None:
    icns = build_icns()
    plist = info_plist()
    os.makedirs(DIST, exist_ok=True)
    for arch, rid in RIDS.items():
        app = stage_bundle(arch, rid, icns, plist)
        sign_bundle(app)
        out = os.path.join(DIST, f"Lumenotepad-macOS-{VERSION}-{arch}.zip")
        n = zip_bundle(app, out)
        size = os.path.getsize(out)
        print(f"  {arch}: signed, {n} files, {size / 1024 / 1024:.1f} MB -> {os.path.basename(out)}")

    shutil.rmtree(os.path.join(DIST, "stage"), ignore_errors=True)
    print("Now run:  python tools/publish-manifest.py   (writes the shared update manifest)")

if __name__ == "__main__":
    main()
