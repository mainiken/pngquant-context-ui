param(
  [string]$InstallDir = "$env:LOCALAPPDATA\PngQuantContext",
  [switch]$KeepFiles
)

$ErrorActionPreference = 'Stop'

$keys = @(
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngQuantContext',
  'HKCU:\Software\Classes\PngQuantContext.Submenu',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressCopy',
  'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngquantCompressReplace',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.CopyBalanced',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.CopyQuality',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.CopyFast',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.ReplaceBalanced',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.ReplaceQuality',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\PngQuantContext.OpenSettings'
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
