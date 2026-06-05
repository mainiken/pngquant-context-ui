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
$base = 'HKCU:\Software\Classes\SystemFileAssociations\.png\shell\PngQuantContext'
$oldKeys = @(
  $base,
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

New-Item -Path $base -Force | Out-Null
Set-ItemProperty -Path $base -Name 'MUIVerb' -Value 'Сжать PNG'
Set-ItemProperty -Path $base -Name '(default)' -Value 'Сжать PNG'
Set-ItemProperty -Path $base -Name 'Icon' -Value $iconValue
foreach ($staleValue in @('SubCommands', 'ExtendedSubCommandsKey')) {
  Remove-ItemProperty -Path $base -Name $staleValue -Force -ErrorAction SilentlyContinue
}

$items = @(
  @{ Key = '01CopyBalanced'; Label = 'Копия - Balanced'; Args = '--auto --mode copy --preset balanced "%1"' },
  @{ Key = '02CopyQuality'; Label = 'Копия - Best quality'; Args = '--auto --mode copy --preset quality "%1"' },
  @{ Key = '03CopyFast'; Label = 'Копия - Fast'; Args = '--auto --mode copy --preset fast "%1"' },
  @{ Key = '04ReplaceBalanced'; Label = 'Заменить - Balanced'; Args = '--auto --mode replace --preset balanced "%1"'; Separator = $true },
  @{ Key = '05ReplaceQuality'; Label = 'Заменить - Best quality'; Args = '--auto --mode replace --preset quality "%1"' },
  @{ Key = '06OpenSettings'; Label = 'Открыть настройки...'; Args = '"%1"'; Separator = $true }
)

foreach ($item in $items) {
  $itemKey = Join-Path $base ("shell\" + $item.Key)
  $command = Join-Path $itemKey 'command'
  New-Item -Path $itemKey -Force | Out-Null
  Set-ItemProperty -Path $itemKey -Name '(default)' -Value $item.Label
  Set-ItemProperty -Path $itemKey -Name 'Icon' -Value $iconValue
  if ($item.Separator) {
    New-ItemProperty -Path $itemKey -Name 'CommandFlags' -Value 0x20 -PropertyType DWord -Force | Out-Null
  }
  New-Item -Path $command -Force | Out-Null
  Set-ItemProperty -Path $command -Name '(default)' -Value ('"{0}" {1}' -f $installedExe, $item.Args)
}

Write-Host "Installed to: $InstallDir"
Write-Host 'Context menu: Сжать PNG'
