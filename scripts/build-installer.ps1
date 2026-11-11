<#
  Builds the Windows setup (REA-Radio-DJ-<version>-setup.exe) with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
  Engine and studio are published self-contained (no separate .NET needed) into a staging folder first.
  BASS must be present in native\ (scripts\fetch-bass.ps1); it is put in the setup, see NOTICE.md for its licence.
  Usage:  powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 [-Iscc <path to ISCC.exe>] [-Out <folder>]
#>
param(
  [string]$Iscc = "",
  [string]$Out = (Join-Path $PSScriptRoot "..\artifacts"),
  [string]$Staging = (Join-Path ([IO.Path]::GetTempPath()) "rea-radio-dj-setup")
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

if (-not $Iscc) {
  $Iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path $Iscc)) { throw "ISCC.exe (Inno Setup 6) not found; pass -Iscc <path>." }
if (-not (Get-ChildItem (Join-Path $root "native") -Filter bass.dll -ErrorAction SilentlyContinue)) {
  throw "BASS not found in native\. Run scripts\fetch-bass.ps1 first."
}

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "No <Version> in Directory.Build.props" }
Write-Host "REA-Radio-DJ $version"

if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Force $Staging, $Out | Out-Null
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }

foreach ($p in @(@{ Project = "src\ReaRadioDj.Server"; Dir = "engine" }, @{ Project = "src\ReaRadioDj.Studio"; Dir = "studio" })) {
  Write-Host "Publishing $($p.Project)"
  & $dotnet publish (Join-Path $root $p.Project) -c Release -r win-x64 --self-contained true -p:DebugType=none -o (Join-Path $Staging $p.Dir) --nologo -v quiet
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($p.Project)" }
}
# Local data and media of the developer's machine never go into a setup.
foreach ($d in @("engine\data", "engine\media")) {
  $path = Join-Path $Staging $d
  if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
Copy-Item (Join-Path $root "LICENSE"), (Join-Path $root "NOTICE.md"), (Join-Path $root "README.md") $Staging

@"
REA-Radio-DJ $version
$('=' * (13 + $version.Length))

Vrije radio-playout met een AI-presentator. Gemaakt door ICT Holland (DVC-it) voor Radio Aa en Hunze.
Broncode en documentatie: https://github.com/jerrydeevee/REA-Radio-DJ

Status: proof of concept. Nog niet bedoeld voor een echte zender.

Na de installatie
-----------------
- Start "REA-Radio-DJ Studio" uit het Startmenu. De studio start de engine zelf als die nog niet draait.
  Bij de eerste start helpt de inrichthulp met de stationsnaam, de muziekmappen en de geluidskaart.
- Muziek kan in submappen van engine\media\music (elke map wordt een categorie) en jingles in
  engine\media\jingles, of kies eigen mappen en shares in de Bibliotheek onder "Mappen en shares".
- De engine luistert op http://127.0.0.1:8600. Daar staat ook een eenvoudige web-interface.
- De database, instellingen en opnamen van de engine staan in engine\data in deze map.

Bijwerken en verwijderen
------------------------
Een nieuwere setup installeert over deze versie heen; instellingen, muziek en opnamen blijven staan.
Verwijderen gaat via Windows-instellingen > Apps. De mappen engine\data en engine\media blijven dan
staan, zodat er geen muziek, instellingen of opnamen verloren gaan. Gooi ze zelf weg als je ze niet meer nodig hebt.

Licentie
--------
REA-Radio-DJ is vrije software onder de GNU GPL versie 3 of later, met een aanvullende toestemming om
met BASS te linken. Zie LICENSE en NOTICE.md.

Deze setup bevat de BASS-bibliotheken van un4seen developments (https://www.un4seen.com/).
BASS is gratis voor niet-commercieel gebruik. Voor commercieel gebruik, bijvoorbeeld een zender met
reclame-inkomsten, is een licentie van un4seen nodig.

Ook meegeleverd: fpcalc (Chromaprint, LGPL-2.1) voor AcoustID.
"@ | Set-Content (Join-Path $Staging "LEESMIJ.txt") -Encoding UTF8

& $Iscc /Q "/DAppVersion=$version" "/DStaging=$Staging" "/DOutDir=$((Resolve-Path $Out).Path)" (Join-Path $root "installer\REA-Radio-DJ.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
$setup = Join-Path (Resolve-Path $Out).Path "REA-Radio-DJ-$version-setup.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $setup -Leaf)" | Set-Content "$setup.sha256" -Encoding ASCII
Write-Host "Setup: $setup"
Write-Host "SHA-256: $hash"
