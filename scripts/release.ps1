<#
.SYNOPSIS
Builds Spire Ledger and packages a release zip; with -Publish, tags it and creates the GitHub release.

.DESCRIPTION
The build needs the game's own DLLs (sts2.dll, GodotSharp.dll, 0Harmony.dll), which can't be put in
CI, so releases are built here. Steps:
  1. checks the working tree is clean and the version isn't tagged yet;
  2. writes the version into mod_manifest.json (and commits it if it changed);
  3. builds Release and runs the unit tests;
  4. zips SpireLedger.dll + SpireLedger.json + INSTALL.txt into dist/release/SpireLedger-v<version>.zip;
  5. with -Publish only: tags v<version>, pushes the tag and runs `gh release create` with the zip.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.0

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.0 -Publish
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$GameDir = 'D:\FastSteamLibrary\steamapps\common\Slay the Spire 2',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

if (git status --porcelain) { throw 'Working tree is not clean; commit or stash first.' }
if (git tag --list "v$Version") { throw "Tag v$Version already exists." }

# 1. Version in the manifest
$manifestPath = Join-Path $repo 'mod_manifest.json'
$manifest = Get-Content $manifestPath -Raw
$updated = $manifest -replace '"version":\s*"[^"]*"', "`"version`": `"$Version`""
if ($updated -ne $manifest) {
    [System.IO.File]::WriteAllText($manifestPath, $updated)
    git add mod_manifest.json
    git commit -m "chore: release v$Version" | Out-Null
    Write-Host "Manifest set to $Version"
}

# 2. Build and test
& $dotnet build SpireLedger.csproj -c Release -o out "-p:STS2GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $dotnet test tests\SpireLedger.Tests.csproj "-p:STS2GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

# 3. Package
$stage = Join-Path $repo 'dist\release\stage'
$zip = Join-Path $repo "dist\release\SpireLedger-v$Version.zip"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item out\SpireLedger.dll (Join-Path $stage 'SpireLedger.dll')
Copy-Item mod_manifest.json (Join-Path $stage 'SpireLedger.json')
@"
Spire Ledger v$Version — Slay the Spire 2 mod

Install: copy SpireLedger.dll and SpireLedger.json into the game's mods folder
(Steam > Slay the Spire 2 > Manage > Browse local files, then the "mods" folder; create it if missing),
then start the game and accept loading mods. Both co-op players need the same version.

Keep your unlocks (Daily Climb, characters) in the modded save profile: see
scripts/import-vanilla-progress.ps1 in https://github.com/MokoGuy/spire-ledger

Run history files: %APPDATA%\SlayTheSpire2\SpireLedger\runs
"@ | Set-Content (Join-Path $stage 'INSTALL.txt') -Encoding utf8
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Packaged $zip"

# 4. Publish (explicit only)
if ($Publish) {
    git tag "v$Version"
    git push origin HEAD "v$Version"
    # Hand-written notes in docs/releases/v<version>.md; generated notes would also list upstream commits.
    $notes = Join-Path $repo "docs\releases\v$Version.md"
    $notesArgs = if (Test-Path $notes) { @('--notes-file', $notes) } else { @('--generate-notes') }
    # Name the repo explicitly: in a fork, gh would otherwise default to the upstream repository.
    $slug = (git remote get-url origin) -replace '^.*github\.com[:/]', '' -replace '\.git$', ''
    & 'C:\Program Files\GitHub CLI\gh.exe' release create "v$Version" $zip -R $slug --title "Spire Ledger v$Version" @notesArgs
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
    Write-Host "Released v$Version"
}
else {
    Write-Host 'Not published. Re-run with -Publish to tag, push and create the GitHub release.'
}
