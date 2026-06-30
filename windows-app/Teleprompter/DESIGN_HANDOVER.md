# Design Handover — Teleprompter (WPF Desktop App)

**Audience:** a design-focused Claude tasked with making this app *look good*.
**Your job:** restyle the UI (visual design, layout, theming, states, motion)
**without breaking the functional wiring** described in the "Binding contract"
section. You are free to redesign layout and visuals; you must keep the data
bindings, control `x:Name`s, and event-handler names intact (or update the
matching code-behind if you move them).

---

## 1. What this app is

A native **Windows desktop teleprompter**, built in **C# / .NET 8 + WPF (XAML)**.
It helps someone read a script on camera. It has **two windows**:

1. **ControlWindow** — the operator UI: script editor, settings, and the live
   transcript box. This is the screen that most needs a visual glow-up.
2. **PrompterWindow** — a borderless, always-on-top overlay pinned to the **top
   of the screen** (next to the webcam) that displays the scrolling/teleprompter
   text. Function over decoration: maximum legibility, minimal chrome.

Four features drive the UI:
1. **Read-time estimate** — live word count + estimated reading time.
2. **Top-of-screen overlay** — the PrompterWindow.
3. **Auto-advance** — listens to the mic and advances per spoken sentence
   (alternative to fixed words-per-minute scrolling).
4. **Incoming-audio transcription** — transcribes the audio you *hear* into a
   **selectable** box (manual highlight + copy; never auto-copies).

---

## 2. Hard constraints (please respect)

- **Platform:** WPF on .NET 8 (`net8.0-windows`). Use **XAML + Styles /
  ResourceDictionaries**, not HTML/CSS, not WinForms, not WinUI3.
- **No functional rewrites.** This is a *visual* pass. Keep behavior identical.
- **Two-window model stays.** Don't merge them.
- **PrompterWindow legibility is sacred:** it sits over a live webcam/video
  scene. Keep a high-contrast, semi-opaque dark backdrop, very large light text,
  generous line spacing, centered. Avoid anything that reduces readability
  (busy gradients behind text, thin low-contrast fonts, tight leading).
