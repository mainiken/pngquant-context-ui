# PngQuant Context UI

Small Windows 11 Explorer context-menu UI for compressing PNG files with `pngquant`.

This project is not a PNG compressor by itself. It is a lightweight UI/context-menu extension for the `pngquant` command-line tool. Install `pngquant` first, bundle it next to this app, or let the app download the official Windows binary on first run.

It adds one native Explorer cascading menu:

```text
Compress PNG
```

The submenu contains quick presets:

- `Compress PNG as copy`
- `Compress PNG and replace`
- `Settings`

The first two commands start compression immediately with the balanced preset. `Settings` opens the compact UI without auto-start so you can adjust options before running compression.

When several PNG files are selected, Windows may launch the menu command once per file. The app merges those launches into one batch window automatically.

## UI

The main compression window is intentionally small because the app is normally launched from Explorer. It shows:

- a per-file queue;
- file name and current status;
- input size, output size, and saved size;
- overall file progress;
- `Cancel` while compression is running.

Compression options are in the small `...` settings menu:

- copy or replace mode;
- balanced, quality, or fast preset;
- no-dithering toggle;
- light or dark theme;
- About, version, and GitHub link.

The progress bar tracks completed files in the queue. `pngquant` does not expose percentage progress inside a single file, so the UI does not fake per-file percentages. `Cancel` stops the current `pngquant` process and marks the remaining queued files as cancelled.

## Requirements and Compatibility

- Recommended and tested target: Windows 11 with the classic/legacy Explorer context menu.
- Other Windows versions and third-party shells are not verified.
- `.NET Framework 4.x` runtime.
- `pngquant.exe` placed next to the app as either:
  - `pngquant.exe`
  - `pngquant\pngquant.exe`

If `pngquant.exe` is missing, the app shows a setup prompt instead of a hard error and can download the official Windows archive from <https://pngquant.org/pngquant-windows.zip>. The official project page is <https://pngquant.org/>.

For a public release, bundle `pngquant.exe` only if its license terms are satisfied. `pngquant` is distributed separately from this UI wrapper.

## Build

Run from PowerShell:

```powershell
.\scripts\build.ps1
```

Output:

```text
dist\PngQuantContext.exe
```

To copy `pngquant.exe` into the local `dist` folder during build:

```powershell
.\scripts\build.ps1 -PngQuantPath "C:\Tools\pngquant\pngquant.exe"
```

## Install

```powershell
.\scripts\install.ps1
```

If `dist\pngquant\pngquant.exe` is not present, pass a local `pngquant.exe` path:

```powershell
.\scripts\install.ps1 -PngQuantPath "C:\Tools\pngquant\pngquant.exe"
```

Default install path:

```text
%LOCALAPPDATA%\PngQuantContext
```

The installer writes only to `HKCU`, so administrator rights are not required.

## Uninstall

```powershell
.\scripts\uninstall.ps1
```

## Manual Registry

The installer creates the menu at the PNG extension association level, independent of the default PNG viewer:

```text
HKCU\Software\Classes\SystemFileAssociations\.png\shell\PngQuantContext
```

Preset commands are nested under:

```text
HKCU\Software\Classes\SystemFileAssociations\.png\shell\PngQuantContext\Shell
```

## Notes

- Compression failures are logged to `PngQuantContext.log` next to the app.
- Copy mode writes `image-compressed.png` beside `image.png`.
