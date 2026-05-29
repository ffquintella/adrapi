#!/usr/bin/env bash
#
# build-api-keys.sh — publish the adrapi-api-keys client as a standalone,
# self-contained single-file executable (no .NET runtime required to run it).
#
# Usage:
#   ./build-api-keys.sh [rid]
#
# `rid` is a .NET Runtime Identifier. If omitted, the host platform is
# detected automatically. Common values:
#   osx-arm64   osx-x64   linux-x64   linux-arm64   win-x64   win-arm64
#
# The executable is written to:
#   artifacts/api-keys/<rid>/adrapi-api-keys[.exe]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/tools/AdrapiApiKeys/AdrapiApiKeys.csproj"

detect_rid() {
  local os arch
  case "$(uname -s)" in
    Darwin) os="osx" ;;
    Linux)  os="linux" ;;
    *)      echo "unknown" ; return ;;
  esac
  case "$(uname -m)" in
    arm64|aarch64) arch="arm64" ;;
    x86_64|amd64)  arch="x64" ;;
    *)             echo "unknown" ; return ;;
  esac
  echo "${os}-${arch}"
}

RID="${1:-$(detect_rid)}"
if [ "$RID" = "unknown" ] || [ -z "$RID" ]; then
  echo "error: could not detect a runtime identifier; pass one explicitly," >&2
  echo "       e.g. ./build-api-keys.sh linux-x64" >&2
  exit 1
fi

OUT="$SCRIPT_DIR/artifacts/api-keys/$RID"

echo "Publishing adrapi-api-keys for $RID -> $OUT"
dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "$OUT"

echo
echo "Done. Run it directly:"
echo "  $OUT/adrapi-api-keys help"
