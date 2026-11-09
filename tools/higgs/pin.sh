#!/usr/bin/env bash
# One CUDA version for compiler, headers and runtime (flashinfer JIT checks this). It must be no newer than the
# driver (nvidia-smi: "CUDA UMD Version") and new enough for the glibc of the distro (Ubuntu 26.04 needs 13.4+).
CUDA="${CUDA:-13.4}"
set -e
export PATH="$HOME/.local/bin:$PATH"
cd ~/higgs
source .venv/bin/activate
uv pip install --prerelease=allow "nvidia-cuda-nvcc==$CUDA.*" "nvidia-cuda-crt==$CUDA.*" "nvidia-nvvm==$CUDA.*" "nvidia-cuda-runtime==$CUDA.*" 2>&1 | tail -10
.venv/lib/python3.12/site-packages/nvidia/cu13/bin/nvcc --version | tail -2
rm -rf ~/.cache/sglang/.cache/flashinfer
echo PIN_DONE
