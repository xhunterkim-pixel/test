#!/usr/bin/env bash
# Cloud (Linux) build toolchain for LevelGate Progression. No game install needed.
#   - .NET SDK from Ubuntu's apt archive (dot.net is blocked by the proxy)
#   - Unity 2022.3 reference DLLs (incl. UnityEngine.UI, TextMeshPro) from the Krafs.Rimworld.Ref NuGet package
#   - BepInEx 5 + Harmony from the BepInEx GitHub release
# The DLLs go into a fake SPT folder (never commit it). Usage:
#   bash tools/cloud-build/setup.sh [dir]      (default dir: $TMPDIR or /tmp/lgp-build)
#   dotnet build LevelGate.Progression -c Release -p:SptDir=<dir>/SPT
set -euo pipefail
D="${1:-${TMPDIR:-/tmp}/lgp-build}"; mkdir -p "$D"; cd "$D"
command -v dotnet >/dev/null || { apt-get update -qq || true; DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0; }
RW=1.6.4871
[ -f rw.nupkg ]  || curl -sSL -o rw.nupkg "https://api.nuget.org/v3-flatcontainer/krafs.rimworld.ref/$RW/krafs.rimworld.ref.$RW.nupkg"
[ -f bep.zip ]   || curl -sSL -o bep.zip  "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip"
mkdir -p rw bepx SPT/EscapeFromTarkov_Data/Managed SPT/BepInEx/core
unzip -oq rw.nupkg -d rw && unzip -oq bep.zip -d bepx
cp rw/ref/net472/*.dll SPT/EscapeFromTarkov_Data/Managed/
cp bepx/BepInEx/core/*.dll SPT/BepInEx/core/
echo "ready: -p:SptDir=$D/SPT"
