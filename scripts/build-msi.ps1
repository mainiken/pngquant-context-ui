param(
  [string]$Version = '',
  [string]$PngQuantPath = '',
  [string]$WixVersion = '5.0.2'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dist = Join-Path $root 'dist'
$installerDir = Join-Path $root 'installer'
$wxs = Join-Path $installerDir 'Product.wxs'
$toolsRoot = Join-Path $root '.tools'
$wixRoot = Join-Path $toolsRoot "wix-$WixVersion"
$wixExe = Join-Path $wixRoot 'tools\net6.0\any\wix.exe'

if (-not $Version) {
  $source = Get-Content -LiteralPath (Join-Path $root 'src\PngQuantContext.cs') -Raw
  $match = [regex]::Match($source, 'private const string AppVersion = "([^"]+)"')
  if (-not $match.Success) {
    throw 'Could not read AppVersion from src\PngQuantContext.cs.'
  }
  $Version = $match.Groups[1].Value
}

$buildArgs = @()
if ($PngQuantPath) {
  $buildArgs += '-PngQuantPath'
  $buildArgs += $PngQuantPath
}
& (Join-Path $root 'scripts\build.ps1') @buildArgs | Out-Null

if (-not (Test-Path -LiteralPath $wixExe)) {
  New-Item -ItemType Directory -Path $wixRoot -Force | Out-Null
  $package = Join-Path $wixRoot "wix.$WixVersion.nupkg"
  $packageUrl = "https://www.nuget.org/api/v2/package/wix/$WixVersion"

  Write-Host "Downloading WiX Toolset $WixVersion..."
  Invoke-WebRequest -Uri $packageUrl -OutFile $package
  Expand-Archive -LiteralPath $package -DestinationPath $wixRoot -Force

  if (-not (Test-Path -LiteralPath $wixExe)) {
    $wixExe = Get-ChildItem -Path $wixRoot -Recurse -Filter wix.exe |
      Where-Object { $_.FullName -match '\\tools\\net[0-9.]+\\any\\wix\.exe$' } |
      Sort-Object FullName -Descending |
      Select-Object -First 1 -ExpandProperty FullName
  }

  if (-not $wixExe -or -not (Test-Path -LiteralPath $wixExe)) {
    throw "Could not locate wix.exe in downloaded WiX package $WixVersion."
  }
}

$out = Join-Path $dist "PngQuantContextSetup-$Version.msi"
$intermediate = Join-Path $dist 'msi-obj'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Remove-Item -LiteralPath $intermediate -Recurse -Force -ErrorAction SilentlyContinue

& $wixExe build $wxs `
  -arch x64 `
  -define "ProductVersion=$Version" `
  -define "SourceRoot=$root" `
  -define "DistDir=$dist" `
  -intermediatefolder $intermediate `
  -out $out

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

Get-Item -LiteralPath $out
