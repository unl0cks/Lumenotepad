# Assembles the macOS installer zip from `dotnet publish` output - runs fine on Windows (stdlib only).
#
# Input:  src/Lumenotepad/bin/Release/net10.0/{osx-arm64,osx-x64}/publish  (created by publish-macos.sh)
#         assets/macos-iconset/icon_{16..1024}.png                        (created by tools/icongen)
# Output: dist/Lumenotepad-macOS-<version>.zip containing
#           payload/arm64/Lumenotepad.app     (Apple Silicon)
#           payload/x64/Lumenotepad.app       (Intel)
#           Install Lumenotepad.command       (auto-detects arch, replaces /Applications copy)
#           README.txt
#
# The zip is written with unix modes (create_system=3) so the .command and the apphosts come out
# executable after macOS Archive Utility extracts them - a plain Windows zip would drop the exec
# bit and the installer would not double-click-run. The install script still chmods defensively.
import os
import plistlib
import struct
import sys
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
VERSION = sys.argv[1] if len(sys.argv) > 1 else "1.0.0"
RIDS = {"arm64": "osx-arm64", "x64": "osx-x64"}
ICONSET = os.path.join(ROOT, "assets", "macos-iconset")
DIST = os.path.join(ROOT, "dist")


def icns_rle(data: bytes) -> bytes:
    """The icns packbits variant (it32/ARGB channels): 0x80+n = run of n+3 repeats, 0..0x7F = n+1 literals."""
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
    """Apple-native ARGB entry (what iconutil emits for the small 1x slots): 'ARGB' + RLE'd A,R,G,B planes."""
    with open(os.path.join(ICONSET, f"icon_{size}.rgba"), "rb") as f:
        rgba = f.read()
    if len(rgba) != size * size * 4:
        sys.exit(f"icon_{size}.rgba has {len(rgba)} bytes, expected {size * size * 4} - re-run tools/icongen")
    planes = [rgba[c::4] for c in (3, 0, 1, 2)]        # A, R, G, B
    return b"ARGB" + b"".join(icns_rle(p) for p in planes)


def build_icns() -> bytes:
    """Compose a .icns: PNG entries for the big sizes, native ARGB for the 16/32 1x slots.
    (PNG data in the small legacy types icp4/icp5 renders SCRAMBLED as an app icon in non-Retina
    1x contexts - macOS decodes it as packbits RGB - so those slots use Apple's ARGB format.)"""
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


INSTALL_CMD = """#!/bin/bash
# Lumenotepad installer / updater. Double-click me.
# First run on macOS 15+: click Done on the warning, then System Settings > Privacy & Security >
# "Open Anyway". (macOS 13/14: right-click this file > Open works as a shortcut.)
set -e
cd "$(dirname "$0")"

ARCH="$(uname -m)"
if [ "$ARCH" = "arm64" ]; then SRC="payload/arm64/Lumenotepad.app"; else SRC="payload/x64/Lumenotepad.app"; fi

DEST="/Applications"
if [ ! -w "$DEST" ]; then DEST="$HOME/Applications"; mkdir -p "$DEST"; fi

echo "Installing Lumenotepad ($ARCH) into $DEST ..."
# quit a running copy so the bundle can be replaced cleanly.
# (no Apple events: osascript would LAUNCH the app when it isn't running, and triggers a TCC
# Automation prompt when it is - a remembered "Don't Allow" would make the quit fail silently
# forever. Plain signals need no permission; .NET exits cleanly on SIGTERM.)
pkill -x Lumenotepad 2>/dev/null || true
for i in 1 2 3 4 5; do pgrep -x Lumenotepad >/dev/null || break; sleep 1; done
pgrep -x Lumenotepad >/dev/null && pkill -9 -x Lumenotepad 2>/dev/null || true
rm -rf "$DEST/Lumenotepad.app"
cp -R "$SRC" "$DEST/Lumenotepad.app"

# zips made on Windows can lose the exec bit - restore it defensively
chmod +x "$DEST/Lumenotepad.app/Contents/MacOS/Lumenotepad" 2>/dev/null || true

# clear the download quarantine, then ad-hoc sign every native library and the bundle so
# Gatekeeper on Apple Silicon (which requires signatures) lets it launch. codesign ships
# with macOS itself - no developer tools needed.
xattr -cr "$DEST/Lumenotepad.app" 2>/dev/null || true
find "$DEST/Lumenotepad.app/Contents/MacOS" -name "*.dylib" -exec codesign --force -s - {} \\; >/dev/null 2>&1 || true
codesign --force -s - "$DEST/Lumenotepad.app" >/dev/null 2>&1 || true

echo "Done - launching Lumenotepad."
open "$DEST/Lumenotepad.app"
"""

