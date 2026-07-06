# Redesign notes — Teleprompter visual pass

Companion to `DESIGN_HANDOVER.md`. Summarises what changed, and (per §9)
flags every non-visual touch so the code-behind stays consistent.

The interactive HTML spec these files were built from lives at the project root:
`Teleprompter Redesign.dc.html` (dark Fluent, warm-amber accent, all six states).

---

## Direction

- **Fluent / Windows 11 dark**, single **warm-amber** accent (`#E8A33D`) for
  primary actions, the "on air" cues, and the active prompter sentence.
- A cool **teal** (`#40C4C4`) is reserved *only* for the second audio path
  (incoming-audio transcription), so the mic path and the loopback path never
  look alike.
- Typeface: **Segoe UI** throughout (native, no font shipping needed).

## Files added

- `Themes/Colors.xaml` — all colour brushes (surfaces, text, accent, teal,
  danger, control chrome, status-bar levels).
- `Themes/Controls.xaml` — styles for Button (Primary / Secondary / Ghost /
  Listen-toggle), Slider, CheckBox-as-toggle-switch, TextBox, and an
  indeterminate "busy" ProgressBar.
- `App.xaml` merges them (Colors **before** Controls — the control styles
  reference the colour brushes via `StaticResource`).

## Files restyled

- `Windows/ControlWindow.xaml` — full relayout: header with wordmark + live
  **ON AIR** / **MIC** pills; script editor with glanceable Words / Read-time
  chips; a playback card whose WPM row **dims when Auto-advance is on**;
  grouped "Prompter options" and "Incoming-audio transcription" cards (the
  latter shows a teal **CAPTURING** pill while listening); a themed status bar.
- `Windows/PrompterWindow.xaml` — kept the backdrop / clip / transform
  structure; bumped backdrop opacity `#CC → #E6`, added a faint amber centre
  guide and soft top/bottom fades (all non-interactive, outside the transforms).

## Binding contract — untouched

All bound properties, `x:Name`s, and event handlers from §4 are preserved
verbatim. **No bindings or names were renamed.** New names were only *added*
(see below), so existing code-behind keeps working.

## Non-visual changes to flag

These are the only touches outside pure styling — all additive, all sanctioned
by §9.3 ("code-behind tweaks needed *only* to support visuals").

1. **New named elements in `ControlWindow.xaml`** (referenced by the new
   code-behind below): `StatusBar` (the status Border), `StatusDot` (the
   status Ellipse), `BusyBar` (the indeterminate ProgressBar, `Collapsed` by
   default).
2. **`ControlWindow.xaml.cs`** — added a `StatusLevel` enum, a
   `SetStatus(string, StatusLevel)` overload, and `ApplyStatusLevel(...)`.
   - The original **single-arg `SetStatus(string)` is intentionally kept** — it
     is still handed to `Progress<string>` as an `Action<string>` for
     model-download progress, and overload resolution binds that method group
     to the single-arg version.
   - A handful of existing `SetStatus(...)` calls now pass a level
     (`Busy` while a model prepares → shows `BusyBar`; `Live` while
     listening/capturing; `Error` in the two `catch` blocks; `Info` otherwise).
     No behaviour changed — only the status bar's colour and the busy bar's
     visibility.
3. **`PrompterWindow.xaml.cs`** — changed only the two static highlight brushes:
   active sentence `White → #F6C065` (amber); neighbours `#888 → #707070`.

## Dependencies

**No NuGet additions.** Everything is pure XAML + `Style`/`ControlTemplate`, as
recommended. (WPF-UI was considered and deliberately skipped to keep the build
lean and the restore unchanged.)

## Known follow-ups (optional, not done)

- Native title bar is left as-is; a custom Fluent dark caption bar would match
  the mockup more closely but adds window-drag / caption-button wiring
  (functional, so out of scope for this visual pass).
- Custom slim dark scrollbars were not templated — the default WPF scrollbars
  still show inside the editors.
- Cannot be compiled here (Linux); XAML is kept valid/self-consistent for
  `dotnet run` on Windows.
