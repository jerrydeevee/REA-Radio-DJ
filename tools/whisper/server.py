"""Minimal OpenAI-compatible speech-to-text server for the REA-Radio-DJ speech check.

POST /v1/audio/transcriptions (multipart: file, model, language, response_format) -> {"text": "..."}
GET  /v1/models -> the loaded model

Uses openai-whisper (already in the Higgs venv). The model is downloaded once on first start into
~/.cache/whisper (small: about 460 MB). Runs on the GPU when enough memory is free, otherwise on the CPU,
so it does not push Higgs out of video memory.

Environment: WHISPER_MODEL (default small), WHISPER_DEVICE (auto|cpu|cuda), WHISPER_PORT (default 8002).
"""
import os
import tempfile

import torch
import uvicorn
import whisper
from fastapi import FastAPI, File, Form, UploadFile

MODEL = os.environ.get("WHISPER_MODEL", "small")
PORT = int(os.environ.get("WHISPER_PORT", "8002"))


def pick_device() -> str:
    wanted = os.environ.get("WHISPER_DEVICE", "auto")
    if wanted != "auto":
        return wanted
    if torch.cuda.is_available():
        free, _ = torch.cuda.mem_get_info()
        if free > 3 * 1024**3:
            return "cuda"
    return "cpu"


DEVICE = pick_device()
print(f"whisper: loading '{MODEL}' on {DEVICE}", flush=True)
model = whisper.load_model(MODEL, device=DEVICE)
app = FastAPI()


@app.get("/v1/models")
def models():
    return {"data": [{"id": MODEL, "device": DEVICE}]}


@app.post("/v1/audio/transcriptions")
async def transcribe(file: UploadFile = File(...), model_name: str = Form("small", alias="model"),
                     language: str = Form("nl"), response_format: str = Form("json")):
    data = await file.read()
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(data)
        path = tmp.name
    try:
        result = model.transcribe(path, language=language or None, fp16=(DEVICE == "cuda"), temperature=0.0)
        return {"text": result.get("text", "").strip()}
    finally:
        os.unlink(path)


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=PORT, log_level="warning")
