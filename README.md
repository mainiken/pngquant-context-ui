# PngQuant Context UI

Small Windows context-menu UI for compressing PNG files with `pngquant`.

It adds one native Explorer cascading menu:

```text
Сжать PNG
```

The submenu contains quick presets:

- `Сжать PNG как копию`
- `Сжать PNG и заменить`
- `Настройки`

The first two commands start compression immediately with the balanced preset. `Настройки` opens the compact UI where you can choose output mode, preset, and optional no-dithering mode.

When several PNG files are selected, Windows may launch the menu command once per file. The app merges those launches into one batch window automatically.

## Requirements

- Windows 10/11.
- `.NET Framework 4.x` runtime.
- `pngquant.exe` placed next to the app as either:
  - `pngquant.exe`
  - `pngquant\pngquant.exe`

For a public release, bundle `pngquant.exe` only if its license terms are satisfied, or ask users to download it separately from <https://pngquant.org/>.

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

The installer creates the menu under `*\\shell` and filters it to PNG files:

```text
HKCU\Software\Classes\*\shell\PngQuantContext
AppliesTo = System.FileExtension:=".png"
```

Preset commands are nested under:

```text
HKCU\Software\Classes\*\shell\PngQuantContext\shell
```

## Notes

- `pngquant` does not expose per-file progress, so the UI shows an indeterminate progress bar while the current file is being processed.
- Compression failures are logged to `PngQuantContext.log` next to the app.
- Copy mode writes `image-compressed.png` beside `image.png`.
