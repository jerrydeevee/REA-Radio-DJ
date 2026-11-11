<#
  Downloads fpcalc (Chromaprint, https://acoustid.org/chromaprint) that REA-Radio-DJ uses to make an audio
  fingerprint for AcoustID (recognising songs with missing or wrong tags). Chromaprint is LGPL-2.1 and is NOT part of
  this repository; this script puts fpcalc.exe in /native, from where the build copies it next to the engine.
  Usage:  powershell -ExecutionPolicy Bypass -File scripts\fetch-fpcalc.ps1
#>
param(
  [string]$Target = (Join-Path $PSScriptRoot "..\native"),
  [string]$Version = "1.6.2"
)
$ErrorActionPreference = "Stop"
$name = "chromaprint-fpcalc-$Version-windows-x86_64"
$url = "https://github.com/acoustid/chromaprint/releases/download/v$Version/$name.zip"
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("rea-radio-dj-fpcalc-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force $tmp, $Target | Out-Null
try {
  $zip = Join-Path $tmp "$name.zip"
  Write-Host "Downloading $name.zip"
  Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
  Expand-Archive $zip (Join-Path $tmp "x") -Force
  $exe = Get-ChildItem (Join-Path $tmp "x") -Recurse -Filter fpcalc.exe | Select-Object -First 1
  if (-not $exe) { throw "fpcalc.exe not found in $name.zip" }
  Copy-Item $exe.FullName $Target -Force
  Write-Host "  -> fpcalc.exe"
  # Keep Chromaprint's license next to it.
  Get-ChildItem $exe.Directory.FullName -File | Where-Object { $_.Name -match '^(LICENSE|COPYING)' } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $Target ("chromaprint-" + $_.Name)) -Force }
} finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "fpcalc is in $Target"
