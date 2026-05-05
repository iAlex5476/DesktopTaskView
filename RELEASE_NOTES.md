# Release Notes

## v0.3.0 — Bug fixes: maximize restore + desktop icon detection

### Fixed

- **Maximized windows now restore correctly.** Previously, a maximized browser,
  IDE, or video player would come back as a normal-sized window after
  minimize/restore. The fix saves each window's full `WINDOWPLACEMENT` before
  minimizing and restores with `SetWindowPlacement`, so maximized windows come
  back maximized and normal windows come back at their original position and size.

- **Clicking a desktop icon no longer triggers "minimize all".** When
  `SysListView32` (the desktop icon grid) was hit, the old code returned
  true immediately, so a single click on any desktop icon would silently
  minimize everything. The fix sends `LVM_HITTEST` into explorer.exe's address
  space via `VirtualAllocEx` / `WriteProcessMemory` / `ReadProcessMemory` and
  skips the minimize action when an icon item is under the cursor.

### SHA-256 (v0.3.0)

```
DesktopTaskView.exe   E8A8A944F3997AC96546D8AF6BEFDF03B76045930D8EDCF22B73694A77BAC17B
```

---

## v0.2.0 — Settings, customization, auto-start

This is the first feature-bearing release after the v0.1.x click-detection fixes.

### New

- **Settings window.** Left-click the tray icon (or pick "Settings..." from the
  tray menu) to open a proper settings dialog.
- **Persistent configuration.** Settings are stored at
  `%APPDATA%\DesktopTaskView\settings.ini` and survive restarts.
- **Adjustable click timing.** Configure the double-click window and the
  single-click confirmation delay in milliseconds, independently of the
  Windows system default.
- **Excluded process list.** Enter process names (e.g. `notepad, code`)
  whose windows should never be minimized.
- **Custom tray icon.** Drawn programmatically — no external `.ico` file
  needed, and the build still produces a single self-contained `.exe`.
- **Auto-start toggle.** "Start with Windows" can now be turned on/off
  from the tray menu and from the Settings window. Writes to
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **Configurable hotkeys.** The two backup hotkeys (`Ctrl+Alt+F11` /
  `Ctrl+Alt+F12`) are now editable in settings; leave a field empty to
  disable a hotkey.
- **Toggle behaviors independently.** "Minimize on single-click" and
  "Open Task View on double-click" can each be turned off without
  exiting the app.

### Preserved (carried over from v0.1.1)

- Strict desktop hit-testing using `Progman` / `WorkerW` ancestry plus a
  `SHELLDLL_DefView` ownership check, so clicks inside File Explorer's
  file list area are no longer mistaken for desktop clicks.
- `SetProcessDPIAware()` at startup for reliable `WindowFromPoint` on
  scaled displays.
- Single-instance enforcement via a named mutex.

### Internal

- Refactored into named components (`TrayContext`, `WindowMinimizer`,
  `DesktopClickWatcher`, `HotkeyHost`, `SettingsForm`) while staying in
  a single `.cs` file that builds with the Windows-bundled
  `Framework64\v4.0.30319\csc.exe`. No SDK / NuGet / MSBuild required.
- Excluded windows now also include cloaked UWP `ApplicationFrameWindow`
  hosts and tool windows (`WS_EX_TOOLWINDOW`).

---

## v0.1.1 — File Explorer click false-positives fixed

Tightened the desktop-click hit test so clicking inside File Explorer's
list view no longer triggers minimize/restore.

## v0.1.0 — First public release

Initial public release of DesktopTaskView.
