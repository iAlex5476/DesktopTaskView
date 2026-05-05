# DesktopTaskView

A tiny Windows 11 tray helper that brings macOS-style "click empty desktop"
behavior to Windows:

- **Single-click** an empty area of the desktop → minimize all visible windows
  (and restore them on the next single-click).
- **Double-click** an empty area of the desktop → open Windows **Task View**.

It runs entirely from the system tray, ships as a single ~30 KB `.exe`, and
needs no installer, no admin rights, and no external dependencies beyond what
already ships with Windows 10/11.

> Built with Codex/GPT as a *vibe coding* project: the human supplies the
> need, the taste, and the pass/fail judgment; the AI supplies the C# and
> the Win32 plumbing. Be skeptical, test it, and please open an issue if
> something behaves badly on your machine.

---

## Features (v0.3.0)

| | |
|---|---|
| Single-click empty desktop | Minimize all visible top-level windows; restore them on the next single-click. |
| Double-click empty desktop | Open Windows Task View (`Win + Tab`). |
| Accurate maximize restore | Maximized windows come back maximized (uses `WINDOWPLACEMENT`; previously they returned to normal size). |
| Desktop icon detection | Clicking a desktop icon no longer triggers minimize — cross-process `LVM_HITTEST` distinguishes icons from empty space. |
| Strict desktop detection | `Progman` / `WorkerW` ancestry + `SHELLDLL_DefView` ownership check, so File Explorer file lists are not mistaken for the desktop. |
| Settings UI | Toggles, click timing, hotkeys, and exclusions in a proper window. |
| Persistent config | `%APPDATA%\DesktopTaskView\settings.ini`. |
| Excluded apps | Comma-separated list of process names that won't be minimized. |
| Custom tray icon | Drawn at runtime, no external `.ico` file. |
| Auto-start toggle | Writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. |
| Configurable hotkeys | Default `Ctrl+Alt+F11` (toggle desktop) and `Ctrl+Alt+F12` (Task View). |
| Single instance | Named mutex prevents accidental duplicates. |
| DPI-aware | `SetProcessDPIAware()` at startup. |

---

## Install

1. Download `DesktopTaskView.exe` from the
   [latest release](../../releases/latest).
2. Put it anywhere you like — e.g. `C:\Tools\DesktopTaskView\`.
   The settings file is stored in `%APPDATA%\DesktopTaskView\` and is
   independent of the exe location.
3. Double-click the exe. A monitor-with-down-arrow icon appears in the
   system tray.
4. (Optional) Right-click the tray icon → **Start with Windows**.

There is no installer. There is also no uninstaller — delete the exe and the
`%APPDATA%\DesktopTaskView` folder.

### SmartScreen note

The exe is unsigned. The first time you run it, Windows SmartScreen may show
a "Windows protected your PC" dialog. Click **More info → Run anyway** if you
trust the build, or build it yourself from source (instructions below).

---

## Use

### Mouse

| Gesture | Action |
|---|---|
| Left single-click on empty desktop area | Minimize all visible windows. Click again to restore. |
| Left double-click on empty desktop area | Open Task View. |
| Left single-click on tray icon | Open Settings. |
| Right-click on tray icon | Show the menu (Enabled, Open Task View, Show / Restore Desktop, Settings..., Start with Windows, About, Exit). |

The single-click behavior intentionally waits a few ms after the click —
that's the **single-click delay** in Settings — so a fast double-click is
recognized as a double-click and not as two independent single-clicks.

### Hotkeys (defaults; rebindable in Settings)

| Hotkey | Action |
|---|---|
| `Ctrl + Alt + F11` | Minimize / restore (same as single-click). |
| `Ctrl + Alt + F12` | Open Task View (same as double-click). |

Leave a hotkey field blank in Settings to disable that hotkey.

### Excluded processes

In Settings → **Excluded processes**, enter process names (one per line, or
comma-separated, no `.exe` suffix). Those windows will be skipped during the
"minimize all" step. Examples:

```
notepad
code
photoshop
```

---

## Build from source

You need Windows 10 or 11. **No Visual Studio, no .NET SDK, no MSBuild, no
NuGet** — DesktopTaskView builds with the `csc.exe` shipped inside Windows.

```bat
git clone https://github.com/<your-account>/DesktopTaskView.git
cd DesktopTaskView
build-exe.bat
```

This produces `DesktopTaskView.exe` next to the source. The build script
just calls:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
  /target:winexe /platform:anycpu /optimize+ /langversion:5
  /reference:System.dll /reference:System.Core.dll
  /reference:System.Drawing.dll /reference:System.Windows.Forms.dll
  /out:DesktopTaskView.exe DesktopTaskView.cs
```

