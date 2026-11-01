# Spire Ledger roadmap

Fork of Damage Charts focused on two-player co-op: exact per-player stats in combat, at the end of a
run, and across runs. Both players install the mod. Distribution: GitHub Releases first.

## Phase 0 — Setup (done)

Fork, rename to SpireLedger, build against the local install, `scripts/import-vanilla-progress.ps1`
to carry unlocks (Daily Climb) into the modded save profile.

## Ongoing — UI that fits the game

Iterate on the panels so they look native: translate the mod's own labels (DEALT, TAKEN, Combat Stats,
Damage / Round, Combat Log, Round… are English while the game runs in French) through the game's
locale, tighten the sparse full-screen layout, use the game's own back button.

## Phase 1 — Correct multiplayer attribution

Damage with no direct dealer (Poison, Doom, some powers) is credited to the local player
(`ActiveSlot()` fallback in `OnHpChanged`), so each PC credits itself with the teammate's poison.
Credit it to the player who applied the power instead (see BAIGUANGMEI/STS2-DamageTracker, MIT).
Check: both PCs show identical numbers for the same fight.

## Phase 2 — More stats per player

Effects applied (Weak, Vulnerable, Poison…) and the extra damage they enabled for others (assist),
block given to allies, cards played, energy spent, damage per energy, biggest hit.

## Phase 3 — Local run history

One JSON file per run, updated after each combat, surviving save & quit / continue. Records each
player's character, game mode (Ascension, Daily Climb, custom modifiers), win/loss and end-of-run
badges. Stored outside the game's save folders (the Steam Cloud sync deletes unknown files there).

## Phase 4 — End-of-run recap

A panel beside the end-of-run badges with the numbers behind each badge.

## Phase 5 — "Ledger" screen in the Compendium

A "Ledger" button next to "Statistiques" in the Compendium (`NCompendiumSubmenu`,
`NCompendiumBottomButton`) opening a screen pushed on `NMainMenuSubmenuStack`: list of runs, per-run
detail, records per mode. Replaces the HTML page idea; an export can come later.

## Phase 6 — Releases

Local build + `gh release` script (CI can't build: it needs the game's DLLs).

## Later

Compare runs between players over a direct encrypted link (iroh, or the Steam networking the game
already uses for co-op).
