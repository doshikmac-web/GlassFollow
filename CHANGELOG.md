# Changelog

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
