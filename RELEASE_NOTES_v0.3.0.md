# DesktopTaskView v0.3.0

Two P0 bug fixes. No new features, no breaking changes.

## Fixed

### Maximized windows restore correctly

Previously, a maximized browser, IDE, or video player would come back as a
normal-sized window after you used the minimize/restore toggle. The fix saves
each window's full `WINDOWPLACEMENT` state before minimizing and restores it
with `SetWindowPlacement`, so maximized windows come back maximized and normal
windows come back at their original position and size.

### Clicking a desktop icon no longer triggers "minimize all"

When the desktop icon grid (`SysListView32`) was hit, the old code returned
`true` immediately — meaning a single click on any desktop icon would silently
minimize all your windows. The fix sends `LVM_HITTEST` into explorer.exe's
address space via cross-process memory (`VirtualAllocEx` /
`WriteProcessMemory` / `ReadProcessMemory`) and skips the minimize action when
an icon item is under the cursor.

## SHA-256

```
DesktopTaskView.exe   E8A8A944F3997AC96546D8AF6BEFDF03B76045930D8EDCF22B73694A77BAC17B
```

Verify on Windows:
```powershell
Get-FileHash .\DesktopTaskView.exe -Algorithm SHA256
```

## Install / upgrade

Same as always — single `.exe`, no installer, no admin rights required:

1. Download `DesktopTaskView.exe` below.
2. Replace your existing copy (exit the old one from the tray first).
3. Run it.

> **SmartScreen note:** the exe is unsigned. Click **More info → Run anyway**
> if you trust the build, or build from source with `build-exe.bat`.
