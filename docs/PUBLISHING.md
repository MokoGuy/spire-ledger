# Publishing Spire Ledger (GitHub release + Steam Workshop)

Method and best practices for shipping a new version. Agents: follow this whole file whenever you
release, tag, push a release, or upload to the Workshop.

## Ground rules

- **GitHub is delegated** (user, 2026-11-01): pushing to `main`, tags and GitHub releases on
  MokoGuy/spire-ledger can be done without asking.
- **Every Steam Workshop publication needs the user's explicit go-ahead for that version** — upload,
  description or visibility change. A yes for v0.3.0 is not a yes for v0.4.0. Prepare locally, then ask.
- **This repo is a fork.** `gh` defaults to the upstream repo (brian-gates/sts2-damage-charts) unless told
  otherwise: always pass `-R MokoGuy/spire-ledger` (or run `gh repo set-default MokoGuy/spire-ledger`
  once). `scripts/release.ps1` already does this. Never open issues, PRs or releases upstream by mistake.
- Publishing talks to Steam directly (ModUploader), so it runs on this PC with the Steam client open and
  logged in, never in CI. The build also needs the game's DLLs, so CI can't build releases either.

## Checklist for a new version

1. **Test in game** on the current game version (`scripts/dev/sts2.py`, see CLAUDE.md): load without
   errors (`sts2.py log`), a solo fight, a local co-op fight (`scripts/dev/coop-start.sh`), the recap and
   the Ledger. STS2MCP may stay installed.
2. **Version** — semantic: `0.x.y`, bump minor for features, patch for fixes. The version lives in
   `mod_manifest.json` (`release.ps1 -Version` writes it). The game builds the co-op compatibility list
   from `id-version`.
3. **Manifest** (`mod_manifest.json`):
   - `min_game_version` = oldest game version you tested (else the game logs a warning);
   - `affects_gameplay: false` stays true to fact: this mod only observes. If a future feature changes
     gameplay, set it to `true` — then every co-op player must have exactly the same mods and versions;
   - `description` short, matching the Workshop text.
4. **Release notes** in `docs/releases/v<version>.md` (what's new, install, known limitations). They are
   used by `release.ps1` instead of generated notes (which would list upstream commits).
5. **Workshop texts** in `dist/workshop/workshop.json`: `changeNote` for this version (subscribers see
   it), description in French and English (Steam BBCode), `visibility`.
6. **Ask the user**, then publish:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version x.y.z -Publish   # tag, push, GitHub release
   powershell -ExecutionPolicy Bypass -File scripts\package-workshop.ps1                # Workshop upload
   ```
7. Commit anything the scripts changed (manifest version, `mod_id.txt` on a first upload) and push.

## Steam Workshop best practices

Sources: MegaCrit's [sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader) README and source
(`template/README.md`, `UploadCommand.cs`), the game's `ModManager` / `JoinFlow`, and our own uploads.
No official MegaCrit publishing guide beyond the uploader README was found (2026-11).

- **Uploader**: `ModUploader-win-x64.zip` from the official releases only; check the SHA-256 against the
  GitHub release digest; unzip into `tools/ModUploader/` (git-ignored). It reads `steam_appid.txt` and
  `steam_api64.dll` from its own folder, so run it from there with an absolute workspace path
  (`package-workshop.ps1` does).
- **Workspace** `dist/workshop/`: `workshop.json`, `image.png` (**< 1 MB**, required; it currently holds an
  animated GIF under that name, built by `docs/marketing/make-gif.sh`: Steam accepts it and animates it), optional
  `previews/` (each < 1 MB; missing files are removed from the item), `content/` (rebuilt by the script).
- **content/ = exactly what a manual install has**: `SpireLedger.dll` + `SpireLedger.json` (the
  manifest renamed) + `LICENSE.txt` (MIT: the notice ships with every copy; the game only reads a mod
  folder's .json/.dll/.pck). No `.pdb`, no personal `SpireLedger.conf`, no test tools.
- **Public page**: say where data is stored and what goes over the network (only the mod version, to the
  run's players), that Early Access updates can break a panel, and link GitHub issues (enabled on the
  fork: forks start with issues off).
- **One item forever**: the first upload writes `dist/workshop/mod_id.txt` — it is committed so every
  later upload updates the same item (item 3831558895). Deleting it creates a new item.
- **Visibility** (`private`, `friends_only`, `unlisted`, `public`): the item is `public` since v0.5.0
  (user, 2026-11-01). `workshop.json`'s `visibility` is sent on **every** upload and overrides a change made
  on the Workshop web page (the v0.5.0 upload put a web-side switch to public back to friends-only), so
  keep the file in sync with the page. Check from outside with the public API:
  `curl -s -X POST https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/ -d itemcount=1 -d "publishedfileids[0]=3831558895"`
  (`result` 1 = public and visible, 9 = hidden: not public, or the Workshop legal agreement not accepted).
- **changeNote on every update**: short, user-facing, starts with the version.
- **Tags**: `Tools & APIs` is reserved for tool/API mods. This game's mods use `Characters`, `Cards`,
  `Relics`, `Cosmetics`, `QoL`, `Visuals`, `Misc` plus free-form language tags; Spire Ledger uses `QoL`,
  `English`, `French`.
  `contentDescriptors` only if mature content applies (never for this mod).
- **minBranch/maxBranch**: leave unset (the uploader README says Steam handles them oddly).
- **Legal agreement**: uploading implies accepting the Steam Workshop terms; if the account never did,
  the item may stay hidden until accepted on its page (user action).
- **Don't mix installs**: a PC with the mod both subscribed on the Workshop and copied into `mods/` loads
  two mods with the same id. The developer PC keeps the manual copy (`sts2.py deploy`); players subscribe.
- **Early Access breaks patches**: after every game update, re-test (Harmony targets may be renamed) and
  ship a fix quickly; bump `min_game_version` when you rely on the new game version.
- **Co-op**: with `affects_gameplay: false` a friend without the mod (or with another version) can still
  join; each player with the mod sees the whole party's stats.