README = f"""Lumenotepad for macOS  (v{VERSION})
=====================================

HOW TO INSTALL (also how to UPDATE - it replaces the old version, your notes are kept):

  1. Unzip this file (double-click it).
  2. Double-click "Install Lumenotepad.command".
     macOS will say it could not verify it - click "Done" (NOT "Move to Trash").
  3. Open System Settings > Privacy & Security, scroll down to the message about
     "Install Lumenotepad.command" being blocked, and click "Open Anyway"
     (you may need your password or Touch ID).
  4. In the final dialog, click "Open Anyway" / "Open" - the installer then runs
     in a Terminal window.

  (On macOS 13 or 14 there is a shortcut: right-click the .command file and
   choose "Open", then "Open" again.)

  IF NOTHING WORKS: open the Terminal app, type  bash  followed by a space,
  then drag "Install Lumenotepad.command" into the window and press Return.

The installer picks the right build for this Mac (Apple Silicon or Intel), puts
Lumenotepad.app into Applications, and launches it.

Your notebooks are stored in ~/Library/Application Support/Lumenotepad - they are
NEVER touched by installing or updating the app.

If the app doesn't open from Launchpad the very first time: right-click the app in
Applications and choose "Open" once. After that it opens normally.
"""


def add_tree(zf: zipfile.ZipFile, src_dir: str, arc_prefix: str, exe_names: set) -> int:
    count = 0
    for base, _, files in os.walk(src_dir):
        for name in files:
            full = os.path.join(base, name)
            rel = os.path.relpath(full, src_dir).replace("\\", "/")
            arc = f"{arc_prefix}/{rel}"
            mode = 0o755 if (name in exe_names or name.endswith(".dylib")) else 0o644
            write_file(zf, arc, open(full, "rb").read(), mode)
            count += 1
    return count


def write_file(zf: zipfile.ZipFile, arcname: str, data: bytes, mode: int) -> None:
    zi = zipfile.ZipInfo(arcname)
    zi.create_system = 3                      # unix, so modes survive extraction on macOS
    zi.external_attr = ((0o100000 | mode) & 0xFFFF) << 16   # S_IFREG + mode, for picky extractors
    zi.compress_type = zipfile.ZIP_DEFLATED
    zf.writestr(zi, data)


def main() -> None:
    icns = build_icns()
    plist = info_plist()
    os.makedirs(DIST, exist_ok=True)
    out = os.path.join(DIST, f"Lumenotepad-macOS-{VERSION}.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for arch, rid in RIDS.items():
            pub = os.path.join(ROOT, "src", "Lumenotepad", "bin", "Release", "net10.0", rid, "publish")
            if not os.path.isdir(pub):
                sys.exit(f"missing publish output: {pub} - run dotnet publish -r {rid} first")
            app = f"payload/{arch}/Lumenotepad.app/Contents"
            n = add_tree(zf, pub, f"{app}/MacOS", exe_names={"Lumenotepad"})
            write_file(zf, f"{app}/Info.plist", plist, 0o644)
            write_file(zf, f"{app}/Resources/lumenotepad.icns", icns, 0o644)
            print(f"  packed {arch}: {n} files")
        write_file(zf, "Install Lumenotepad.command", INSTALL_CMD.encode(), 0o755)
        write_file(zf, "README.txt", README.encode(), 0o644)
    print(f"wrote {out} ({os.path.getsize(out) / 1024 / 1024:.1f} MB)")


if __name__ == "__main__":
    main()
