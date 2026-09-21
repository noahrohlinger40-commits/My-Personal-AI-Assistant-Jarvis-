"""Local, OpenAI-compatible speech-to-text server for Jarvis.

Serves POST /v1/audio/transcriptions (the request shape Jarvis already sends) using
faster-whisper on the CPU, so speech recognition needs no API key and no cloud service.

Settings come from environment variables so the launcher can tune them without edits:
  JARVIS_WHISPER_MODEL    model name or path        (default: small.en)
  JARVIS_WHISPER_HOST     bind address              (default: 127.0.0.1)
  JARVIS_WHISPER_PORT     port                      (default: 8178)
  JARVIS_WHISPER_THREADS  CPU threads for inference (default: 6)
  JARVIS_WHISPER_BEAM     beam size                 (default: 3)
  JARVIS_WHISPER_MODELS   model directory           (default: %LOCALAPPDATA%/Jarvis/whisper/models)
  JARVIS_WHISPER_HINT     short vocabulary hint     (default: none, see the note near VOCABULARY_HINT)
  JARVIS_WHISPER_CLIENT_PROMPT  set to 1 to pass the app's prompt to Whisper (default: off, see below)
  JARVIS_WHISPER_SAVE_DIR save every upload here for debugging (default: off)
  JARVIS_WHISPER_LOG      log file to write to      (default: console)
  JARVIS_PARENT_PID       exit when this process exits (set by Jarvis so the server never outlives it)
"""

from __future__ import annotations

import io
import os
import sys
import threading
import time
from pathlib import Path

import uvicorn
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import JSONResponse, PlainTextResponse
from faster_whisper import WhisperModel
from faster_whisper.utils import download_model

MODEL_NAME = os.environ.get("JARVIS_WHISPER_MODEL", "small.en")
HOST = os.environ.get("JARVIS_WHISPER_HOST", "127.0.0.1")
PORT = int(os.environ.get("JARVIS_WHISPER_PORT", "8178"))
CPU_THREADS = int(os.environ.get("JARVIS_WHISPER_THREADS", "6"))
BEAM_SIZE = int(os.environ.get("JARVIS_WHISPER_BEAM", "3"))
# Kept outside the project on purpose: the project usually lives in a OneDrive-synced folder, and sync
# conflicts corrupt large model files (and the symbolic links a Hugging Face cache is built from).
MODEL_DIR = os.environ.get(
    "JARVIS_WHISPER_MODELS",
    str(Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "Jarvis" / "whisper" / "models"),
)

# Whisper is English-only when the model name ends in ".en"; a language hint is then unnecessary.
ENGLISH_ONLY = MODEL_NAME.endswith(".en")

# Jarvis sends a long block of *instructions* as the transcription prompt. That suits OpenAI's GPT-4o
# transcription models, but Whisper treats the prompt as earlier speech to continue from. On unclear
# audio it then repeats the instructions back as the transcript ("Preserve command phrases..."). So the
# app's prompt is ignored by default.
#
# No vocabulary hint is used by default either. A hint such as "Jarvis." makes Whisper treat the wake
# word as already said, so it sometimes drops it from the start of a command. Without a hint the word
# is at worst misspelled ("Garbus"), and the app matches known misspellings as wake words.
USE_CLIENT_PROMPT = os.environ.get("JARVIS_WHISPER_CLIENT_PROMPT") == "1"
VOCABULARY_HINT = os.environ.get("JARVIS_WHISPER_HINT", "")
SAVE_DIR = os.environ.get("JARVIS_WHISPER_SAVE_DIR")

app = FastAPI(title="Jarvis local Whisper server")
_model: WhisperModel | None = None
# One inference at a time keeps CPU use predictable while the local LLM is also running.
_inference_lock = threading.Lock()


def model_is_usable(directory: Path) -> bool:
    """True when the folder holds a real, non-empty model file (not a broken link or placeholder)."""
    weights = directory / "model.bin"
    return weights.is_file() and weights.stat().st_size > 1_000_000


def resolve_model_directory() -> Path:
    """Return a folder of plain model files, downloading them once if they are missing or damaged.

    Plain files are used instead of a Hugging Face cache because that cache is built from symbolic
    links, which cloud-sync tools such as OneDrive can silently break.
    """
    explicit = Path(MODEL_NAME)

    if explicit.is_dir():
        return explicit

    directory = Path(MODEL_DIR) / MODEL_NAME.replace("/", "--")

    if not model_is_usable(directory):
        print(f"[whisper] '{MODEL_NAME}' is missing or damaged; downloading it to {directory} "
              "(one time only)...", flush=True)
        download_model(MODEL_NAME, output_dir=str(directory))

        if not model_is_usable(directory):
            raise RuntimeError(f"The speech model in {directory} is still incomplete after downloading.")

    return directory


def load_model() -> WhisperModel:
    started = time.perf_counter()
    model = WhisperModel(
        str(resolve_model_directory()),
        device="cpu",
        compute_type="int8",
        cpu_threads=CPU_THREADS,
    )
    print(f"[whisper] loaded '{MODEL_NAME}' in {time.perf_counter() - started:.1f}s "
          f"(cpu_threads={CPU_THREADS}, beam={BEAM_SIZE})", flush=True)
    return model


