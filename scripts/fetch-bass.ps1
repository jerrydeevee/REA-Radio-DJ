<#
  Downloads the BASS native libraries (un4seen.com) that REA-Radio-DJ needs.
  BASS is free for non-commercial use only and is NOT part of this repository.
  See https://www.un4seen.com/ for its license terms.
  Usage:  powershell -ExecutionPolicy Bypass -File scripts\fetch-bass.ps1
#>
param([string]$Target = (Join-Path $PSScriptRoot "..\native"))
$ErrorActionPreference = "Stop"
$zips = @("bass24.zip", "bassmix24.zip", "bassenc24.zip", "bassenc_mp324.zip", "bassloud24.zip", "bassflac24.zip", "bass_fx24.zip", "bassasio14.zip")
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("rea-radio-dj-bass-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force $tmp, $Target | Out-Null
try {
  foreach ($z in $zips) {
    $file = Join-Path $tmp $z
    Write-Host "Downloading $z"
    $url = if ($z -eq "bass_fx24.zip") { "https://www.un4seen.com/files/z/0/$z" } else { "https://www.un4seen.com/files/$z" }
    Invoke-WebRequest $url -OutFile $file -UseBasicParsing
    $dir = Join-Path $tmp ([IO.Path]::GetFileNameWithoutExtension($z))
    Expand-Archive $file $dir -Force
    # 64-bit DLLs live in the x64 sub folder of each archive
    Get-ChildItem $dir -Recurse -Filter *.dll | Where-Object { $_.Directory.Name -eq 'x64' } |
      ForEach-Object { Copy-Item $_.FullName $Target -Force; Write-Host "  -> $($_.Name)" }
    # keep the C headers for reference
    Get-ChildItem $dir -Recurse -Filter *.h | ForEach-Object { Copy-Item $_.FullName $Target -Force }
  }
} finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "BASS libraries are in $Target"
