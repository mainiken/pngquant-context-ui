param(
  [string]$InstallDir = "$env:LOCALAPPDATA\PngQuantContext",
  [string]$PngQuantPath = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dist = Join-Path $root 'dist'
$exeSource = Join-Path $dist 'PngQuantContext.exe'

if (-not (Test-Path -LiteralPath $exeSource)) {
  & (Join-Path $root 'scripts\build.ps1') | Out-Null
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -LiteralPath $exeSource -Destination (Join-Path $InstallDir 'PngQuantContext.exe') -Force

$distPngquant = Join-Path $dist 'pngquant\pngquant.exe'
if (Test-Path -LiteralPath $distPngquant) {
  New-Item -ItemType Directory -Path (Join-Path $InstallDir 'pngquant') -Force | Out-Null
  Copy-Item -LiteralPath $distPngquant -Destination (Join-Path $InstallDir 'pngquant\pngquant.exe') -Force
} elseif ($PngQuantPath -and (Test-Path -LiteralPath $PngQuantPath)) {
  New-Item -ItemType Directory -Path (Join-Path $InstallDir 'pngquant') -Force | Out-Null
  Copy-Item -LiteralPath $PngQuantPath -Destination (Join-Path $InstallDir 'pngquant\pngquant.exe') -Force
}

$installedExe = Join-Path $InstallDir 'PngQuantContext.exe'
$iconValue = "$installedExe,0"
$baseReg = 'HKCU\Software\Classes\*\shell\PngQuantContext'
$oldKeys = @(
  'Registry::HKEY_CURRENT_USER\Software\Classes\*\shell\PngQuantContext',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngQuantContext',
  'HKCU:\Software\Classes\PngQuantContext.Submenu',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.CopyBalanced',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.CopyQuality',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.CopyFast',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.ReplaceBalanced',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.ReplaceQuality',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.OpenSettings',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressCopy',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressReplace'
)

foreach ($oldKey in $oldKeys) {
  if (Test-Path -LiteralPath $oldKey) {
    Remove-Item -LiteralPath $oldKey -Recurse -Force
  }
}

& reg.exe add $baseReg /ve /d 'Сжать PNG' /f | Out-Null
& reg.exe add $baseReg /v MUIVerb /d 'Сжать PNG' /f | Out-Null
& reg.exe add $baseReg /v Icon /d $iconValue /f | Out-Null
& reg.exe add $baseReg /v AppliesTo /d 'System.FileExtension:=".png"' /f | Out-Null
& reg.exe delete $baseReg /v SubCommands /f 2>$null | Out-Null
& reg.exe delete $baseReg /v ExtendedSubCommandsKey /f 2>$null | Out-Null

$items = @(
  @{ Key = '01CompressCopy'; Label = 'Сжать PNG как копию'; Args = '--auto --mode copy --preset balanced "%1"' },
  @{ Key = '02CompressReplace'; Label = 'Сжать PNG и заменить'; Args = '--auto --mode replace --preset balanced "%1"' },
  @{ Key = '03Settings'; Label = 'Настройки'; Args = '"%1"'; Separator = $true }
)

foreach ($item in $items) {
  $itemKey = "$baseReg\shell\$($item.Key)"
  $command = "$itemKey\command"
  & reg.exe add $itemKey /ve /d $item.Label /f | Out-Null
  & reg.exe add $itemKey /v Icon /d $iconValue /f | Out-Null
  if ($item.Separator) {
    & reg.exe add $itemKey /v CommandFlags /t REG_DWORD /d 32 /f | Out-Null
  }
  & reg.exe add $command /ve /d ('"{0}" {1}' -f $installedExe, $item.Args) /f | Out-Null
}

Write-Host "Installed to: $InstallDir"
Write-Host 'Context menu: Сжать PNG'
