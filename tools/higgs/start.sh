#!/usr/bin/env bash
# Starts the Higgs TTS 3 server (SGLang-Omni) for REA-Radio-DJ on port 8001.
# Reference clips for cloned voices must be inside VOICES (default: ~/higgs/voices).
export PATH="$HOME/.local/bin:$PATH"
cd ~/higgs
source .venv/bin/activate
# CUDA toolkit that came with the pip packages (nvcc for JIT kernels); no system CUDA needed.
export CUDA_HOME="$VIRTUAL_ENV/lib/python3.12/site-packages/nvidia/cu13"
export PATH="$CUDA_HOME/bin:$PATH"
# WSL: the GPU driver library (libcuda) lives here; JIT kernels link against it.
# The pip CUDA runtime only ships libcudart.so.13; the JIT linker asks for libcudart.so.
mkdir -p ~/higgs/link && ln -sf "$CUDA_HOME/lib/libcudart.so.13" ~/higgs/link/libcudart.so
export LIBRARY_PATH="/usr/lib/wsl/lib:$HOME/higgs/link:$CUDA_HOME/lib:${LIBRARY_PATH:-}"
# DeepGEMM has no complete kernels for the RTX 50 series (sm_120) yet; not needed for a 4B TTS model.
export SGLANG_ENABLE_JIT_DEEPGEMM=0
# The first start compiles GPU kernels and captures CUDA graphs: give it more than the default 600 s.
export SGLANG_OMNI_STARTUP_TIMEOUT="${SGLANG_OMNI_STARTUP_TIMEOUT:-1800}"
VOICES="${VOICES:-$HOME/higgs/voices}"
PORT="${PORT:-8001}"
mkdir -p "$VOICES"
exec sgl-omni serve --model-path bosonai/higgs-tts-3-4b --allowed-local-media-path "$VOICES" --port "$PORT"
