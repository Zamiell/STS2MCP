#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Release}"
GAME_DIR="${STS2_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2}"
MODS_DIR="${STS2_MODS_DIR:-$GAME_DIR/mods}"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/STS2_MCP.csproj"
OUT_DIR="$SCRIPT_DIR/out/STS2_MCP"

to_shell_path() {
  local path="$1"
  if [[ "$path" =~ ^([A-Za-z]):/(.*)$ && -d /mnt ]]; then
    local drive="${BASH_REMATCH[1],,}"
    echo "/mnt/$drive/${BASH_REMATCH[2]}"
  else
    echo "$path"
  fi
}

to_windows_path() {
  local path="$1"
  if [[ "$path" =~ ^/mnt/([A-Za-z])/(.*)$ ]]; then
    local drive="${BASH_REMATCH[1]^^}"
    echo "$drive:/${BASH_REMATCH[2]}"
  else
    echo "$path"
  fi
}

GAME_DIR_FS="$(to_shell_path "$GAME_DIR")"
MODS_DIR_FS="$(to_shell_path "$MODS_DIR")"
PROJECT_BUILD="$(to_windows_path "$PROJECT")"
OUT_DIR_BUILD="$(to_windows_path "$OUT_DIR")"

DOTNET="${DOTNET:-}"
if [[ -z "$DOTNET" ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET="dotnet"
  elif [[ -x "/mnt/c/Program Files/dotnet/dotnet.exe" ]]; then
    DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
  elif [[ -x "/c/Program Files/dotnet/dotnet.exe" ]]; then
    DOTNET="/c/Program Files/dotnet/dotnet.exe"
  fi
fi

if [[ -z "$DOTNET" ]]; then
  echo "ERROR: 'dotnet' not found. Install the .NET 9 SDK:" >&2
  echo "  https://dotnet.microsoft.com/download/dotnet/9.0" >&2
  exit 1
fi

if [[ ! -f "$GAME_DIR_FS/data_sts2_windows_x86_64/sts2.dll" ]]; then
  echo "ERROR: Could not find sts2.dll under:" >&2
  echo "  $GAME_DIR_FS/data_sts2_windows_x86_64" >&2
  echo "Set STS2_GAME_DIR to your Slay the Spire 2 install path if it differs." >&2
  exit 1
fi

echo "=== Building STS2_MCP ($CONFIGURATION) ==="
echo "Game directory : $GAME_DIR"
echo "Output         : $OUT_DIR"
echo "Mods directory : $MODS_DIR_FS"
echo

"$DOTNET" build "$PROJECT_BUILD" -c "$CONFIGURATION" -o "$OUT_DIR_BUILD" -p:STS2GameDir="$GAME_DIR"

mkdir -p "$MODS_DIR_FS"
cp "$OUT_DIR/STS2_MCP.dll" "$MODS_DIR_FS/STS2_MCP.dll"
cp "$SCRIPT_DIR/mod_manifest.json" "$MODS_DIR_FS/STS2_MCP.json"

echo
echo "=== Installed STS2_MCP ==="
echo "  $MODS_DIR_FS/STS2_MCP.dll"
echo "  $MODS_DIR_FS/STS2_MCP.json"
