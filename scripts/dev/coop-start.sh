#!/usr/bin/env bash
# Start a local 2-player co-op test: two Steam-less instances connected over FastMP (localhost), both
# embarked into a seeded custom run. Host API on 15527, client on 15528.
# Usage: scripts/dev/coop-start.sh [SEED]   (needs STS2MCP installed: python scripts/dev/sts2.py install)
set -euo pipefail
cd "$(dirname "$0")/../.."
SEED="${1:-COOP1}"
D="python -I scripts/dev/sts2.py"

STS2MCP_PORT=15527 $D launch-local --fastmp host_custom
sleep 8
STS2MCP_PORT=15528 $D launch-local --fastmp join
# Wait for the client to show up in the host's lobby, then embark (lobby actions use the menu endpoint).
sleep 15
STS2MCP_PORT=15527 $D act custom_embark "seed=$SEED"
sleep 2
STS2MCP_PORT=15528 $D act custom_embark
sleep 12
STS2_API=multiplayer STS2MCP_PORT=15527 $D state | head -8
