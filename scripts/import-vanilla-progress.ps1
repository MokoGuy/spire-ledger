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

At startup the game syncs Steam Cloud to local (CloudSaveStore.SyncCloudToLocalInternal): a local
save is kept only if its file time equals the cloud file's time, otherwise the cloud copy wins, and
local files missing from the cloud are deleted. So:
- the target modded profile must have been opened once in the game (so it exists in the cloud);
- each copied file gets the cloud file's time, read from Steam's remotecache.vdf, so the game keeps
  it and uploads it on its next save;
- the run history is not copied (the game would delete it).

Close the game and wait until Steam has finished syncing (about 30 s) before running it.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\import-vanilla-progress.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\import-vanilla-progress.ps1 -Profile 2

.EXAMPLE
# Clone normal profile 1 into modded profile 3 (a throwaway test profile).
powershell -ExecutionPolicy Bypass -File scripts\import-vanilla-progress.ps1 -Profile 1 -TargetProfile 3
#>
param(
    [ValidateRange(1, 3)][int]$Profile = 1,
    [ValidateRange(1, 3)][int]$TargetProfile = 0,
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
if ($TargetProfile -eq 0) { $TargetProfile = $Profile }
$dstProfile = Join-Path $account "modded\profile$TargetProfile"
$dst = Join-Path $dstProfile 'saves'

if (-not (Test-Path (Join-Path $src 'progress.save'))) { throw "No normal progress found in $src." }

if (Test-Path $dstProfile) {
    $backup = "$dstProfile.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item $dstProfile $backup -Recurse
    Write-Host "Backed up the modded profile to $backup"
}
New-Item -ItemType Directory -Force $dst | Out-Null

# Cloud timestamps of this game's files, from Steam's remotecache.vdf (account id = SteamID64 - base).
$steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
$accountId = [int64]$SteamId - 76561197960265728
$vdf = if ($steamPath) { Join-Path $steamPath "userdata\$accountId\2868840\remotecache.vdf" } else { $null }
$vdfText = if ($vdf -and (Test-Path $vdf)) { Get-Content $vdf -Raw } else { '' }
function Get-CloudTime([string]$relPath) {
    $m = [regex]::Match($vdfText, '"' + [regex]::Escape($relPath) + '"\s*\{[^}]*?"time"\s*"(\d+)"')
    if ($m.Success) { return [DateTimeOffset]::FromUnixTimeSeconds([int64]$m.Groups[1].Value).LocalDateTime }
    return $null
}

$missing = @()
foreach ($name in 'progress.save', 'progress.save.backup', 'prefs.save', 'prefs.save.backup') {
    $file = Join-Path $src $name
    if (Test-Path $file) {
        $target = Join-Path $dst $name
        Copy-Item $file $target -Force
        if ($name -like '*.backup') { continue }
        $cloudTime = Get-CloudTime "modded/profile$TargetProfile/saves/$name"
        if ($cloudTime) { (Get-Item $target).LastWriteTime = $cloudTime }
        else { $missing += $name }
    }
}
Write-Host "Copied progress and prefs from profile$Profile to modded\profile$TargetProfile"
if ($missing.Count -gt 0) {
    Write-Warning ("No cloud copy found for $($missing -join ', ') in modded\profile$TargetProfile. " +
        "Open that save profile once in the game (with mods), close it, wait ~30 s, then run this again; " +
        "otherwise the game deletes the copied files at startup.")
}
Write-Host "Done. Launch the game with mods right away and pick save profile $TargetProfile."
