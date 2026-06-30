# Teleprompter (native Windows app)

A native **C# / .NET 8 WPF** teleprompter for Windows that hooks directly into
Windows audio. Built alongside the original web app (which stays in the repo
root); this is the standalone desktop version.

## Features

1. **Read-time estimate** — live word count and estimated reading time as you
   type, based on your WPM setting.
2. **Top-of-screen overlay** — a borderless, always-on-top window pinned to the
   top of your screen so you can read while looking at the webcam. Supports
   mirror mode (beam-splitter rigs) and an optional click-through mode.
3. **Auto-advance (follow-mode)** — instead of a fixed WPM, the app listens to
   your **microphone (your outgoing voice)** and moves to the next sentence once
   it hears you finish the current one. Uses **Vosk** streaming recognition.
4. **Incoming-audio transcription** — transcribes the audio you **hear** (the
   other party / system playback) via **WASAPI loopback** and shows it in a
   selectable box. Highlight any part and `Ctrl+C`, or use **Copy all**. Nothing
   is auto-copied. Uses **Whisper.net** (GPU-accelerated).

> **Audio routing:** your **microphone (outgoing)** drives auto-advance only and
> is never transcribed. The audio you **hear (incoming/loopback)** is what gets
> transcribed. The two never mix.

## Requirements

- Windows 10 / 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A microphone (for auto-advance) and any audio playing (for transcription)
- **GPU (recommended):** NVIDIA with current drivers + CUDA runtime for fast
  Whisper transcription. Without a usable GPU it falls back to CPU
  (`Whisper.net.Runtime`), which is slower — consider a smaller model (see below).

## Run (development)

```powershell
cd windows-app\Teleprompter
dotnet run
```

On first use of each audio feature the app downloads its speech model **once**
into `%LOCALAPPDATA%\Teleprompter\models`:

- Whisper `large-v3-turbo` ggml (transcription) — ~1.5 GB
- Vosk small English model (auto-advance) — ~50 MB

The status bar shows download progress.

## Build a standalone .exe

```powershell
cd windows-app\Teleprompter
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The executable is produced under `bin\Release\net8.0-windows\win-x64\publish\`.

## Configuration notes

- **Model choice:** `Services/ModelManager.cs` → `WhisperModel`. `LargeV3Turbo`
  gives the best accuracy/latency on an RTX 2080. On CPU-only machines switch to
  `GgmlType.Base` or `GgmlType.SmallEn` for usable speed.
- **Scroll speed heuristic:** `Windows/PrompterWindow.xaml.cs` →
  `OnRendering` (pixels/sec = words/sec × 12, scaled by font size). Tune if your
  continuous scroll feels fast/slow.
- **Transcription chunking:** `Services/LoopbackTranscriber.cs` →
  `ChunkInterval` (default 5 s) and `SilenceThreshold`.

## How to use

1. `dotnet run` → paste your script; watch word count + estimated time update.
2. **Open prompter** → the overlay pins to the top of the screen. Adjust font
   size and overlay height. Toggle mirror / click-through as needed.
3. To scroll by speed: set WPM, then **Play**.
   To scroll by voice: tick **Auto-advance** and just read — each finished
   sentence advances the prompter.
4. To capture what the other person says: click **Start listening**; their
   speech appears in the transcription box. Highlight + `Ctrl+C` or **Copy all**.

## Project layout

```
Teleprompter/
  Models/        AppState (shared, bound to both windows), ObservableObject
  Services/      ReadingEstimator, SentenceSplitter, SpeechMatcher,
                 MicTranscriber (Vosk), LoopbackTranscriber (Whisper),
                 ModelManager (downloads models)
  Windows/       ControlWindow (editor + settings + transcript),
                 PrompterWindow (top-of-screen overlay)
```
