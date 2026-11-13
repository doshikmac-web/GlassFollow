# Changelog

## 2.0.2
- Lower GPU load: only the regions of the screen that changed are copied for each captured frame (a full copy
  happens first, after pauses and every half second). `--full-copy` restores the old behaviour.
- `--fps` below ~120 is now a real cap. Before, the capture delivered up to twice the requested rate.

## 2.0.1
- Fix: crash ("Unknown Hard Error") after a while. The window procedure and keyboard hook delegates could be freed by the
  garbage collector while native code still called them; they are now kept alive and cannot throw into native code.

## 2.0.0
- Second glass style, "uWidgets", next to the default one ("correct"). Choose it for all windows with
  `--style uwidgets`, or per window with a `=uw` suffix on a spec (`zen=uw`, `"explorer:CabinetWClass=correct"`).
  Both styles can be used at the same time.
- Settings for the new style: `--uw-refraction`, `--uw-blur`, `--uw-theme`, `--uw-tint`, `--uw-rim`, `--uw-angle`,
  `--uw-sheen`, `--uw-specular`.
- The new shader is adapted from uWidgets and is licensed CC BY-NC-SA 4.0 (non-commercial), see
  THIRD_PARTY_NOTICES.md. Without that file the program works as before and `--style uwidgets` falls back to the
  default style.

## 1.2.1
- Fix: the glass no longer stays lost after sleep, the lock screen or a screensaver. The capture is rebuilt on
  resume, display change and return of the desktop, and monitors are matched by position instead of handle.
- Errors in capture or rendering no longer crash the program silently; it recovers, or restarts itself if the GPU
  device was lost. Recovery events are logged to `%LOCALAPPDATA%\GlassFollow\glassfollow.log`.
- The autostart task now restarts GlassFollow if it exits with a failure.

## 1.2.0
- Interactive autostart setup with an ASCII banner: `GlassFollow-setup.cmd` or `GlassFollow.exe --setup`. Guided flags or a hand-typed command line.

## 1.1.0
- Screenshots: while Win+Shift+S or PrtScn is active, the glass is frozen and, together with the app windows,
  becomes visible to capture. `--no-shot` turns this off.
- The Windhawk mod hides windows from capture only while GlassFollow is running.
- Windhawk mod: per-app settings (enabled, hide from capture, transparency method, window classes).

## 1.0.0
- Liquid-glass refraction behind any number of windows, one capture session per monitor.
- Follows windows across monitors with different refresh rates.
- Window specs: `process[:class[:title]]`, `*` as any process, class prefix with `*`.
- Dialogs (class `#32770`), tool windows and thin windows supported when a class or title is given.
- Pauses and keeps the glass static while a fullscreen app has focus.
- `--list`, `--delay`, `--hidden`, autostart scripts.
- Windhawk mod `glass-prep` (transparency without blur + hide from capture).
