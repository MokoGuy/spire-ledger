# Handoff — Spire Ledger (2026-11-01)

Read with `CLAUDE.md` (architecture, testing) and `docs/PUBLISHING.md` (release rules).

## Where things stand

- **Published:** v0.3.0 on GitHub (MokoGuy/spire-ledger, release + tag) and the Steam Workshop
  (item 3831558895, friends-only). The user's PC has a newer local build (not published) in
  `D:\FastSteamLibrary\steamapps\common\Slay the Spire 2\mods\`.
- **Local commits on `main`, not pushed** (pushing main counts as publishing: ask first):
  - `forecast_dock` option, default **above** the player (user's choice "C" — keeps co-op teammates visible).
  - Party damage HUD only **next to the names** in the party list (`party_hud` list/off, N toggles);
    the "above the characters" and "both" variants were removed at the user's request.
  - Marketing brief `docs/marketing/BRIEF.md`; test hooks below.
- Everything is committed (last: "chore: handoff notes…"); 11+ commits ahead of `origin/main`.

## In progress: new screenshots (Workshop + GitHub)

Follow `docs/marketing/BRIEF.md`. Decisions already taken by the user:
- 5 benefits, in order: 1 card choice stats (hero), 2 who carries the party (damage next to names),
  3 forecast incl. Burns / max HP, 4 numbers behind the badges (end-of-run recap), 5 you vs teammate
  (Ledger filtered on a teammate). Import, version banner, placement variants = guide only, not gallery.
- Two **invented** player names (not MokoGuy/Stymp, not "Test Host"); two different characters.
- One full **French** set and one full **English** set (game language + mod labels).
- **Kreon** font for overlays (download from Google Fonts, OFL — user approved); game palette
  gold #EFC851, cream #FFF6E2, dark slate panels; highlight the mod's element (callout/zoom/dim).
- Card history from the user's **real** runs (approved): 142 run files copied (read-only copy) into the
  Steam-less test profile `%APPDATA%\SlayTheSpire2\default\1\profile1\saves\history`; launch the test
  instance with `SPIRELEDGER_HISTORY_NETID=76561198040385943`.
- **Main Workshop image must be square** (list + item page); consider an animated GIF (check what
  Steam/ModUploader accepts: uploader wants `image.png` < 1 MB).
- Plan: compose with HTML/CSS + headless Edge (`msedge --headless --screenshot=... --window-size=...`)
  so CSS handles Kreon weights, shadows and callouts; show ONE annotated capture (FR + EN) to the
  user before producing the rest.

## Test setup (all local, never on the user's real profiles)

- Tools repo: `K:\Projets\sts2-tools\STS2MCP` (fork MokoGuy/STS2MCP, branch `local-hardening`, pushed).
  New: `STS2MCP_PLAYER_NAMES="Name1,Name2"` renames test players (net id 1 = host, 1000 = client);
  `custom_embark character=REGENT`; `mouse_click`, `key`, `click_node`, `screenshot`, `console`.
- Test instance settings (`%APPDATA%\SlayTheSpire2\default\1\settings.save`): 1920×1080 window,
  language `fra` (switch to `eng` for the English set).
- Co-op with characters: `$TEMP/coopchars.sh SEED HOSTCHAR CLIENTCHAR SHOTNAME` (temporary script; the
  same steps are in `scripts/dev/coop-start.sh` + `custom_embark character=`). Export
  `STS2MCP_PLAYER_NAMES` and `SPIRELEDGER_HISTORY_NETID` before launching.
- `python -I scripts/dev/sts2.py autoplay FLOOR [pick=I]` (new, **untested**): plays the run (fights,
  map, events, rewards, rest, shop) to reach a floor — needed for badges (≥ 5 act-1 floors) and real
  per-card plays. Run it on both ports concurrently in co-op (`STS2_API=multiplayer`).
- Always: `sts2.py install` before, `sts2.py uninstall` + delete `mods/STS2_MCP.conf` after; never deploy
  while the user's game runs (`tasklist | grep SlayTheSpire2`, check the process has no `--force-steam`).
  Move test run files out of `%APPDATA%\SlayTheSpire2\SpireLedger\runs` afterwards (keep the user's
  `*_2P_76561198040385943.json`).

## Other open items

- Next release (0.3.1/0.4.0) once screenshots are done: forecast above by default, party HUD list-only.
- Card history under card choices not verifiable on Steam-less instances without the history hook.
- Build tracking (decks/relics per run, win rates) — proposed earlier, not started.
- Forecast in co-op assumes every attack targets the local player.
