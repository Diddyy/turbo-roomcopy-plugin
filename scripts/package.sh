#!/usr/bin/env sh
# Builds the plugin folder a Turbo server loads, dist/roomcopy/: manifest.json, the C# sources
# (server/**/*.cs) and the plugin's files (gamedata/, wwwroot/, client/src/). The server compiles
# the sources (and builds the client module) as it loads the plugin: the folder holds nothing built.
# The sources are compiled first as the server does (server/TurboRoomCopy.csproj imports the server's
# plugin-sdk/Turbo.Plugin.props), so a folder that would not load is not made.
# TurboBin: the server's bin folder to build against (default: ../turbo-cloud's built Turbo.Main).
set -e
cd "$(dirname "$0")/.."
dotnet build server/TurboRoomCopy.csproj -nologo -v q -clp:NoSummary
rm -rf dist/roomcopy
mkdir -p dist/roomcopy
cp manifest.json dist/roomcopy/
find server -name '*.cs' ! -path '*/bin/*' ! -path '*/obj/*' | tar -cf - -T - | tar -xf - -C dist/roomcopy
for folder in gamedata wwwroot client/src; do
  if [ -d "$folder" ]; then mkdir -p "dist/roomcopy/$(dirname "$folder")" && cp -r "$folder" "dist/roomcopy/$folder"; fi
done
echo "dist/roomcopy: $(find dist/roomcopy -type f | wc -l) files"
