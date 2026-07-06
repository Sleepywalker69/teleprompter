#!/usr/bin/env python3
"""
Pre-install the speech models the Teleprompter WPF app needs, so the app does
NOT have to download them on first run.

It fetches:
  * Whisper ggml model  -> incoming-audio transcription (feature 4)
  * Vosk small EN model -> mic auto-advance / follow-mode (feature 3)

...and places them exactly where the app's ModelManager looks:
    %LOCALAPPDATA%\\Teleprompter\\models
so the app finds them and skips its own download.

Standard library only — no `pip install` required. Just:

    python preinstall_models.py

Options:
    --dest DIR         Override the target models directory.
    --whisper NAME     large-v3-turbo (default) | base | small-en
                       Use base/small-en if you'll run Whisper on CPU.
    --skip-whisper     Only install the Vosk model.
    --skip-vosk        Only install the Whisper model.

NOTE: the --whisper choice must match `WhisperModel` in
windows-app/Teleprompter/Services/ModelManager.cs. Default (large-v3-turbo)
matches the shipped app; change both together if you switch models.
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys
import tempfile
import time
import urllib.request
import zipfile
from pathlib import Path

# --- Whisper ggml models -----------------------------------------------------
# (hf_filename, app_expected_filename)
#   app_expected_filename mirrors ModelManager.WhisperModelPath:
#   "ggml-{GgmlType.ToString().ToLowerInvariant() with no separators}.bin"
WHISPER_MODELS = {
    "large-v3-turbo": ("ggml-large-v3-turbo.bin", "ggml-largev3turbo.bin"),
    "base":           ("ggml-base.bin",           "ggml-base.bin"),
    "small-en":       ("ggml-small.en.bin",       "ggml-smallen.bin"),
}
WHISPER_BASE_URL = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/"

# --- Vosk model --------------------------------------------------------------
VOSK_MODEL_NAME = "vosk-model-small-en-us-0.15"
VOSK_URL = f"https://alphacephei.com/vosk/models/{VOSK_MODEL_NAME}.zip"

USER_AGENT = "TeleprompterModelInstaller/1.0"


def default_models_dir() -> Path:
    local_appdata = os.environ.get("LOCALAPPDATA")
    if local_appdata:
        return Path(local_appdata) / "Teleprompter" / "models"
    # Non-Windows fallback (the app itself is Windows-only, but this lets you
    # stage the files and copy them over).
    print("  ! LOCALAPPDATA not set (not on Windows?) — defaulting to ./models.")
    print("    Use --dest to point at your Windows %LOCALAPPDATA%\\Teleprompter\\models.")
    return Path.cwd() / "models"


def _fmt_bytes(n: float) -> str:
    for unit in ("B", "KB", "MB", "GB"):
        if n < 1024 or unit == "GB":
            return f"{n:.1f}{unit}"
        n /= 1024
    return f"{n:.1f}GB"


def download(url: str, dest: Path, label: str) -> None:
    """Stream `url` to `dest` (atomic via a .part temp file) with a progress line."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})

    print(f"  Downloading {label}")
    print(f"    from {url}")
    start = time.time()
    with urllib.request.urlopen(req) as resp:
        total = int(resp.headers.get("Content-Length", 0))
        done = 0
        chunk = 1024 * 256
        with open(tmp, "wb") as f:
            while True:
                buf = resp.read(chunk)
                if not buf:
                    break
                f.write(buf)
                done += len(buf)
                _progress(done, total, start)
    sys.stdout.write("\n")

    if total and dest_size_mismatch(tmp, total):
        tmp.unlink(missing_ok=True)
        raise IOError(
            f"Size mismatch for {label}: got {tmp.stat().st_size} bytes, "
            f"expected {total}. Download may be corrupt — please retry."
        )
    tmp.replace(dest)


def dest_size_mismatch(path: Path, expected: int) -> bool:
    return path.stat().st_size != expected


def _progress(done: int, total: int, start: float) -> None:
    elapsed = max(time.time() - start, 1e-6)
    speed = done / elapsed
    if total:
        pct = done / total * 100
        bar_len = 30
        filled = int(bar_len * done / total)
        bar = "#" * filled + "-" * (bar_len - filled)
        sys.stdout.write(
            f"\r    [{bar}] {pct:5.1f}%  "
            f"{_fmt_bytes(done)}/{_fmt_bytes(total)}  {_fmt_bytes(speed)}/s   "
        )
    else:
        sys.stdout.write(f"\r    {_fmt_bytes(done)}  {_fmt_bytes(speed)}/s   ")
    sys.stdout.flush()


def install_whisper(models_dir: Path, choice: str) -> None:
    hf_name, app_name = WHISPER_MODELS[choice]
    target = models_dir / app_name
    print(f"\n[Whisper] {choice}")
    if target.exists() and target.stat().st_size > 0:
        print(f"  Already present: {target}  (skipping)")
        return
    download(WHISPER_BASE_URL + hf_name, target, f"Whisper {choice} (~this can be large)")
    print(f"  Installed: {target}")


def install_vosk(models_dir: Path) -> None:
    model_dir = models_dir / VOSK_MODEL_NAME
    marker = model_dir / "am" / "final.mdl"
    print(f"\n[Vosk] {VOSK_MODEL_NAME}")
    if marker.exists():
        print(f"  Already present: {model_dir}  (skipping)")
        return

    with tempfile.TemporaryDirectory() as td:
        zip_path = Path(td) / f"{VOSK_MODEL_NAME}.zip"
        download(VOSK_URL, zip_path, "Vosk small EN model (~50MB)")
        print("  Extracting…")
        models_dir.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(zip_path) as zf:
            # The archive already contains a top-level VOSK_MODEL_NAME/ folder.
            zf.extractall(models_dir)

    if not marker.exists():
        raise IOError(f"Vosk extraction did not produce expected file: {marker}")
    print(f"  Installed: {model_dir}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Pre-install Teleprompter speech models.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--dest", type=Path, default=None,
                        help="Target models directory (default: %%LOCALAPPDATA%%\\Teleprompter\\models)")
    parser.add_argument("--whisper", choices=sorted(WHISPER_MODELS), default="large-v3-turbo",
                        help="Whisper model to install (must match ModelManager.cs)")
    parser.add_argument("--skip-whisper", action="store_true", help="Do not install the Whisper model")
    parser.add_argument("--skip-vosk", action="store_true", help="Do not install the Vosk model")
    args = parser.parse_args()

    models_dir = (args.dest or default_models_dir()).expanduser().resolve()
    print(f"Models directory: {models_dir}")
    models_dir.mkdir(parents=True, exist_ok=True)

    try:
        if not args.skip_whisper:
            install_whisper(models_dir, args.whisper)
        if not args.skip_vosk:
            install_vosk(models_dir)
    except KeyboardInterrupt:
        print("\nInterrupted. Re-run to resume (partial files are discarded).")
        return 130
    except Exception as exc:  # noqa: BLE001 - surface a clean message to the user
        print(f"\nERROR: {exc}", file=sys.stderr)
        return 1

    print("\nAll set. Launch the app and it will find the models already installed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
