# Handoff — Spire Ledger (2026-11-01)

Read with `CLAUDE.md` (architecture, testing) and `docs/PUBLISHING.md` (release rules).

## Where things stand

- **Published:** v0.4.0 on GitHub (release + tag) and the Steam Workshop (item 3831558895, still
  friends-only), with the new images: animated square main image (GIF bytes in `dist/workshop/image.png`,
  confirmed animating) and 5 English previews. The user's `mods/` has v0.4.0 + STS2MCP (may stay).
- **Marketing images done:** `docs/marketing/` (BRIEF.md, HTML sources, captures FR + EN, `render.sh`,
  `make-gif.sh`); README gallery uses `docs/screenshots/marketing/`.
- Everything is committed and pushed.

## Test setup (all local, never on the user's real profiles)

- Tools repo: `K:\Projets\sts2-tools\STS2MCP` (fork MokoGuy/STS2MCP, branch `local-hardening`, pushed).
  New: `STS2MCP_PLAYER_NAMES="Name1,Name2"` renames test players (net id 1 = host, 1000 = client);
  `custom_embark character=REGENT`; `mouse_click`, `key`, `click_node`, `screenshot`, `console`.
- Test instance settings (`%APPDATA%\SlayTheSpire2\default\1\settings.save`): 1920×1080 window,
  language `fra` (switch to `eng` for the English set).
- Co-op with characters: `$TEMP/coopchars.sh SEED HOSTCHAR CLIENTCHAR SHOTNAME` (temporary script; the
  same steps are in `scripts/dev/coop-start.sh` + `custom_embark character=`). Export
  `STS2MCP_PLAYER_NAMES` and `SPIRELEDGER_HISTORY_NETID` before launching.
- `python -I scripts/dev/sts2.py autoplay FLOOR [pick=I]` (works, solo and co-op): plays the run (fights,
  map, events, rewards, rest, shop) to reach a floor — needed for badges (≥ 5 act-1 floors) and real
  per-card plays. Run it on both ports concurrently in co-op (`STS2_API=multiplayer`).
- `sts2.py install` once (STS2MCP may stay in `mods/`, user OK 2026-11-01); never deploy
  while the user's game runs (`tasklist | grep SlayTheSpire2`, check the process has no `--force-steam`).
  Move test run files out of `%APPDATA%\SlayTheSpire2\SpireLedger\runs` afterwards (keep the user's
  `*_2P_76561198040385943.json`).

## Other open items

- Workshop visibility is still `friends_only`: going `unlisted`/`public` is the user's call (after a
  friend has tested the Workshop install on another PC).
- Card history under card choices not verifiable on Steam-less instances without the history hook.
- Build tracking: done after v0.4.0 (Ledger "Builds" tab + final relics/deck in the run detail), not released yet.
- Forecast in co-op: not a limitation. Every monster attack hits all players (`AttackCommand.FromMonster` → `TargetingAllOpponents`, no monster narrows it), and the intent number is computed against the local creature (`AttackIntent.GetSingleDamage` uses `LocalContext.GetMe`), so the local Vulnerable/Weak/etc. are already in it.
