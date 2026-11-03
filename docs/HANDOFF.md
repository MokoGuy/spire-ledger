# Handoff — Spire Ledger (2026-11-01)

Read with `CLAUDE.md` (architecture, testing) and `docs/PUBLISHING.md` (release rules).

## Where things stand

- **Published:** v0.5.0 on GitHub (release + tag) and the Steam Workshop (item 3831558895, public
  since 2026-11-01): Ledger "Builds" tab (win rates per character/relic/card from the game's `.run` history,
  favorite/lucky/cursed highlights, game art) and each run's final deck/relics. Images unchanged since
  v0.4.0 (animated GIF main image, 5 English previews; no Builds preview yet). The user's `mods/` has the
  v0.5.0 build + STS2MCP (may stay).
- **Marketing images done:** `docs/marketing/` (BRIEF.md, HTML sources, captures FR + EN, `render.sh`,
  `make-gif.sh`); README gallery uses `docs/screenshots/marketing/`.
- Everything is committed and pushed.

## Test setup (all local, never on the user's real profiles)

- Tools repo: `K:\Projets\sts2-tools\STS2MCP` (fork MokoGuy/STS2MCP, branch `local-hardening`, pushed).
  New: `STS2MCP_PLAYER_NAMES="Name1,Name2"` renames test players (net id 1 = host, 1000 = client);
  `custom_embark character=REGENT`; `mouse_click`, `key`, `click_node`, `screenshot`, `console`.
- **Separate test install** (2026-11-03): the game copy `K:\STS2-test` (own `mods/`: dev SpireLedger +
  STS2MCP), launched Steam-less only by `sts2.py launch`; `sts2.py quit` kills only its processes. The
  user's Steam install has an empty `mods/` and the user is subscribed on the Workshop: never deploy there.
  Old manual files from it: `%APPDATA%\SlayTheSpire2\SpireLedger\backups\steam-mods-20261103`.
- Test profile `%APPDATA%\SlayTheSpire2\default\1` = copies of the user's Steam `profile1` and
  `modded/profile1` (previous test profile in `default\1.backup-20261103`); test mod data (config, runs)
  in `SpireLedger\local`, seeded with copies of the user's run files. `export
  SPIRELEDGER_HISTORY_NETID=76561198040385943` to see that history as the user. Window 1920×1080; clicks
  use 1080p coordinates.
- Co-op with characters: `$TEMP/coopchars.sh SEED HOSTCHAR CLIENTCHAR SHOTNAME` (temporary script; the
  same steps are in `scripts/dev/coop-start.sh` + `custom_embark character=`). Export
  `STS2MCP_PLAYER_NAMES` and `SPIRELEDGER_HISTORY_NETID` before launching.
- `python -I scripts/dev/sts2.py autoplay FLOOR [pick=I]` (works, solo and co-op): plays the run (fights,
  map, events, rewards, rest, shop) to reach a floor — needed for badges (≥ 5 act-1 floors) and real
  per-card plays. Run it on both ports concurrently in co-op (`STS2_API=multiplayer`).
- `sts2.py install` / `deploy` write into the test copy only; `refresh-copy` after a game update.

## Other open items

- Card history under card choices not verifiable on Steam-less instances without the history hook.
- The Ledger's hover names on icons (`UpdateTip`) were not seen in a screenshot: STS2MCP's `mouse_click`
  warps the OS cursor in window pixels (4K) but sends the event in viewport pixels (1080p), so a test can't
  hover. Check by hand.
- Possible next: a Builds preview image for the Workshop/README (marketing pipeline).
- Forecast in co-op: not a limitation. Every monster attack hits all players (`AttackCommand.FromMonster` → `TargetingAllOpponents`, no monster narrows it), and the intent number is computed against the local creature (`AttackIntent.GetSingleDamage` uses `LocalContext.GetMe`), so the local Vulnerable/Weak/etc. are already in it.
