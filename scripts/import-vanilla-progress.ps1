<#
.SYNOPSIS
Copies your normal (unmodded) Slay the Spire 2 progress into the modded save profile.

.DESCRIPTION
With any mod enabled, the game switches to a separate, empty save profile
(%APPDATA%\SlayTheSpire2\steam\<SteamId>\modded\profileN). It has none of your unlocks, so the
Daily Climb (DAILY_RUN_EPOCH), characters and multiplayer Ascension are missing.

This script copies progress.save and prefs.save (and their .backup files) from the normal profile
into the modded one. The normal profile is only read. The current modded profile is first backed up
next to itself as profileN.backup-<timestamp>.

At startup the game syncs Steam Cloud to local: it overwrites a local save when the cloud copy is
newer (by file time) and deletes local history runs that are not in the cloud. So the copied files
are stamped with the current time to win that sync, and the run history is not copied: the game
would delete it on the next launch.

Close the game before running it.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\import-vanilla-progress.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\import-vanilla-progress.ps1 -Profile 2
#>
param(
    [ValidateRange(1, 3)][int]$Profile = 1,
    [string]$SteamId
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue) {
    throw "Slay the Spire 2 is running. Close the game first, otherwise it overwrites the copied files."
}

$root = Join-Path $env:APPDATA 'SlayTheSpire2\steam'
if (-not (Test-Path $root)) { throw "No save folder found at $root." }

if (-not $SteamId) {
    $ids = @(Get-ChildItem $root -Directory | Where-Object { $_.Name -match '^\d+$' } | ForEach-Object Name)
    if ($ids.Count -eq 0) { throw "No Steam account folder found under $root." }
    if ($ids.Count -gt 1) { throw "Several Steam accounts found ($($ids -join ', ')). Pass one with -SteamId." }
    $SteamId = $ids[0]
}

$account = Join-Path $root $SteamId
$src = Join-Path $account "profile$Profile\saves"
$dstProfile = Join-Path $account "modded\profile$Profile"
$dst = Join-Path $dstProfile 'saves'

if (-not (Test-Path (Join-Path $src 'progress.save'))) { throw "No normal progress found in $src." }

if (Test-Path $dstProfile) {
    $backup = "$dstProfile.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item $dstProfile $backup -Recurse
    Write-Host "Backed up the modded profile to $backup"
}
New-Item -ItemType Directory -Force $dst | Out-Null

$now = Get-Date
foreach ($name in 'progress.save', 'progress.save.backup', 'prefs.save', 'prefs.save.backup') {
    $file = Join-Path $src $name
    if (Test-Path $file) {
        $target = Join-Path $dst $name
        Copy-Item $file $target -Force
        # Newer than the cloud copy, so the game's startup sync keeps it and then uploads it.
        (Get-Item $target).LastWriteTime = $now
    }
}
Write-Host "Copied progress and prefs from profile$Profile to modded\profile$Profile"
Write-Host "Done. Launch the game with mods right away and pick save profile $Profile."