def transcribe_bytes(audio: bytes, language: str | None, prompt: str | None) -> tuple[str, float]:
    assert _model is not None
    started = time.perf_counter()

    with _inference_lock:
        segments, _info = _model.transcribe(
            io.BytesIO(audio),
            language=None if ENGLISH_ONLY and not language else (language or None),
            initial_prompt=(prompt if USE_CLIENT_PROMPT and prompt else VOCABULARY_HINT) or None,
            beam_size=BEAM_SIZE,
            temperature=0.0,
            # Skip silence before decoding. This is what stops Whisper from inventing text
            # such as "Thank you." when the microphone captured only room noise.
            vad_filter=True,
            # Deliberately lenient: the app already gates on voice activity, and a strict filter here
            # trims a quiet first word such as the wake word.
            vad_parameters={
                "threshold": 0.35,
                "min_speech_duration_ms": 100,
                "min_silence_duration_ms": 400,
                "speech_pad_ms": 400,
            },
            condition_on_previous_text=False,
            no_speech_threshold=0.6,
        )
        text = " ".join(segment.text.strip() for segment in segments).strip()

    return text, time.perf_counter() - started


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "model": MODEL_NAME}


@app.get("/v1/models")
def list_models() -> dict[str, object]:
    return {"object": "list", "data": [{"id": MODEL_NAME, "object": "model", "owned_by": "local"}]}


@app.post("/v1/audio/transcriptions")
async def create_transcription(
    file: UploadFile = File(...),
    model: str = Form(default=""),  # accepted for API compatibility; the server's own model is used
    language: str | None = Form(default=None),
    prompt: str | None = Form(default=None),
    response_format: str = Form(default="json"),
):
    audio = await file.read()

    if not audio:
        raise HTTPException(status_code=400, detail="Empty audio upload.")

    if SAVE_DIR:
        Path(SAVE_DIR).mkdir(parents=True, exist_ok=True)
        (Path(SAVE_DIR) / f"{time.strftime('%Y%m%d-%H%M%S')}-{int(time.time() * 1000) % 1000:03d}.wav").write_bytes(audio)

    try:
        text, seconds = transcribe_bytes(audio, language, prompt)
    except Exception as exc:  # decoding errors should surface as a clear HTTP error, not a crash
        raise HTTPException(status_code=422, detail=f"Could not transcribe audio: {exc}") from exc

    print(f"[whisper] {len(audio) / 1024:.0f} KiB -> {seconds:.2f}s: {text!r}", flush=True)

    if response_format == "text":
        return PlainTextResponse(text)

    return JSONResponse({"text": text})


def redirect_output_to_log() -> None:
    """Send stdout/stderr to a file. Jarvis launches this hidden, with no console to print to."""
    log_path = os.environ.get("JARVIS_WHISPER_LOG")

    if not log_path:
        return

    Path(log_path).parent.mkdir(parents=True, exist_ok=True)
    log = open(log_path, "a", buffering=1, encoding="utf-8")
    sys.stdout = log
    sys.stderr = log


def exit_when_parent_exits() -> None:
    """Shut down as soon as the launching Jarvis process ends, even if it crashed."""
    parent_pid = os.environ.get("JARVIS_PARENT_PID")

    if not parent_pid or os.name != "nt":
        return

    import ctypes

    synchronize = 0x00100000
    handle = ctypes.windll.kernel32.OpenProcess(synchronize, False, int(parent_pid))

    if not handle:
        # The parent is already gone, so nobody is left to use this server.
        os._exit(0)

    def wait_then_exit() -> None:
        ctypes.windll.kernel32.WaitForSingleObject(handle, 0xFFFFFFFF)
        os._exit(0)

    threading.Thread(target=wait_then_exit, daemon=True).start()


def main() -> int:
    global _model
    redirect_output_to_log()
    exit_when_parent_exits()
    print(f"[whisper] starting at {time.strftime('%Y-%m-%d %H:%M:%S')}", flush=True)
    _model = load_model()

    # Prime the engine so the first real request is not slower than the rest.
    silence = b"RIFF" + (36 + 32000).to_bytes(4, "little") + b"WAVEfmt " + (16).to_bytes(4, "little") \
        + (1).to_bytes(2, "little") + (1).to_bytes(2, "little") + (16000).to_bytes(4, "little") \
        + (32000).to_bytes(4, "little") + (2).to_bytes(2, "little") + (16).to_bytes(2, "little") \
        + b"data" + (32000).to_bytes(4, "little") + bytes(32000)
    try:
        transcribe_bytes(silence, None, None)
    except Exception as exc:
        print(f"[whisper] warm-up skipped: {exc}", flush=True)

    print(f"[whisper] ready on http://{HOST}:{PORT}/v1", flush=True)
    uvicorn.run(app, host=HOST, port=PORT, log_level="warning")
    return 0


if __name__ == "__main__":
    sys.exit(main())
