#!/usr/bin/env bash
#
# serve-docs.sh — serve the ADRAPI documentation locally with docsify.
#
# Usage:
#   ./serve-docs.sh [port]
#
# Defaults to port 3000. Requires Node.js. Uses a globally installed
# `docsify-cli` if present, otherwise falls back to `npx docsify-cli`.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCS_DIR="$SCRIPT_DIR/docs"
PORT="${1:-3000}"

if [ ! -d "$DOCS_DIR" ]; then
  echo "error: docs directory not found at $DOCS_DIR" >&2
  exit 1
fi

echo "Serving ADRAPI docs from $DOCS_DIR on http://localhost:$PORT"

if command -v docsify >/dev/null 2>&1; then
  exec docsify serve "$DOCS_DIR" --port "$PORT"
elif command -v npx >/dev/null 2>&1; then
  exec npx docsify-cli serve "$DOCS_DIR" --port "$PORT"
else
  echo "error: neither 'docsify' nor 'npx' found on PATH." >&2
  echo "Install Node.js, then: npm i -g docsify-cli" >&2
  exit 1
fi
