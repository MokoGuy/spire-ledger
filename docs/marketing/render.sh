#!/usr/bin/env bash
# Render the marketing images: src/<name>.html?lang=fr|en -> out/<lang>/<name>.png (1920x1080, or the
# size given as 3rd arg, e.g. 1080x1080 for the square main image), with headless Edge.
# Usage: docs/marketing/render.sh NAME [fr|en|all] [WxH]
set -euo pipefail
cd "$(dirname "$0")"
NAME="$1"; LANGS="${2:-all}"; SIZE="${3:-1920x1080}"
[ "$LANGS" = all ] && LANGS="fr en"
EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
for L in $LANGS; do
  mkdir -p "out/$L"
  "$EDGE" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
    --allow-file-access-from-files --virtual-time-budget=3000 \
    --window-size="${SIZE/x/,}" --screenshot="$(cygpath -w "$PWD/out/$L/$NAME.png")" \
    "file:///$(cygpath -m "$PWD/src/$NAME.html")?lang=$L" 2>/dev/null
  echo "out/$L/$NAME.png"
done
