#!/bin/bash
# Builds the macOS installer zip from Windows (or anywhere with the .NET SDK).
#   tools/publish-macos.sh [version]        e.g.  tools/publish-macos.sh 1.0.1
#
# Publishes self-contained builds for Apple Silicon + Intel, then packs both into
# dist/Lumenotepad-macOS-<version>.zip with the arch-detecting installer script.
# Prereq once per icon change: dotnet run --project tools/icongen  (emits assets/macos-iconset).
set -e
cd "$(dirname "$0")/.."
VERSION="${1:-1.0.0}"

for RID in osx-arm64 osx-x64; do
  echo "== publish $RID =="
  dotnet publish src/Lumenotepad/Lumenotepad.csproj -c Release -r "$RID" \
    --self-contained true -p:UseAppHost=true -v q --nologo
done

python tools/publish-macos/make_bundle.py "$VERSION"
