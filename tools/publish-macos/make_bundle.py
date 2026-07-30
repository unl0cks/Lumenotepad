# Assembles the macOS distribution from `dotnet publish` output - runs on Windows.
#
# Input:  src/Lumenotepad/bin/Release/net10.0/{osx-arm64,osx-x64}/publish  (created by publish-macos.sh)
#         assets/macos-iconset/icon_{16..1024}.png                        (created by tools/icongen)
# Output: dist/Lumenotepad-macOS-<version>-arm64.zip   (Apple Silicon)
#         dist/Lumenotepad-macOS-<version>-x64.zip     (Intel)
#         (the update manifest is written separately by tools/publish-manifest.py, which covers
#          every platform in one file)
#
# The bundle is STAGED ON DISK and code-signed here with rcodesign (the cross-platform Apple signer,
# `cargo install apple-codesign`) rather than by a script on the user's Mac. Apple Silicon refuses to
# execute unsigned Mach-O, and `dotnet publish` leaves ~220 unsigned dylibs, so something has to sign
# them; doing it at build time is what lets the download be a plain drag-into-Applications app instead
# of a Terminal installer.
#
# The signature is AD-HOC, which satisfies the kernel but not Gatekeeper - the first launch of any
# downloaded build still needs one trip through System Settings > Privacy & Security. Every update after
# that is prompt-free because the app fetches those itself (see Services/UpdateService.cs), and nothing
# an app downloads is quarantined. Notarising with a real Developer ID would remove even the first trip.
#
# Zips are written with unix modes (create_system=3) so the app host and dylibs stay executable through
# macOS Archive Utility - a plain Windows zip drops the exec bit and the app will not start.
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
    """<Version> in the csproj is the single source of truth - the binary, the Info.plist and the update
    manifest all have to agree or the updater would offer a build that says it is already installed."""
    m = re.search(r"<Version>([^<]+)</Version>", open(CSPROJ, encoding="utf-8").read())
    if not m:
        sys.exit("no <Version> in Lumenotepad.csproj")
    return m.group(1).strip()


VERSION = sys.argv[1] if len(sys.argv) > 1 else csproj_version()
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


README = f"""Lumenotepad for macOS  (v{VERSION})
=====================================

TO INSTALL, OR TO UPDATE BY HAND:
  Drag Lumenotepad.app into your Applications folder (replace the old one if asked).

THE FIRST TIME YOU OPEN IT, macOS WILL REFUSE:
  1. Double-click Lumenotepad - you get "Apple could not verify Lumenotepad".
     Click "Done".
  2. Open System Settings > Privacy & Security and scroll down. There is a line
     about Lumenotepad being blocked - click "Open Anyway", then confirm.
  3. It opens. You only ever have to do this once.

  (That is macOS being cautious about an app not bought from the App Store - not a
   sign anything is wrong. Any small independent app does this.)

AFTER THAT, UPDATES ARE ONE CLICK:
  Preferences > About > "Check for updates". The app downloads new versions
  itself, so macOS does NOT ask you to approve them again. You never need to
  repeat the steps above, and you never need this zip again.

YOUR NOTES:
  Stored in ~/Library/Application Support/Lumenotepad and never touched by
  installing or updating.
"""


def stage_bundle(arch: str, rid: str, icns: bytes, plist: bytes) -> str:
    """Lay the .app out on disk so it can be signed as a real bundle."""
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
    """Ad-hoc sign the bundle (and, recursively, every Mach-O inside it) with rcodesign.

    Not optional on Apple Silicon: the kernel kills unsigned arm64 code, so without this the app simply
    would not launch after a drag-install. Ad-hoc is as far as we can go without a paid Developer ID -
    it makes the app RUNNABLE, not Gatekeeper-approved."""
    exe = shutil.which("rcodesign")
    if not exe:
        sys.exit("rcodesign not found - install it with:  cargo install apple-codesign")
    r = subprocess.run([exe, "sign", app], capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("rcodesign failed:\n" + r.stdout + "\n" + r.stderr)
    # Prove a signature actually landed rather than trusting the exit code.
    if not os.path.isdir(os.path.join(app, "Contents", "_CodeSignature")):
        sys.exit("rcodesign reported success but wrote no _CodeSignature - refusing to ship it")


def zip_bundle(app: str, out: str) -> int:
    """Zip the signed bundle with unix modes intact. Signatures cover file CONTENT, not mode, so setting
    the exec bit here cannot invalidate them."""
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
        write_file(zf, "README.txt", README.encode(), 0o644)
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
