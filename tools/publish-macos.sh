#!/bin/bash
# Builds the macOS distribution from Windows (or anywhere with the .NET SDK + rcodesign).
#   tools/publish-macos.sh              # version comes from <Version> in Lumenotepad.csproj
#   tools/publish-macos.sh 1.2.1        # explicit override
#
# Publishes self-contained builds for Apple Silicon + Intel, code-signs each .app with rcodesign, and
# emits one zip per architecture plus the in-app update manifest:
#   dist/Lumenotepad-macOS-<version>-arm64.zip
#   dist/Lumenotepad-macOS-<version>-x64.zip
#   dist/macos-latest.json
#
# Prereqs:
#   cargo install apple-codesign                    (once - the cross-platform Apple code signer)
#   dotnet run --project tools/icongen              (once per icon change - emits assets/macos-iconset)
set -e
cd "$(dirname "$0")/.."

command -v rcodesign >/dev/null || {
  echo "rcodesign not found. Install it with:  cargo install apple-codesign" >&2
  exit 1
}

for RID in osx-arm64 osx-x64; do
  echo "== publish $RID =="
  # dotnet publish never deletes files a previous publish left behind - a renamed dll would
  # linger and ship inside the .app. Start every publish from a clean output dir.
  rm -rf "src/Lumenotepad/bin/Release/net10.0/$RID/publish"
  dotnet publish src/Lumenotepad/Lumenotepad.csproj -c Release -r "$RID" \
    --self-contained true -p:UseAppHost=true -v q --nologo
done

python tools/publish-macos/make_bundle.py "$@"
