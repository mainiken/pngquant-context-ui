param(
  [string]$InstallDir = "$env:LOCALAPPDATA\PngQuantContext",
  [switch]$KeepFiles
)

$ErrorActionPreference = 'Stop'

$keys = @(
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngQuantContext',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressCopy',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressReplace'
)

foreach ($key in $keys) {
  if (Test-Path -LiteralPath $key) {
    Remove-Item -LiteralPath $key -Recurse -Force
  }
}

if (-not $KeepFiles -and (Test-Path -LiteralPath $InstallDir)) {
  Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

Write-Host 'Context menu removed.'
