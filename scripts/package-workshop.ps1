<#
.SYNOPSIS
Builds Spire Ledger, stages the Steam Workshop content and uploads it with MegaCrit's ModUploader.

.DESCRIPTION
Windows counterpart of package-workshop.sh. Publishing talks to Steam directly, so it runs here with
the Steam client open and logged in, never in CI.

Workspace: dist/workshop/ (workshop.json + image.png, tracked; content/ is rebuilt each time).
Uploader:  tools/ModUploader/ModUploader.exe (git-ignored), from
           https://github.com/megacrit/sts2-mod-uploader/releases (ModUploader-win-x64.zip).

The first upload creates the Workshop item (visibility from workshop.json, "friends_only" for now) and
writes dist/workshop/mod_id.txt: commit it so later uploads update the same item. Bump the version in
mod_manifest.json and the changeNote in workshop.json before each update.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\package-workshop.ps1            # build + stage + upload
powershell -ExecutionPolicy Bypass -File scripts\package-workshop.ps1 -StageOnly # build + stage only
#>
param(
    [string]$GameDir = 'D:\FastSteamLibrary\steamapps\common\Slay the Spire 2',
    [string]$Uploader,
    [switch]$StageOnly
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$workspace = Join-Path $repo 'dist\workshop'
$content = Join-Path $workspace 'content'
if (-not $Uploader) { $Uploader = Join-Path $repo 'tools\ModUploader\ModUploader.exe' }

Write-Host "==> Building Release"
& 'C:\Program Files\dotnet\dotnet.exe' build SpireLedger.csproj -c Release -o out "-p:STS2GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host "==> Staging $content"
Remove-Item $content -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $content | Out-Null
# Exactly what subscribers receive: the DLL and the manifest, named like a manual install, plus the MIT
# license that must ship with every copy (the game only reads the .json/.dll/.pck files of a mod folder).
Copy-Item out\SpireLedger.dll (Join-Path $content 'SpireLedger.dll')
Copy-Item mod_manifest.json (Join-Path $content 'SpireLedger.json')
Copy-Item LICENSE (Join-Path $content 'LICENSE.txt')

$image = Get-Item (Join-Path $workspace 'image.png')
if ($image.Length -ge 1MB) { throw "image.png is $([math]::Round($image.Length / 1MB, 2)) MB; Steam needs less than 1 MB." }
$cfg = Get-Content (Join-Path $workspace 'workshop.json') -Raw | ConvertFrom-Json
$version = (Get-Content mod_manifest.json -Raw | ConvertFrom-Json).version
$modId = Join-Path $workspace 'mod_id.txt'
Write-Host ("    version {0}, visibility '{1}', item {2}" -f $version, $cfg.visibility,
    $(if (Test-Path $modId) { (Get-Content $modId -Raw).Trim() } else { 'new (first upload)' }))

if ($StageOnly) { Write-Host 'Staged only (-StageOnly).'; return }

if (-not (Test-Path $Uploader)) {
    Write-Host @"

ModUploader not found at $Uploader
  1. Download ModUploader-win-x64.zip from https://github.com/megacrit/sts2-mod-uploader/releases
     and unzip it into tools\ModUploader\ (or pass -Uploader <path to ModUploader.exe>).
  2. Make sure the Steam client is running and logged in.
  3. Re-run this script.
"@
    return
}
if (-not (Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) { throw 'Steam is not running; start it and log in first.' }

Write-Host "==> Uploading with $Uploader"
# Steamworks reads steam_appid.txt from the working directory and loads steam_api64.dll next to the
# exe, both shipped in the uploader's folder: run from there with an absolute workspace path.
Push-Location (Split-Path -Parent $Uploader)
try { & $Uploader upload -w $workspace; $code = $LASTEXITCODE } finally { Pop-Location }
if ($code -ne 0) { throw "ModUploader failed (exit $code); see mod-uploader.log next to the uploader." }
if (Test-Path $modId) { Write-Host "Workshop item: https://steamcommunity.com/sharedfiles/filedetails/?id=$((Get-Content $modId -Raw).Trim())" }
Write-Host 'Done. Commit dist/workshop/mod_id.txt after the first upload.'
