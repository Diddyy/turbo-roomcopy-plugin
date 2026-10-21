#!/usr/bin/env sh
# Builds the plugin folder a Turbo server loads: dist/roomcopy/{TurboRoomCopy.dll,manifest.json}.
# TURBO_CLOUD: the turbo-cloud checkout to build against (default ../turbo-cloud), already built.
set -e
cd "$(dirname "$0")/.."
TURBO_CLOUD="${TURBO_CLOUD:-$(pwd)/../turbo-cloud}"
CONFIGURATION="${TURBO_CLOUD_CONFIGURATION:-Debug}"
rm -rf dist/roomcopy
dotnet build TurboRoomCopy/TurboRoomCopy.csproj -c Release -nologo -v q \
  -p:TurboCloudPath="$TURBO_CLOUD" -p:TurboCloudConfiguration="$CONFIGURATION" \
  -p:DesignTime=false -o dist/roomcopy
# Only the plugin's own files: everything else is the host's.
find dist/roomcopy -type f ! -name 'TurboRoomCopy.dll' ! -name 'TurboRoomCopy.pdb' ! -name 'manifest.json' -delete
find dist/roomcopy -mindepth 1 -type d -empty -delete
ls dist/roomcopy