- **Dependencies:** prefer pure-XAML styling. If you want a component library
  for a modern look, **[WPF-UI (lepoco/wpfui)](https://github.com/lepoco/wpfui)**
  (Fluent/Windows 11 style) is the recommended option — note any NuGet additions
  explicitly in `Teleprompter.csproj` so the build still restores. Do **not** pull
  in heavy/abandoned packages.

---

## 3. Current files

```
windows-app/Teleprompter/
  App.xaml                       # app entry, StartupUri = Windows/ControlWindow.xaml
  Windows/
    ControlWindow.xaml(.cs)      # operator UI  ← primary redesign target
    PrompterWindow.xaml(.cs)     # overlay      ← restyle for legibility only
  Models/AppState.cs             # the bound view-model (see contract below)
  Services/                      # logic only; no UI
  Themes/                        # (create this) put your ResourceDictionaries here
```

Recommended approach: add `Themes/Colors.xaml`, `Themes/Controls.xaml` (or a
single `Theme.xaml`), merge them in `App.xaml` under
`Application.Resources` → `ResourceDictionary.MergedDictionaries`, and move the
inline styles currently in `ControlWindow.xaml` there.

---

## 4. Binding contract — DO NOT BREAK

`ControlWindow.DataContext` is an `AppState`. These bindings must keep working
(rename only if you also update `AppState.cs`):

| Bound property | Direction | UI role |
|---|---|---|
| `ScriptText` | TwoWay | the script editor text |
| `WordCount` | OneWay | "Words: N" |
| `EstimatedDurationText` | OneWay | "Estimated read time: M:SS" |
| `WordsPerMinute` | TwoWay | speed slider (60–300) + readout |
| `FontSize` | TwoWay | prompter font-size slider (16–120) + readout |
| `OverlayHeightFraction` | TwoWay | overlay-height slider (0.15–0.6, shown as %) |
| `AutoAdvanceEnabled` | TwoWay | "Auto-advance" toggle |
| `MirrorMode` | TwoWay | "Mirror mode" toggle |
| `ClickThrough` | TwoWay | "Click-through overlay" toggle |
| `TranscriptionText` | OneWay | the selectable transcript box |

**Named controls referenced from code-behind** (keep these `x:Name`s):
`ScriptBox`, `OpenPrompterButton`, `PlayPauseButton`, `ListenButton`,
`TranscriptBox`, `StatusText`.

**Event handlers wired in XAML** (keep these names / signatures):
`OnOpenPrompter`, `OnPlayPause`, `OnRestart`, `OnPrev`, `OnNext`,
`OnAutoAdvanceChanged` (on the auto-advance CheckBox `Checked` + `Unchecked`),
`OnToggleListen`, `OnCopyAll`, `OnClearTranscript`,
`OnTranscriptTextChanged` (on `TranscriptBox.TextChanged`).

> Two pieces of text are set from code-behind, so style them via their `x:Name`:
> `PlayPauseButton.Content` toggles between **"Play" / "Pause"**;
> `ListenButton.Content` toggles between **"Start listening" / "Stop listening"**;
> `StatusText.Text` shows status/progress/errors.

For **PrompterWindow**, code-behind drives `PrompterText` (a `TextBlock` whose
`Inlines` and `FontSize` are set in code), `ScrollTransform` (a
`TranslateTransform` animated for scrolling), and `MirrorTransform` (a
`ScaleTransform`, `ScaleX` flips for mirror mode). Keep these `x:Name`s and the
`TextBlock`/transform structure; restyle freely around them (colors, backdrop
opacity, padding, the highlight treatment for the active sentence).

---

## 5. ControlWindow — layout & content to design

Current content (group/reorganize as you see fit):

- **Script panel:** a large multiline editor (`ScriptBox`) — the focal input.
- **Live metrics:** word count + estimated read time (small, glanceable).
- **Playback controls:** Open prompter, Play/Pause, Restart, ◀ Prev, Next ▶.
- **Speed & size:** WPM slider, font-size slider (each with a numeric readout).
- **Prompter options:** Auto-advance toggle (+ helper caption), Mirror mode,
  Click-through, Overlay-height slider (shown as %).
- **Incoming-audio transcription:** Start/Stop listening, Copy all, Clear, and
  the selectable transcript box (`TranscriptBox`).
- **Status bar:** `StatusText` at the bottom (shows model-download progress,
  "Listening to: <device>", errors).

Design goals:
- Modern, calm, **dark-theme-first** (this is broadcast/streaming tooling; dark
  reduces glare on camera). A light theme is a nice-to-have, not required.
- Clear visual hierarchy: script editor is primary; settings are secondary and
  shouldn't overwhelm. Consider grouping into cards/sections with headers.
- The two "modes" (WPM scrolling vs. Auto-advance) can feel mutually exclusive —
  consider a visual treatment that makes the active mode obvious (e.g., dim the
  WPM slider when Auto-advance is on). *(Behavior already supports both; this is
  purely visual emphasis.)*
- Buttons need clear primary/secondary roles (Play is primary; Prev/Next/Clear
  are secondary).

---

## 6. States to design (please cover all)

- **Empty / first run:** no script yet, models not downloaded.
- **Model downloading:** first time Auto-advance or Listening is enabled, a model
  downloads (Whisper ~1.5 GB, Vosk ~50 MB). `StatusText` reports progress — give
  this a proper "working" treatment (progress/indeterminate indicator).
- **Playing vs. paused** (Play/Pause button + maybe a subtle "on air"-style cue).
- **Auto-advance listening** (mic active) — a clear "I'm listening to your mic"
  indicator.
- **Transcribing** (loopback active) — a clear "capturing incoming audio"
  indicator, distinct from the mic one so the two audio paths aren't confused.
- **Error** (e.g., model download failed, no audio device) — `StatusText` shows
  it; design an error style.

---

## 7. PrompterWindow — visual spec

- Borderless, always-on-top, spans the screen width at the very top.
- Semi-opaque **dark** backdrop (currently `#CC000000`) — keep it readable over
  any background; you may tune opacity but don't go low-contrast.
- Text: large, light-weight, **centered**, generous line height. The **active
  sentence** (in Auto-advance mode) is highlighted (currently white + semibold)
  while neighbors are dimmed (currently `#888`). Make this highlight elegant and
  unmistakable — it's the reader's anchor point.
- Optional polish: a subtle reading guide / center line, soft top-and-bottom
  fade (vignette) so text eases in/out of view. Keep it tasteful and legible.

---

## 8. Brand / aesthetic direction (suggested, not binding)

- Tone: professional broadcast/creator tool — confident, minimal, high-contrast.
- Suggested palette: near-black surfaces (`#1E1E1E` / `#252526`), a single
  vivid accent for primary actions and the active-sentence highlight, muted grays
  for secondary text. Pick a cohesive accent (current placeholder accent is the
  status-bar blue `#007ACC` and the estimate teal `#4EC9B0` — feel free to
  replace with one unified accent).
- Typography: a clean, highly legible UI font for ControlWindow; a large, even
  legible font for the prompter (the prompter font size is user-controlled).

---

## 9. Deliverables requested

1. Updated XAML for `ControlWindow.xaml` and `PrompterWindow.xaml`.
2. A theme `ResourceDictionary` (or set) under `Themes/`, merged in `App.xaml`,
   containing reusable color/brush resources and control styles
   (Buttons, Sliders, CheckBoxes, TextBoxes, ToggleButtons).
3. Any code-behind tweaks needed *only* to support visuals (e.g., adding
   indeterminate progress, or styling the Play/Listen toggle text via converters
   instead of code) — keeping the contract in §4 intact.
4. Note any NuGet additions in `Teleprompter.csproj`.

Since this environment is Linux, the app can't be compiled/run here — keep XAML
valid and self-consistent so it builds on Windows with `dotnet run`. Flag any
binding/x:Name you changed so the code-behind can be updated to match.
