#!/usr/bin/env bash
# Animated square main image: out/<lang>/0_main.png, then the five benefit scenes of src/0_anim.html,
# chained with short cross-fades and looped -> out/<lang>/0_main.gif (Steam previews must stay < 1 MB).
# Needs ffmpeg and the renders of 0_main (render.sh 0_main L 1080x1080).
# Usage: docs/marketing/make-gif.sh [fr|en|all] [SIZE=512]
set -euo pipefail
cd "$(dirname "$0")"
LANGS="${1:-all}"; SIZE="${2:-512}"
[ "$LANGS" = all ] && LANGS="fr en"
EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
HOLD=2.2 FADE=0.35 FPS=12
for L in $LANGS; do
  A="out/$L/anim"; mkdir -p "$A"
  cp "out/$L/0_main.png" "$A/0.png"
  for n in 1 2 3 4 5; do
    "$EDGE" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
      --allow-file-access-from-files --virtual-time-budget=3000 --window-size=1080,1080 \
      --screenshot="$(cygpath -w "$PWD/$A/$n.png")" "file:///$(cygpath -m "$PWD/src/0_anim.html")?lang=$L&scene=$n" 2>/dev/null
  done
  # 0 1 2 3 4 5 then 0 again, so the loop restarts on the main image without a jump.
  inputs=(); for n in 0 1 2 3 4 5 0; do inputs+=(-loop 1 -t "$(awk "BEGIN{print $HOLD + $FADE}")" -i "$A/$n.png"); done
  chain="[0]scale=$SIZE:$SIZE:flags=lanczos,fps=$FPS,format=rgb24[v0]"; prev=v0
  for i in 1 2 3 4 5 6; do
    chain+=";[$i]scale=$SIZE:$SIZE:flags=lanczos,fps=$FPS,format=rgb24[s$i]"
    chain+=";[$prev][s$i]xfade=transition=fade:duration=$FADE:offset=$(awk "BEGIN{print $i * $HOLD}")[v$i]"; prev=v$i
  done
  # Drop the trailing copy of the main image (the loop shows it again) and build one shared palette.
  chain+=";[$prev]trim=duration=$(awk "BEGIN{print 6 * $HOLD}"),split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle"
  ffmpeg -loglevel error -y "${inputs[@]}" -filter_complex "$chain" -loop 0 "out/$L/0_main.gif"
  echo "out/$L/0_main.gif $(( $(stat -c %s "out/$L/0_main.gif") / 1024 )) KB"
done
