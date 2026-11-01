#!/usr/bin/env bash
# Replays the LEDGER7 co-op run (Regent host "Kestrel", Silent client "Moira") up to the floor-6 card
# reward and screenshots it as $1 (e.g. card_fr). $2=over also plays to game over and shoots $1_over;
# $3=forecast first shoots $1_party and $1_forecast (with $2=only it stops there).
set -u
cd /k/Projets/spire-ledger
D="python -I scripts/dev/sts2.py"
export STS2MCP_PLAYER_NAMES="Kestrel,Moira" SPIRELEDGER_HISTORY_NETID=76561198040385943
$D quit >/dev/null; sleep 5
# MK_LANG=fra|eng sets the test profile language (only once no instance is left to rewrite it on exit).
[ -n "${MK_LANG:-}" ] && sed -i "s/\"language\": \"[a-z]*\"/\"language\": \"$MK_LANG\"/" "$APPDATA/SlayTheSpire2/default/1/settings.save"
STS2MCP_PORT=15527 $D launch-local --fastmp host_custom >/dev/null; sleep 8
STS2MCP_PORT=15528 $D launch-local --fastmp join >/dev/null; sleep 15
STS2MCP_PORT=15527 $D act custom_embark seed=LEDGER7 character=REGENT >/dev/null; sleep 1
STS2MCP_PORT=15528 $D act custom_embark character=SILENT >/dev/null; sleep 2
STS2MCP_PORT=15527 $D act custom_embark seed=LEDGER7 >/dev/null; sleep 12
export STS2_API=multiplayer
both() { (STS2MCP_PORT=15528 timeout 500 $D "$@" >/dev/null 2>&1 &); STS2MCP_PORT=15527 timeout 500 $D "$@"; }
both autoplay 3
H() { STS2MCP_PORT=15527 $D "$@"; }
H act claim_reward index=0 >/dev/null; sleep 1; H act claim_reward index=1 >/dev/null; sleep 2
H act select_card_reward card_index=2 >/dev/null; sleep 1
both autoplay 6 pick=2
for i in 1 0; do for P in 15527 15528; do STS2MCP_PORT=$P $D act choose_event_option index=$i >/dev/null; done; sleep 6; done
if [ "${3:-}" = forecast ]; then
  both fight 1; sleep 10; H shot "$1_party" >/dev/null
  H console "card BURN" >/dev/null; H console "card BURN" >/dev/null
  for i in 2 3 4 5; do H console "power PAPER_CUTS_POWER 2 $i" >/dev/null; done
  sleep 3; H shot "$1_forecast"
  [ "${2:-}" = only ] && exit 0
fi
both fight 20; sleep 4
H act claim_reward index=1 >/dev/null; sleep 2
H shot "$1"
if [ "${2:-}" = over ]; then
  H act select_card_reward card_index=0 >/dev/null; sleep 1
  both autoplay 30; sleep 5
  H shot "$1_over"
fi
