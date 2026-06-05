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
$base = 'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngQuantContext'
$command = Join-Path $base 'command'
$oldKeys = @(
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressCopy',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressReplace'
)

foreach ($oldKey in $oldKeys) {
  if (Test-Path -LiteralPath $oldKey) {
    Remove-Item -LiteralPath $oldKey -Recurse -Force
  }
}

New-Item -Path $base -Force | Out-Null
Set-ItemProperty -Path $base -Name '(default)' -Value 'Сжать PNG...'
Set-ItemProperty -Path $base -Name 'Icon' -Value $installedExe
New-Item -Path $command -Force | Out-Null
Set-ItemProperty -Path $command -Name '(default)' -Value ('"{0}" "%1"' -f $installedExe)

Write-Host "Installed to: $InstallDir"
Write-Host 'Context menu: Сжать PNG...'
