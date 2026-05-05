# Project Memory: DesktopTaskView

Use this file when continuing development in a new Codex/ChatGPT conversation.

## Project

`DesktopTaskView` is a tiny Windows 11 desktop helper made through vibe coding with Codex + GPT.

Repository name on GitHub: `DesktopTaskView`

Current local folder:

```text
D:\Dev\DesktopTaskView
```

## Current Version

Latest version: `v0.3.0`

`v0.1.0` was the first public release.

`v0.1.1` fixed a bug where clicking inside File Explorer could sometimes be mistaken for clicking the desktop.

`v0.2.0` added a Settings UI, persistent config under `%APPDATA%\DesktopTaskView\settings.ini`,
adjustable click timing, an excluded-process list, configurable hotkeys, an auto-start toggle
in the tray menu, and a runtime-drawn custom tray icon. The strict desktop hit test from
v0.1.1 is preserved.

`v0.3.0` fixed two P0 bugs: (1) maximized windows now restore to maximized state via
`WINDOWPLACEMENT` / `SetWindowPlacement`; (2) clicking a desktop icon no longer triggers
"minimize all" — cross-process `LVM_HITTEST` (VirtualAllocEx/WriteProcessMemory/ReadProcessMemory)
correctly distinguishes icon clicks from empty-desktop clicks.

## Current Behavior

- Single-click an empty desktop area:
  - Minimize regular visible windows.
  - The app records only the windows it minimized.
- Single-click the desktop again:
  - Restore only the windows minimized by this app.
- Double-click an empty desktop area:
  - Open Windows Task View.
- Tray menu:
  - Enable/disable.
  - Open Task View.
  - Show / Restore Desktop.
  - Exit.
- Backup shortcuts:
  - `Ctrl + Alt + F11`: Windows desktop toggle.
  - `Ctrl + Alt + F12`: Task View.

## Tech Stack

- Language: C#
- UI/runtime style: WinForms `ApplicationContext` with a tray icon.
- Compiler: Windows built-in .NET Framework C# compiler:

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

- Build script:

```text
build-exe.bat
```

- Main source file:

```text
DesktopTaskView.cs
```

- Output:

```text
DesktopTaskView.exe
```

## Important Implementation Decisions

### Do not use AutoHotkey as the current main path

AutoHotkey was used for early prototypes, but the current released app is a native C# exe.

Old AutoHotkey experiments were moved to:

```text
useless/
```

### Do not use Ahk2Exe

Ahk2Exe downloads/install flow triggered Windows friction and was abandoned.

### Single-click desktop behavior should not use Shell.ToggleDesktop

`Shell.Application.ToggleDesktop` was unstable in the mouse-triggered path.

The current working approach is:

1. Enumerate regular top-level visible windows.
2. Exclude shell, tray, app-owned windows, invisible/minimized windows, and empty-title windows.
3. Store window handles.
4. Minimize them.
5. On the next desktop single-click, restore only those stored windows.

### Double-click desktop behavior

Task View is opened through Explorer shell:

```text
explorer.exe shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}
```

Fallback uses simulated `Win + Tab`.

### Desktop click detection must be strict

There was a bug where File Explorer file-list areas were mistaken for the desktop because Explorer can expose similar classes such as:

```text
SysListView32
SHELLDLL_DefView
```

Current rule:

- A point counts as desktop only if the clicked window ancestry includes desktop view classes and ultimately belongs to the actual desktop host:

```text
Progman
WorkerW
```

Do not loosen this check casually.

### DPI awareness matters

The app calls `SetProcessDPIAware()` at startup. This was added because high-DPI / scaled coordinates caused unreliable `WindowFromPoint` behavior.

## Current Files

Root should stay clean:

```text
.gitignore
build-exe.bat
DesktopTaskView.cs
DesktopTaskView.exe
disable-exe-startup.bat
disable-exe-startup.ps1
enable-exe-startup.bat
enable-exe-startup.ps1
LICENSE
README.md
RELEASE_NOTES.md
RELEASE_NOTES_v0.1.1.md
PROJECT_MEMORY.md
release/
diary/
useless/
```

`diary/` contains process notes and personal diary drafts.

`useless/` contains old experiments and scripts that should not be deleted unless explicitly requested.

`release/` contains packaged release zips.

## Build

To build locally:

```text
build-exe.bat
```

This creates or replaces:

```text
DesktopTaskView.exe
```

If the exe is running, stop it from tray or kill the process before rebuilding.

## Release Process

When code changes:

1. Build and test locally.
2. Upload/update `DesktopTaskView.cs` in the GitHub repo.
3. Upload/update `DesktopTaskView.exe` in the GitHub repo if keeping exe in root.
4. Create a new GitHub Release.
5. Use semantic-ish versioning:
   - Bug fix: `v0.1.1` -> `v0.1.2`
   - Small feature: `v0.1.x` -> `v0.2.0`
   - Larger stable milestone: eventually `v1.0.0`
6. Upload release assets:
   - `DesktopTaskView.exe`
   - `release/DesktopTaskView-vX.Y.Z.zip`

Do not keep overwriting old releases unless correcting a just-created mistake. Prefer new releases for real changes.

## GitHub Status

The project has been published publicly on GitHub under the user account.

Releases exist:

- `v0.1.0`: first public release.
- `v0.1.1`: File Explorer click detection bug fix.
- `v0.2.0`: Settings UI, persistent config, hotkey/timing/exclusion customization, auto-start toggle, custom tray icon.

## Potential Future Features

Ideas discussed or likely useful:

- Custom tray icon.
- Settings UI.
- Adjustable double-click timing.
- Exclude app list.
- Auto-start toggle inside tray menu.
- Installer.
- GitHub Actions build/release automation.
- Better README screenshots/GIF.

## Collaboration Notes

The user is not presenting this as “I self-taught programming from scratch.”

The accurate framing is:

- The user had the need, tested behavior, made experience judgments, confirmed or rejected directions, and published the result.
- Codex/GPT suggested technical paths, generated code, debugged issues, built release files, and guided GitHub publishing.
- This is a vibe coding project: human taste and feedback plus AI implementation and debugging.

Keep this framing if writing docs/diary content.
