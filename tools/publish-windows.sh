#!/bin/bash
# Builds the Windows portable zip.
#   tools/publish-windows.sh              # version comes from <Version> in Lumenotepad.csproj
#   tools/publish-windows.sh 1.2.1        # explicit override
#
# Output: dist/Lumenotepad-<version>-win-x64-portable.zip
#
# Portable rather than an installer on purpose - see tools/publish-windows/make_zip.py for why
# (the Windows build keeps its user data beside its own exe).
set -e
cd "$(dirname "$0")/.."

echo "== publish win-x64 =="
# dotnet publish never deletes files a previous publish left behind - a renamed dll would
# linger and ship inside the zip. Start from a clean output dir.
rm -rf "src/Lumenotepad/bin/Release/net10.0/win-x64/publish"
dotnet publish src/Lumenotepad/Lumenotepad.csproj -c Release -r win-x64 \
  --self-contained true -p:UseAppHost=true -v q --nologo

python tools/publish-windows/make_zip.py "$@"
