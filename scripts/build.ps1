param(
  [string]$Configuration = 'Release',
  [string]$PngQuantPath = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root 'src\PngQuantContext.cs'
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'PngQuantContext.exe'

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$cscCandidates = @(
  "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
  "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
  throw 'csc.exe not found. Install .NET Framework Developer Pack or build on Windows with .NET Framework 4.x.'
}

& $csc /nologo /target:winexe /codepage:65001 /out:$out $src /reference:System.Windows.Forms.dll /reference:System.Drawing.dll
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

if ($PngQuantPath -and (Test-Path -LiteralPath $PngQuantPath)) {
  $pngquantDir = Join-Path $dist 'pngquant'
  New-Item -ItemType Directory -Path $pngquantDir -Force | Out-Null
  Copy-Item -LiteralPath $PngQuantPath -Destination (Join-Path $pngquantDir 'pngquant.exe') -Force
}

Get-Item -LiteralPath $out
