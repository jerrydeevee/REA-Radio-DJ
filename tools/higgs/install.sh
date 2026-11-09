#!/usr/bin/env bash
# Installs the SGLang-Omni server for Higgs TTS 3 in ~/higgs and downloads the model.
set -e
export PATH="$HOME/.local/bin:$PATH"
mkdir -p ~/higgs/voices && cd ~/higgs
echo "== venv"
[ -d .venv ] || uv venv .venv -p 3.12
source .venv/bin/activate
echo "== sglang-omni"
uv pip install --prerelease=allow "sglang-omni==0.2.0"
echo "== torch check"
python - <<'PY'
import torch
print("torch", torch.__version__, "cuda", torch.version.cuda, "available", torch.cuda.is_available())
if torch.cuda.is_available():
    print("device", torch.cuda.get_device_name(0), "capability", torch.cuda.get_device_capability(0))
    print("arch list", torch.cuda.get_arch_list())
PY
echo "== hf cli"
uv pip install -U "huggingface_hub[cli]"
echo "== model download"
hf download bosonai/higgs-tts-3-4b
echo "== DONE"
