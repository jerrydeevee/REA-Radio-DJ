#!/usr/bin/env bash
# Starts the Whisper speech-check server in WSL (port 8002), using the Higgs venv that already has openai-whisper.
# First start downloads the model into ~/.cache/whisper (small: about 460 MB).
#   wsl -d Ubuntu -- bash -lc "~/higgs/whisper-start.sh"     (after copying this file there)
#   or: bash tools/whisper/start.sh from a WSL shell in the repository
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV="${HIGGS_VENV:-$HOME/higgs/.venv}"
export WHISPER_MODEL="${WHISPER_MODEL:-small}"
export WHISPER_PORT="${WHISPER_PORT:-8002}"
exec "$VENV/bin/python" "$HERE/server.py"
