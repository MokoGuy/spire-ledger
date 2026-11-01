# Spire Ledger — screenshot brief (Workshop + GitHub)

Method adapted from the `aso-appstore-screenshots` skill (adamlyttleapps): benefits first, one capture
per benefit, one headline per capture, one visual identity, a showcase image at the end. Steam advice:
best image first, no redundant images, captions readable at thumbnail size.

## Audience

Co-op Slay the Spire 2 players (2 friends) who want to know who carried the run and play better.

## Benefits (draft, to confirm) — one capture each, in this order

| # | Headline FR | Headline EN | Capture |
|---|---|---|---|
| 1 | CHOISISSEZ VOS CARTES EN CONNAISSANCE DE CAUSE | PICK CARDS WITH YOUR OWN STATS | Card reward: copies in deck, plays this run, wins with it over past runs |
| 2 | VOYEZ QUI PORTE L'ÉQUIPE, EN DIRECT | SEE WHO CARRIES THE PARTY, LIVE | Co-op fight: damage + share next to each name, forecast above the player |
| 3 | ANTICIPEZ CHAQUE POINT DE VIE PERDU | KNOW EVERY HP YOU'RE ABOUT TO LOSE | Forecast with attacks + Burns + max HP loss, mid-fight, zoom callout |
| 4 | LES CHIFFRES DERRIÈRE CHAQUE BADGE | THE NUMBERS BEHIND EVERY BADGE | End-of-run recap: party table + badges |
| 5 | COMPAREZ-VOUS À VOS COÉQUIPIERS | MEASURE UP AGAINST YOUR TEAMMATES | Ledger filtered on a teammate: you vs them over all runs |

Not in the gallery (guide / README "Getting started" instead): progress import, version banner,
placement variants, debug views.

## Capture rules

- Real-looking players: two names, two different characters (no "Test Host" / "Test Client 1").
- One language per set: a full French set and a full English set (game language + mod labels).
- Mid-run states with meaningful numbers (not round 1 with zeros).
- Highlight the feature: dim or crop the rest, a callout box or zoom on the mod's panel.
- Game-coherent style: dark slate panel band, gold title (#EFC851), cream text (#FFF6E2), the game's
  Kreon font; nothing that looks like a generic web banner.
- 1920×1080 source, 16:9; headline readable at Workshop thumbnail size.

## Main image (Workshop thumbnail)

Square (the Workshop list and the item page show it square), legible at small size: the mod name, one
strong visual of the hero feature, game palette. Some mods use an animated GIF there: check what Steam
accepts for the main preview (ModUploader takes `image.png` < 1 MB; a GIF may need the web "Add/edit
images" page) before investing in an animation.

Decisions (2026-11-01): benefits 1-5 confirmed in this order; two invented player names; Kreon font;
card history from the user's real runs.

## Status

- [x] Benefits confirmed by the user
- [x] Tooling: player names, language, history demo, `sts2.py autoplay` (works in co-op on both ports)
- [x] Pipeline: `src/<n>.html?lang=fr|en` + `src/style.css` (Kreon, game palette) → `render.sh NAME [fr|en|all] [WxH]`
      (headless Edge) → `out/<lang>/<n>.png`; raw game captures in `captures/<lang>/`
- [x] 0 main (square 1080), 1 card choices, 2 party, 3 forecast, 4 recap, 5 Ledger — FR + EN, all from the latest build
- [x] User OK on the style (2026-11-01)
- [x] Max HP line: the console id is `PAPER_CUTS_POWER` (`PAPER_CUTS` silently applied nothing); the forecast shows "−3 max HP" (checked 2026-11-01)
- [x] Animated main image: `make-gif.sh` -> `out/<lang>/0_main.gif` (512 px, ~900 KB); uploaded 2026-11-01 as GIF bytes in
      `dist/workshop/image.png` (ModUploader only sends that name); the user confirmed it animates on the Workshop page

Capture recipe: LEDGER7 seed, Regent host "Kestrel" + Silent client "Moira", Ascension 5; card reward on floor 6
after the event fight shows a card already in the deck. Replay: `MK_LANG=fra|eng scripts/dev/marketing-run.sh SHOTNAME [over|only] [forecast]`.
Ledger (5): with only the LEDGER7 host files (`*_1.json`) of one language in `SpireLedger/runs` (the user's real
run set aside and restored), one `launch-local`, Compendium → Registre/Ledger → the Moira chip → a finished run.