If `csc.exe` is missing, you don't have the .NET Framework 4.x runtime
enabled. On Windows 10/11 it normally is. Worst case, enable it via
*Turn Windows features on or off → .NET Framework 3.5 / 4.x*.

---

## How it works (short version)

The runtime architecture is intentionally small. Everything lives in
[`DesktopTaskView.cs`](DesktopTaskView.cs):

- **`Program.Main`** — single-instance check, DPI awareness, runs the tray
  context.
- **`TrayContext`** — `ApplicationContext` that owns the tray icon, the
  settings, the click watcher, and the hotkey host.
- **`AppSettings`** — INI-style load/save under `%APPDATA%`. No JSON
  dependency.
- **`DesktopClickWatcher`** — `WH_MOUSE_LL` low-level mouse hook. On
  `WM_LBUTTONDOWN` it asks `DesktopHitTest` whether the click is on the
  empty desktop, and disambiguates single vs. double click using a
  WinForms timer.
- **`DesktopHitTest`** — strict check: `WindowFromPoint` must hit either
  `Progman` / `WorkerW` directly, or a `SHELLDLL_DefView` / `SysListView32`
  whose ancestor chain ends at a `Progman` / `WorkerW` *that itself owns
  a* `SHELLDLL_DefView`. This is what prevents the v0.1.0 false-positive
  inside File Explorer.
- **`WindowMinimizer`** — enumerates top-level windows, filters
  shell/tray/cloaked/excluded/empty-title/tool windows, minimizes the
  rest, and remembers what it minimized so a second click can restore
  exactly that set (and not whatever the user minimized manually).
- **`HotkeyHost`** — hidden `NativeWindow` subclass that receives
  `WM_HOTKEY` for the registered global hotkeys.
- **`SettingsForm`** — WinForms dialog bound to a working copy of the
  settings; on Save it pushes back into `TrayContext` which re-applies
  everything live.

All native interop lives in `Native` at the bottom of the file.

### Design decisions worth knowing

These were learned the hard way during v0.1.x and should not be regressed:

- **Don't use `Shell.Application.ToggleDesktop` for the single-click path.**
  It was unreliable when triggered from a mouse event.
- **Don't loosen the desktop hit test.** `SHELLDLL_DefView` and
  `SysListView32` show up *both* on the desktop *and* inside File
  Explorer. The owning-`SHELLDLL_DefView` check on the ancestor is what
  distinguishes them.
- **Call `SetProcessDPIAware()` early.** Without it, `WindowFromPoint`
  returns wrong handles on scaled displays.
- **AutoHotkey / Ahk2Exe were tried and abandoned.** Old experiments live
  under `useless/` for reference; ignore them.

---

## Files

```
build-exe.bat              build script
DesktopTaskView.cs         single-file source
DesktopTaskView.exe        built binary (also attached to GitHub releases)
enable-exe-startup.bat     auto-start helper (alternative to the tray toggle)
enable-exe-startup.ps1
disable-exe-startup.bat
disable-exe-startup.ps1
LICENSE                    MIT
README.md                  this file
RELEASE_NOTES.md           changelog
PROJECT_MEMORY.md          context notes for future AI-assisted sessions
.gitignore
```

---

## Roadmap / ideas

Tracked rough ideas for future versions:

- Better README screenshots / animated GIF.
- Optional MSI installer.
- GitHub Actions build & release automation.
- Per-monitor exclude rules.
- Pluggable click actions (e.g. cycle workspaces).

PRs welcome, especially with a short description of the use case.

---

## License

MIT — see [LICENSE](LICENSE).
